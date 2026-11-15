using InfraVault.Application;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using InfraVault.Domain.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Infrastructure;
using InfraVault.Infrastructure.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraVault.Tests.Shared;

/// <summary>Horloge pilotée par le test : indispensable pour l'anti force brute et les expirations.</summary>
public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// Un coffre jetable par test : dossier de données propre, base SQLite réelle, services câblés
/// comme en production. Le KDF est volontairement réglé au minimum acceptable pour que les tests
/// non cryptographiques ne paient pas le coût d'Argon2id.
/// </summary>
public sealed class VaultHarness : IAsyncDisposable
{
    /// <summary>Conforme à MasterPasswordPolicy (12 caractères minimum, score >= 2).</summary>
    public const string MasterPassword = "Xq7!mB2vLp9#aZ";

    public const string NewMasterPassword = "Vt4?nW8kRj3$eQ";

    private VaultHarness(string dataDirectory, ServiceProvider services, TestClock clock)
    {
        DataDirectory = dataDirectory;
        Services = services;
        Clock = clock;
    }

    public string DataDirectory { get; }
    public ServiceProvider Services { get; }
    public TestClock Clock { get; }

    /// <summary>Renseignée par <see cref="CreateVaultAsync"/>, affichée une seule fois en production.</summary>
    public string RecoveryKey { get; private set; } = "";

    public Guid VaultId { get; private set; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public AuthenticationService Auth => Get<AuthenticationService>();
    public IVaultSession Session => Get<IVaultSession>();
    public IVaultDbFactory DbFactory => Get<IVaultDbFactory>();
    public IAppPaths Paths => Get<IAppPaths>();

    /// <param name="fastKdf">false pour exercer les vrais paramètres Argon2id (lent, réservé aux tests crypto).</param>
    public static async Task<VaultHarness> StartAsync(bool fastKdf = true, CancellationToken ct = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "infravault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var clock = new TestClock();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // IClock est enregistré par TryAddSingleton côté Application : il faut le devancer.
        services.AddSingleton<IClock>(clock);

        services.AddInfraVaultApplication();
        services.AddInfraVaultInfrastructure(directory);

        if (fastKdf)
        {
            // 8 Mio / 1 passe / 1 voie : le minimum que KdfParameters.EnsureValid accepte.
            services.AddSingleton<IKeyDerivationService>(
                new Argon2KeyDerivationService(new KdfParameters(8 * 1024, 1, 1)));
        }

        var provider = services.BuildServiceProvider();
        var harness = new VaultHarness(directory, provider, clock);
        await harness.DbFactory.InitializeAsync(ct).ConfigureAwait(false);
        return harness;
    }

    /// <summary>Démarre un coffre déjà créé et déverrouillé — le point de départ de la plupart des tests.</summary>
    public static async Task<VaultHarness> StartUnlockedAsync(bool fastKdf = true, CancellationToken ct = default)
    {
        var harness = await StartAsync(fastKdf, ct).ConfigureAwait(false);
        await harness.CreateVaultAsync(ct: ct).ConfigureAwait(false);
        return harness;
    }

    public async Task<CreateVaultResult> CreateVaultAsync(
        string name = "Coffre de test", string password = MasterPassword, CancellationToken ct = default)
    {
        var result = await Auth.CreateVaultAsync(name, password, ct).ConfigureAwait(false);
        RecoveryKey = result.RecoveryKey;
        VaultId = result.VaultId;
        return result;
    }

    /// <summary>Lit la base hors EF Core, comme le ferait quelqu'un qui aurait volé le fichier.</summary>
    public SqliteConnection OpenRawDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>Octets bruts du fichier SQLite, WAL inclus : ce qu'un attaquant lirait réellement.</summary>
    public async Task<byte[]> ReadDatabaseBytesAsync(CancellationToken ct = default)
    {
        // Rapatrie le WAL dans le fichier principal pour que rien n'échappe à l'inspection.
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var parts = new List<byte>();
        foreach (var path in new[] { Paths.DatabasePath, Paths.DatabasePath + "-wal" })
            if (File.Exists(path))
                parts.AddRange(await ReadSharedAsync(path, ct).ConfigureAwait(false));
        return [.. parts];
    }

    /// <summary>
    /// Lit un fichier que SQLite garde ouvert en écriture. Sous Windows, l'ouverture par défaut
    /// (<c>FileShare.Read</c>) est refusée dans ce cas : il faut accepter explicitement les autres
    /// titulaires du fichier. Sous Linux le problème ne se pose pas, d'où un échec visible
    /// uniquement sur la plateforme que le produit cible.
    /// </summary>
    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync().ConfigureAwait(false);
        // Sans cela, SQLite garde le fichier ouvert et la suppression échoue sous Windows.
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }
}
