using System.Text;
using InfraVault.Application.Interfaces;
using InfraVault.Domain.Entities;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Infrastructure.Persistence;

public sealed class VaultDbContext(DbContextOptions<VaultDbContext> options) : DbContext(options), IVaultDb
{
    public DbSet<Vault> Vaults => Set<Vault>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetProperty> AssetProperties => Set<AssetProperty>();
    public DbSet<AssetRelationship> AssetRelationships => Set<AssetRelationship>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<CredentialVersion> CredentialVersions => Set<CredentialVersion>();
    public DbSet<CredentialAssetLink> CredentialAssetLinks => Set<CredentialAssetLink>();
    public DbSet<Documentation> Documentations => Set<Documentation>();
    public DbSet<DocumentationVersion> DocumentationVersions => Set<DocumentationVersion>();
    public DbSet<DocumentationAssetLink> DocumentationAssetLinks => Set<DocumentationAssetLink>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Vault>(e => { e.ToTable("vaults"); e.HasKey(x => x.Id); });
        b.Entity<User>(e => { e.ToTable("users"); e.HasKey(x => x.Id); });
        b.Entity<Setting>(e => { e.ToTable("settings"); e.HasKey(x => x.Key); });

        b.Entity<AssetType>(e =>
        {
            e.ToTable("asset_types");
            e.HasIndex(x => x.Key).IsUnique();
        });

        b.Entity<Asset>(e =>
        {
            e.ToTable("assets");
            e.Property(x => x.Name).HasMaxLength(Limits.NameMaxLength);
            e.HasOne(x => x.AssetType).WithMany().HasForeignKey(x => x.AssetTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vault>().WithMany().HasForeignKey(x => x.VaultId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Properties).WithOne().HasForeignKey(p => p.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.Hostname);
            e.HasIndex(x => x.IpAddress);
            e.HasIndex(x => x.AssetTypeId);
            e.HasIndex(x => x.UpdatedAt);
        });

        b.Entity<AssetProperty>(e =>
        {
            e.ToTable("asset_properties");
            e.HasIndex(x => new { x.AssetId, x.PropertyKey }).IsUnique();
        });

        b.Entity<AssetRelationship>(e =>
        {
            e.ToTable("asset_relationships");
            // Suppressions contrôlées : une ressource encore reliée ne peut pas disparaître silencieusement.
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.SourceAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.TargetAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SourceAssetId, x.TargetAssetId, x.RelationshipType }).IsUnique();
            e.HasIndex(x => x.TargetAssetId);
            e.ToTable(t => t.HasCheckConstraint("ck_relationship_not_self", "source_asset_id <> target_asset_id"));
        });

        b.Entity<Credential>(e =>
        {
            e.ToTable("credentials");
            e.Property(x => x.Name).HasMaxLength(Limits.NameMaxLength);
            e.HasOne<Vault>().WithMany().HasForeignKey(x => x.VaultId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.CredentialId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AssetLinks).WithOne().HasForeignKey(l => l.CredentialId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.CredentialType);
            e.HasIndex(x => x.UsernameIndex);
            e.HasIndex(x => x.ExpiresAt);
        });

        b.Entity<CredentialVersion>(e =>
        {
            e.ToTable("credential_versions");
            e.HasIndex(x => new { x.CredentialId, x.VersionNumber }).IsUnique();
        });

        b.Entity<CredentialAssetLink>(e =>
        {
            e.ToTable("credential_asset_links");
            e.HasKey(x => new { x.CredentialId, x.AssetId });
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AssetId);
        });

        b.Entity<Documentation>(e =>
        {
            e.ToTable("documentations");
            e.HasOne<Vault>().WithMany().HasForeignKey(x => x.VaultId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.DocumentationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AssetLinks).WithOne().HasForeignKey(l => l.DocumentationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.DocumentationId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Title);
            e.HasIndex(x => x.UpdatedAt);
        });

        b.Entity<DocumentationVersion>(e =>
        {
            e.ToTable("documentation_versions");
            e.HasIndex(x => new { x.DocumentationId, x.VersionNumber }).IsUnique();
        });

        b.Entity<DocumentationAssetLink>(e =>
        {
            e.ToTable("documentation_asset_links");
            e.HasKey(x => new { x.DocumentationId, x.AssetId });
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AssetId);
        });

        b.Entity<Attachment>(e => e.ToTable("attachments"));

        b.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasIndex(x => x.Sequence).IsUnique();
            e.HasIndex(x => x.EntityId);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.EventType);
        });

        // Convention : colonnes en snake_case, conformément au modèle de données du cahier des charges.
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            property.SetColumnName(ToSnakeCase(property.Name));
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 6);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>Fabrique de contextes et initialisation de la base locale.</summary>
public sealed class VaultDbFactory : IVaultDbFactory
{
    private readonly IAppPaths _paths;
    private readonly DbContextOptions<VaultDbContext> _options;

    public VaultDbFactory(IAppPaths paths)
    {
        _paths = paths;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            ForeignKeys = true,
            Pooling = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        _options = new DbContextOptionsBuilder<VaultDbContext>().UseSqlite(ConnectionString).Options;
    }

    public string ConnectionString { get; }

    public IVaultDb Create() => new VaultDbContext(_options);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        try
        {
            await using var db = new VaultDbContext(_options);
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

            var connection = (SqliteConnection)db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct).ConfigureAwait(false);

            await using (var pragma = connection.CreateCommand())
            {
                // WAL : écritures atomiques et reprise propre après une fermeture inattendue.
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check;";
                var result = (await check.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new IntegrityException("La base de données locale est corrompue. Restaurez une sauvegarde.");
            }

            if (!await db.Settings.AnyAsync(s => s.Key == SettingKeys.SchemaVersion, ct).ConfigureAwait(false))
            {
                db.Settings.Add(new Setting { Key = SettingKeys.SchemaVersion, Value = "1" });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        catch (SqliteException ex)
        {
            throw new IntegrityException("La base de données locale est illisible ou corrompue. Restaurez une sauvegarde.", ex);
        }
    }
}
