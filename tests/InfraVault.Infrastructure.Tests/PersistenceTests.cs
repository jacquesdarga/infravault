using InfraVault.Domain.Entities;
using InfraVault.Shared.Constants;
using InfraVault.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Infrastructure.Tests;

/// <summary>
/// La base locale est le seul endroit où vivent les données. Son schéma, son mode journal
/// et ses contraintes d'intégrité référentielle sont vérifiés sur le fichier réellement produit.
/// </summary>
public sealed class PersistenceTests
{
    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    [Fact]
    public async Task L_initialisation_cree_le_fichier_de_base()
    {
        await using var harness = await VaultHarness.StartAsync();

        Assert.True(File.Exists(harness.Paths.DatabasePath));
    }

    [Fact]
    public async Task L_initialisation_est_repetable_sans_dommage()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        // Au démarrage suivant de l'application, la base existe déjà.
        await harness.DbFactory.InitializeAsync();
        await harness.DbFactory.InitializeAsync();

        await using var db = harness.DbFactory.Create();
        Assert.Equal(1, await db.Vaults.CountAsync());
    }

    [Fact]
    public async Task La_base_est_en_mode_WAL()
    {
        await using var harness = await VaultHarness.StartAsync();

        await using var connection = harness.OpenRawDatabase();

        // WAL : une coupure de courant pendant une écriture ne corrompt pas le coffre.
        Assert.Equal("wal", (await ScalarAsync(connection, "PRAGMA journal_mode;"))?.ToLowerInvariant());
    }

    [Fact]
    public async Task La_version_de_schema_est_enregistree()
    {
        await using var harness = await VaultHarness.StartAsync();

        await using var db = harness.DbFactory.Create();
        var version = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeys.SchemaVersion);

        Assert.Equal("1", version?.Value);
    }

    [Fact]
    public async Task Le_controle_d_integrite_passe_sur_une_base_neuve()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await using var connection = harness.OpenRawDatabase();

        Assert.Equal("ok", (await ScalarAsync(connection, "PRAGMA integrity_check;"))?.ToLowerInvariant());
    }

    [Theory]
    [InlineData("vaults")]
    [InlineData("users")]
    [InlineData("settings")]
    [InlineData("asset_types")]
    [InlineData("assets")]
    [InlineData("asset_properties")]
    [InlineData("asset_relationships")]
    [InlineData("credentials")]
    [InlineData("credential_versions")]
    [InlineData("credential_asset_links")]
    [InlineData("documentations")]
    [InlineData("documentation_versions")]
    [InlineData("documentation_asset_links")]
    [InlineData("attachments")]
    [InlineData("audit_events")]
    public async Task Les_tables_du_modele_de_donnees_existent(string table)
    {
        await using var harness = await VaultHarness.StartAsync();

        await using var connection = harness.OpenRawDatabase();
        var found = await ScalarAsync(connection, $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';");

        Assert.Equal(table, found);
    }

    [Theory]
    [InlineData("assets", "asset_type_id")]
    [InlineData("assets", "ip_address")]
    [InlineData("assets", "internet_exposed")]
    [InlineData("assets", "public_address")]
    [InlineData("assets", "exposed_ports")]
    [InlineData("assets", "updated_at")]
    [InlineData("vaults", "encrypted_dek")]
    [InlineData("vaults", "dek_nonce")]
    [InlineData("vaults", "kdf_parameters")]
    [InlineData("vaults", "recovery_encrypted_dek")]
    [InlineData("credentials", "username_index")]
    [InlineData("audit_events", "event_type")]
    public async Task Les_colonnes_suivent_la_convention_snake_case(string table, string column)
    {
        await using var harness = await VaultHarness.StartAsync();

        await using var connection = harness.OpenRawDatabase();
        var found = await ScalarAsync(connection,
            $"SELECT name FROM pragma_table_info('{table}') WHERE name='{column}';");

        Assert.Equal(column, found);
    }

    [Fact]
    public async Task Les_cles_etrangeres_sont_appliquees()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await using var db = harness.DbFactory.Create();
        db.Assets.Add(new Asset
        {
            Id = Guid.NewGuid(),
            VaultId = harness.VaultId,
            AssetTypeId = Guid.NewGuid(), // catégorie inexistante
            Name = "Orpheline",
            CreatedAt = harness.Clock.UtcNow,
            UpdatedAt = harness.Clock.UtcNow,
        });

        // Sans contrainte active, cette ressource existerait en pointant vers le vide.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Une_ressource_ne_peut_pas_etre_reliee_a_elle_meme()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var assetId = await CreateAssetAsync(harness);

        await using var db = harness.DbFactory.Create();
        db.AssetRelationships.Add(new AssetRelationship
        {
            Id = Guid.NewGuid(),
            SourceAssetId = assetId,
            TargetAssetId = assetId,
            RelationshipType = RelationshipTypes.Hosts,
            CreatedAt = harness.Clock.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deux_categories_ne_peuvent_pas_partager_la_meme_cle()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await using var db = harness.DbFactory.Create();
        db.AssetTypes.Add(new AssetType { Id = Guid.NewGuid(), Key = AssetTypeKeys.Server, Name = "Doublon" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Les_categories_systeme_sont_livrees_avec_le_coffre()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await using var db = harness.DbFactory.Create();
        var keys = await db.AssetTypes.AsNoTracking().Select(t => t.Key).ToListAsync();

        Assert.Contains(AssetTypeKeys.Server, keys);
        Assert.Contains(AssetTypeKeys.VirtualMachine, keys);
        Assert.Contains(AssetTypeKeys.Stack, keys);
        Assert.All(keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
    }

    [Fact]
    public async Task Un_utilisateur_local_est_cree_avec_le_coffre()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await using var db = harness.DbFactory.Create();

        Assert.Equal(1, await db.Users.CountAsync());
        Assert.NotNull(harness.Session.UserId);
    }

    private static async Task<Guid> CreateAssetAsync(VaultHarness harness)
    {
        await using var db = harness.DbFactory.Create();
        var typeId = await db.AssetTypes.Where(t => t.Key == AssetTypeKeys.Server).Select(t => t.Id).FirstAsync();
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            VaultId = harness.VaultId,
            AssetTypeId = typeId,
            Name = "SRV-TEST-01",
            CreatedAt = harness.Clock.UtcNow,
            UpdatedAt = harness.Clock.UtcNow,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }
}
