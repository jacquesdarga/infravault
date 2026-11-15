using InfraVault.Domain.Entities;
using InfraVault.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace InfraVault.Application.Interfaces;

/// <summary>Unité de travail sur la base du coffre. Une instance par opération (courte durée de vie).</summary>
public interface IVaultDb : IDisposable, IAsyncDisposable
{
    DbSet<Vault> Vaults { get; }
    DbSet<User> Users { get; }
    DbSet<Setting> Settings { get; }
    DbSet<AssetType> AssetTypes { get; }
    DbSet<Asset> Assets { get; }
    DbSet<AssetProperty> AssetProperties { get; }
    DbSet<AssetRelationship> AssetRelationships { get; }
    DbSet<Credential> Credentials { get; }
    DbSet<CredentialVersion> CredentialVersions { get; }
    DbSet<CredentialAssetLink> CredentialAssetLinks { get; }
    DbSet<Documentation> Documentations { get; }
    DbSet<DocumentationVersion> DocumentationVersions { get; }
    DbSet<DocumentationAssetLink> DocumentationAssetLinks { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IVaultDbFactory
{
    IVaultDb Create();
    /// <summary>Crée le schéma si nécessaire et vérifie l'intégrité du fichier SQLite.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}

/// <summary>Emplacements locaux de l'application.</summary>
public interface IAppPaths
{
    string DataDirectory { get; }
    string DatabasePath { get; }
    string DefaultBackupDirectory { get; }
    string LogDirectory { get; }
}

/// <summary>Chiffrement authentifié AES-256-GCM. Seul point d'entrée cryptographique symétrique.</summary>
public interface IEncryptionService
{
    /// <summary>Chiffre et renvoie une « boîte scellée » : version | nonce | tag | chiffré.</summary>
    byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData);
    /// <summary>Ouvre une boîte scellée. Lève <c>IntegrityException</c> si la donnée ou la clé ne correspond pas.</summary>
    byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> box, ReadOnlySpan<byte> associatedData);
    (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptDetached(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData);
    byte[] DecryptDetached(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> associatedData);
}

/// <summary>Dérivation de clé résistante aux attaques par dictionnaire (Argon2id).</summary>
public interface IKeyDerivationService
{
    KdfParameters DefaultParameters { get; }
    byte[] DeriveKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, KdfParameters parameters);
}

public delegate T KeyFunc<out T>(ReadOnlySpan<byte> key);

/// <summary>
/// Session du coffre : détient la DEK en mémoire tant que le coffre est déverrouillé.
/// Après <see cref="Lock"/>, la clé est effacée et plus aucun secret ne peut être déchiffré.
/// </summary>
public interface IVaultSession
{
    bool IsUnlocked { get; }
    Guid VaultId { get; }
    Guid? UserId { get; }
    string VaultName { get; }
    string UserName { get; }
    DateTime? UnlockedAtUtc { get; }
    event EventHandler? StateChanged;

    void Open(Guid vaultId, string vaultName, Guid userId, string userName, ReadOnlySpan<byte> dek);
    void Lock();
    void EnsureUnlocked();

    byte[] Seal(ReadOnlySpan<byte> plaintext, string associatedData);
    byte[] SealString(string plaintext, string associatedData);
    byte[] Open(ReadOnlySpan<byte> box, string associatedData);
    string OpenString(ReadOnlySpan<byte> box, string associatedData);

    /// <summary>Index aveugle (HMAC, clé dédiée) d'une valeur normalisée, pour la recherche exacte.</summary>
    byte[] BlindIndex(string value);
    /// <summary>HMAC de scellement du journal d'audit (clé dédiée).</summary>
    byte[] AuditMac(ReadOnlySpan<byte> data);
    /// <summary>Accès contrôlé à la DEK (ré-enveloppement, sauvegardes). Ne jamais conserver la clé reçue.</summary>
    T UseKey<T>(KeyFunc<T> func);
}

public sealed record SearchDocument(
    string EntityType, Guid EntityId, string Category, string Title,
    string? Subtitle, string? Body, string? Tags, string? Icon, string? Color);

public sealed record SearchIndexHit(
    string EntityType, Guid EntityId, string Category, string Title, string? Subtitle, string? Icon, string? Color);

/// <summary>Index plein texte (SQLite FTS5). Ne reçoit jamais de secret ni de contenu déchiffré.</summary>
public interface ISearchIndex
{
    Task UpsertAsync(SearchDocument document, CancellationToken ct = default);
    Task UpsertManyAsync(IEnumerable<SearchDocument> documents, CancellationToken ct = default);
    Task RemoveAsync(string entityType, Guid entityId, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SearchIndexHit>> QueryAsync(string text, int limit, CancellationToken ct = default);
}

/// <summary>Opérations physiques sur le fichier SQLite (instantané cohérent, validation, remplacement).</summary>
public interface IDatabaseFiles
{
    Task SnapshotAsync(string destinationPath, CancellationToken ct = default);
    /// <summary>Vérifie l'intégrité d'un fichier SQLite et renvoie l'identifiant du coffre qu'il contient.</summary>
    Task<Guid?> ValidateAsync(string path, CancellationToken ct = default);
    /// <summary>Remplace la base courante ; l'ancienne est conservée à côté et son chemin est renvoyé.</summary>
    Task<string?> ReplaceDatabaseAsync(string sourcePath, CancellationToken ct = default);
}

public sealed record StoredFile(string Path, long Length, DateTime LastWriteUtc);

/// <summary>Accès fichiers hors base (sauvegardes, exports).</summary>
public interface IFileStorage
{
    void EnsureDirectory(string directory);
    Task WriteAtomicAsync(string path, ReadOnlyMemory<byte> content, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string path, CancellationToken ct = default);
    /// <summary>Lit au plus <paramref name="maxBytes"/> octets en début de fichier (en-têtes).</summary>
    Task<byte[]> ReadPrefixAsync(string path, int maxBytes, CancellationToken ct = default);
    IReadOnlyList<StoredFile> List(string directory, string pattern);
    void Delete(string path);
    bool Exists(string path);
    string CreateTempFile();
}

/// <summary>Presse-papiers avec effacement différé et exclusion de l'historique Windows.</summary>
public interface IClipboardService
{
    void CopySensitive(string text, TimeSpan clearAfter);
    void CopyPlain(string text);
    void ClearIfOwned();
}

/// <summary>Événements du poste (mise en veille, verrouillage de session Windows).</summary>
public interface IPlatformSecurityService : IDisposable
{
    event EventHandler? SystemSuspending;
    event EventHandler? SessionLocked;
    void Start();
}
