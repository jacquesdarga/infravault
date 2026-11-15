namespace InfraVault.Domain.Entities;

/// <summary>
/// Identifiant / secret. Seules les métadonnées utiles à la recherche restent en clair
/// (nom, type, URL, port, tags). Username, secret et notes sont chiffrés (AES-256-GCM).
/// </summary>
public class Credential
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string Name { get; set; } = "";
    public string CredentialType { get; set; } = "password";

    public byte[]? UsernameEncrypted { get; set; }
    /// <summary>Index aveugle HMAC du username normalisé : permet la recherche exacte sans le stocker en clair.</summary>
    public byte[]? UsernameIndex { get; set; }
    public byte[] EncryptedSecret { get; set; } = [];
    public byte[]? NotesEncrypted { get; set; }

    public string? Url { get; set; }
    public int? Port { get; set; }
    public string? Tags { get; set; }

    public int CurrentVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public List<CredentialVersion> Versions { get; set; } = [];
    public List<CredentialAssetLink> AssetLinks { get; set; } = [];
}

/// <summary>Version historisée d'un secret. Les anciennes versions restent chiffrées.</summary>
public class CredentialVersion
{
    public Guid Id { get; set; }
    public Guid CredentialId { get; set; }
    public byte[] EncryptedSecret { get; set; } = [];
    public int VersionNumber { get; set; }
    public string? ChangeReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Association N-N entre un identifiant et les ressources auxquelles il donne accès.</summary>
public class CredentialAssetLink
{
    public Guid CredentialId { get; set; }
    public Guid AssetId { get; set; }
    public string RelationshipType { get; set; } = "ACCESS";
}
