using System.Security.Cryptography;
using InfraVault.Application.Interfaces;
using InfraVault.Infrastructure.Persistence;
using InfraVault.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;

namespace InfraVault.Infrastructure.Backup;

/// <summary>Emplacements locaux. Par défaut %LOCALAPPDATA%\InfraVault ; surchargeable (tests, coffre portable).</summary>
public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfraVault")
            : dataDirectory);
    }

    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "vault.db");
    public string DefaultBackupDirectory => Path.Combine(DataDirectory, "backups");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string TempDirectory => Path.Combine(DataDirectory, "tmp");
}

public sealed class FileStorageService(AppPaths paths) : IFileStorage
{
    public void EnsureDirectory(string directory) => Directory.CreateDirectory(directory);

    /// <summary>Écriture atomique : fichier temporaire puis renommage, pour ne jamais laisser un fichier à moitié écrit.</summary>
    public async Task WriteAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public Task<byte[]> ReadAsync(string path, CancellationToken ct = default) => File.ReadAllBytesAsync(path, ct);

    public async Task<byte[]> ReadPrefixAsync(string path, int maxBytes, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var buffer = new byte[(int)Math.Min(maxBytes, stream.Length)];
        await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        return buffer;
    }

    public IReadOnlyList<StoredFile> List(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).GetFiles(pattern)
            .Select(f => new StoredFile(f.FullName, f.Length, f.LastWriteTimeUtc)).ToList();
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public bool Exists(string path) => File.Exists(path);

    public string CreateTempFile()
    {
        Directory.CreateDirectory(paths.TempDirectory);
        return Path.Combine(paths.TempDirectory, Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + ".tmp");
    }
}

/// <summary>Opérations physiques sur le fichier SQLite du coffre.</summary>
public sealed class DatabaseFiles(VaultDbFactory factory, AppPaths paths, SqliteSearchIndex searchIndex) : IDatabaseFiles
{
    /// <summary>Instantané cohérent, y compris pendant l'utilisation (VACUUM INTO).</summary>
    public async Task SnapshotAsync(string destinationPath, CancellationToken ct = default)
    {
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        await using var connection = new SqliteConnection(factory.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path;";
        cmd.Parameters.AddWithValue("$path", destinationPath);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<Guid?> ValidateAsync(string path, CancellationToken ct = default)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        try
        {
            await using var connection = new SqliteConnection(cs);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals((await check.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    return null;
            }
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id FROM vaults LIMIT 1;";
            return Guid.TryParse((await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString(), out var id) ? id : null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    public async Task<string?> ReplaceDatabaseAsync(string sourcePath, CancellationToken ct = default)
    {
        // Libère toutes les connexions du pool avant de toucher aux fichiers.
        SqliteConnection.ClearAllPools();
        searchIndex.Reset();
        Directory.CreateDirectory(paths.DataDirectory);

        string? previous = null;
        var db = paths.DatabasePath;
        if (File.Exists(db))
        {
            previous = $"{db}.avant-restauration-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(db, previous, overwrite: true);
        }
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(db + suffix)) File.Delete(db + suffix);

        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
        await using (var target = new FileStream(db, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await source.CopyToAsync(target, ct).ConfigureAwait(false);
        }
        return previous;
    }
}
