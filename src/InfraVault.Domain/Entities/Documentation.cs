namespace InfraVault.Domain.Entities;

/// <summary>Document technique. Le titre, le type et les tags sont indexables ; le contenu est chiffré.</summary>
public class Documentation
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public string Title { get; set; } = "";
    public byte[] ContentEncrypted { get; set; } = [];
    public string DocumentType { get; set; } = "Note";
    public string? Tags { get; set; }
    public int CurrentVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<DocumentationVersion> Versions { get; set; } = [];
    public List<DocumentationAssetLink> AssetLinks { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
}

public class DocumentationVersion
{
    public Guid Id { get; set; }
    public Guid DocumentationId { get; set; }
    public byte[] ContentEncrypted { get; set; } = [];
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DocumentationAssetLink
{
    public Guid DocumentationId { get; set; }
    public Guid AssetId { get; set; }
}

/// <summary>Pièce jointe : nom et contenu chiffrés.</summary>
public class Attachment
{
    public Guid Id { get; set; }
    public Guid DocumentationId { get; set; }
    public byte[] FilenameEncrypted { get; set; } = [];
    public byte[] ContentEncrypted { get; set; } = [];
    public string MimeType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
