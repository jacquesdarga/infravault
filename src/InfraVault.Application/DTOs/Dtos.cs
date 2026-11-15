using InfraVault.Domain.Enums;
using InfraVault.Domain.ValueObjects;

namespace InfraVault.Application.DTOs;

// ───────────────────────── Coffre & paramètres ─────────────────────────

public sealed record VaultInfo(Guid Id, string Name, DateTime CreatedAt, DateTime? PasswordChangedAt,
    bool HasRecoveryKey, DateTime? RecoveryCreatedAt, string KdfAlgorithm, KdfParameters Kdf, int CryptoVersion);

public sealed record CreateVaultResult(Guid VaultId, string RecoveryKey);

public sealed record PasswordGeneratorOptions
{
    public int Length { get; init; } = 24;
    public bool Lowercase { get; init; } = true;
    public bool Uppercase { get; init; } = true;
    public bool Digits { get; init; } = true;
    public bool Symbols { get; init; } = true;
    public bool ExcludeAmbiguous { get; init; } = true;
}

/// <summary>Paramètres du coffre (non sensibles), persistés en JSON.</summary>
public sealed record VaultSettings
{
    public int AutoLockMinutes { get; init; } = 10;
    public bool LockOnMinimize { get; init; }
    public bool LockOnSuspend { get; init; } = true;
    public bool LockOnSessionLock { get; init; } = true;
    public int ClipboardClearSeconds { get; init; } = 30;
    public int SecretRevealSeconds { get; init; } = 20;
    public bool AutoBackupEnabled { get; init; } = true;
    public int AutoBackupIntervalHours { get; init; } = 24;
    public string? BackupDirectory { get; init; }
    public int BackupRetention { get; init; } = 10;
    public List<string> Environments { get; init; } = ["Production", "Staging", "Development", "Testing"];
    public PasswordGeneratorOptions Generator { get; init; } = new();
}

// ───────────────────────── Infrastructure ─────────────────────────

public sealed record AssetTypeDto(Guid Id, string Key, string Name, string? Description, string Icon, string Color,
    bool IsSystem, int SortOrder, IReadOnlyList<FieldDefinition> Fields, int AssetCount = 0);

public sealed record AssetTypeInput(Guid? Id, string Name, string? Description, string Icon, string Color,
    IReadOnlyList<FieldDefinition> Fields);

public sealed record AssetSummaryDto(Guid Id, string Name, string TypeKey, string TypeName, string Icon, string Color,
    string? Hostname, string? IpAddress, string Environment, string Status, string? Tags, DateTime UpdatedAt,
    bool InternetExposed = false, string? PublicAddress = null, string? ExposedPorts = null);

public sealed record AssetPropertyDto(string Key, string Label, string? Group, FieldKind Kind, bool IsSensitive,
    string? Value, bool HasValue);

public sealed record AssetDetailDto(AssetSummaryDto Summary, Guid TypeId, string? Description, bool HasCustomIcon,
    DateTime CreatedAt, IReadOnlyList<AssetPropertyDto> Properties, IReadOnlyList<AssetSummaryDto> Path);

public sealed record AssetInput
{
    public Guid? Id { get; init; }
    public Guid AssetTypeId { get; init; }
    public string Name { get; init; } = "";
    public string? Hostname { get; init; }
    public string? IpAddress { get; init; }
    public string? Description { get; init; }
    public string Environment { get; init; } = "Production";
    public string Status { get; init; } = "Active";
    public string? Tags { get; init; }
    public bool InternetExposed { get; init; }
    public string? PublicAddress { get; init; }
    public string? ExposedPorts { get; init; }
    public string? Icon { get; init; }
    public string? IconColor { get; init; }
    /// <summary>Valeurs des champs spécifiques. Une clé sensible absente du dictionnaire est conservée telle quelle.</summary>
    public Dictionary<string, string?> Properties { get; init; } = [];
    /// <summary>À la création : rattache la ressource à un parent par une relation hiérarchique.</summary>
    public Guid? ParentId { get; init; }
    public string? ParentRelationType { get; init; }
}

public sealed record AssetFilter(string? TypeKey = null, string? Environment = null, string? Status = null, string? Text = null);

public sealed record TreeNodeDto(Guid AssetId, string Name, string TypeKey, string Icon, string Color, string Status,
    string? RelationType, IReadOnlyList<TreeNodeDto> Children);

public sealed record RelationshipDto(Guid Id, string Type, string Label, bool IsOutgoing, bool IsHierarchical,
    AssetSummaryDto Other, string? Description, string? Ports = null);

public sealed record DeleteImpact(string AssetName, int ChildCount, int RelationshipCount, int CredentialLinkCount,
    int DocumentationLinkCount)
{
    public bool HasDependencies => ChildCount + RelationshipCount + CredentialLinkCount + DocumentationLinkCount > 0;
}

// ───────────────────────── Identifiants ─────────────────────────

public sealed record CredentialSummaryDto(Guid Id, string Name, string Type, string TypeLabel, string Icon, string? Url,
    int? Port, string? Tags, DateTime? ExpiresAt, ExpiryState Expiry, DateTime UpdatedAt, int CurrentVersion,
    int LinkedAssetCount, bool HasUsername);

public sealed record CredentialVersionDto(Guid Id, int VersionNumber, string? ChangeReason, DateTime CreatedAt, bool IsCurrent);

public sealed record CredentialDetailDto(CredentialSummaryDto Summary, DateTime CreatedAt, bool HasNotes,
    IReadOnlyList<AssetSummaryDto> Assets, IReadOnlyList<CredentialVersionDto> Versions);

public sealed record CredentialInput
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "password";
    public string? Username { get; init; }
    /// <summary>Null en modification = secret inchangé (aucune nouvelle version).</summary>
    public string? Secret { get; init; }
    public string? Notes { get; init; }
    public string? Url { get; init; }
    public int? Port { get; init; }
    public string? Tags { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? ChangeReason { get; init; }
    public IReadOnlyList<Guid> AssetIds { get; init; } = [];
}

/// <summary>Contenu déchiffré d'une fiche, chargé uniquement pour l'édition.</summary>
public sealed record CredentialEditData(string? Username, string? Notes);

// ───────────────────────── Documentation ─────────────────────────

public sealed record DocumentSummaryDto(Guid Id, string Title, string DocumentType, string? Tags, DateTime UpdatedAt,
    int CurrentVersion, int LinkedAssetCount, int AttachmentCount);

public sealed record DocumentVersionDto(Guid Id, int VersionNumber, DateTime CreatedAt, bool IsCurrent);

public sealed record AttachmentDto(Guid Id, string FileName, string MimeType, long SizeBytes, DateTime CreatedAt);

public sealed record DocumentDetailDto(DocumentSummaryDto Summary, string Content, DateTime CreatedAt,
    IReadOnlyList<AssetSummaryDto> Assets, IReadOnlyList<DocumentVersionDto> Versions,
    IReadOnlyList<AttachmentDto> Attachments);

public sealed record DocumentInput
{
    public Guid? Id { get; init; }
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public string DocumentType { get; init; } = "Note";
    public string? Tags { get; init; }
    public IReadOnlyList<Guid> AssetIds { get; init; } = [];
}

// ───────────────────────── Recherche ─────────────────────────

public sealed record SearchHit(string EntityType, Guid EntityId, string Title, string? Subtitle, string? Icon, string? Color);

public sealed record SearchGroup(string Category, IReadOnlyList<SearchHit> Hits);

public sealed record SearchResults(string Query, IReadOnlyList<SearchGroup> Groups, int Total, TimeSpan Elapsed);

// ───────────────────────── Audit ─────────────────────────

public sealed record AuditEventDto(Guid Id, long Sequence, string EventType, string EventLabel, string EntityType,
    Guid? EntityId, DateTime TimestampUtc, bool Success, string? Label, string? Detail, string UserName, bool IsSecurity);

public sealed record AuditQuery(Guid? EntityId = null, string? EventType = null, bool SecurityOnly = false,
    bool FailuresOnly = false, DateTime? FromUtc = null, int Take = 200, int Skip = 0);

public sealed record AuditVerification(bool IsValid, long EventCount, long? BrokenAtSequence, bool SealChecked, string Message);

// ───────────────────────── Sauvegardes ─────────────────────────

public sealed record BackupInfo(string Path, string FileName, DateTime CreatedAtUtc, long SizeBytes, bool Automatic,
    Guid VaultId, string VaultName, int Assets, int Credentials, int Documents, string AppVersion);

public sealed record BackupVerification(bool IsValid, bool FullyVerified, string Message);

public sealed record ImportResult(int Created, int Skipped, IReadOnlyList<string> Warnings);

// ───────────────────────── Tableau de bord & administration ─────────────────────────

public sealed record TypeCount(string TypeKey, string TypeName, string Icon, string Color, int Count);

public sealed record DashboardDto(
    int AssetCount, int ServerCount, int VmCount, int ApplicationCount, int DatabaseCount, int CredentialCount,
    int DocumentCount, int RelationshipCount,
    IReadOnlyList<TypeCount> ByType,
    IReadOnlyList<(string Environment, int Count)> ByEnvironment,
    IReadOnlyList<AssetSummaryDto> RecentAssets,
    IReadOnlyList<CredentialSummaryDto> ExpiringCredentials,
    IReadOnlyList<AuditEventDto> RecentEvents,
    DateTime? LastBackupUtc, bool BackupOverdue);

public sealed record TagUsage(string Tag, int Assets, int Credentials, int Documents)
{
    public int Total => Assets + Credentials + Documents;
}
