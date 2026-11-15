namespace InfraVault.Shared.Constants;

public static class AppInfo
{
    public const string Name = "InfraVault";
    public const string Version = "1.0.0";
    public const string Tagline = "Know your infrastructure. Secure your access. Control your information.";
}

/// <summary>Paramètres cryptographiques figés pour la version 1 du format.</summary>
public static class CryptoConstants
{
    public const int CryptoVersion = 1;
    public const int KeySizeBytes = 32;      // AES-256
    public const int NonceSizeBytes = 12;    // 96 bits (GCM)
    public const int TagSizeBytes = 16;      // 128 bits
    public const int SaltSizeBytes = 16;
    public const int RecoveryKeyBytes = 20;  // 160 bits d'entropie, encodés en Base32
    public const byte SealedBoxVersion = 1;
    public const string KdfAlgorithm = "Argon2id";
}

public static class AssetTypeKeys
{
    public const string Datacenter = "datacenter";
    public const string Server = "server";
    public const string Hypervisor = "hypervisor";
    public const string VirtualMachine = "vm";
    public const string ContainerPlatform = "container_platform";
    public const string Stack = "stack";
    public const string Container = "container";
    public const string NetworkDevice = "network_device";
    public const string Storage = "storage";
    public const string Database = "database";
    public const string Application = "application";
    public const string Domain = "domain";
    public const string CloudResource = "cloud_resource";
    public const string Custom = "custom";
}

public static class AssetStatuses
{
    public const string Active = "Active";
    public const string Maintenance = "Maintenance";
    public const string Retired = "Retired";
    public const string Unknown = "Unknown";
    public static readonly IReadOnlyList<string> All = [Active, Maintenance, Retired, Unknown];
}

public static class DefaultEnvironments
{
    public static readonly IReadOnlyList<string> All = ["Production", "Staging", "Development", "Testing"];
}

public static class EntityTypes
{
    public const string Vault = "Vault";
    public const string Asset = "Asset";
    public const string AssetType = "AssetType";
    public const string Relationship = "Relationship";
    public const string Credential = "Credential";
    public const string Documentation = "Documentation";
    public const string Attachment = "Attachment";
    public const string Backup = "Backup";
    public const string Settings = "Settings";
}

public static class AuditEventTypes
{
    public const string VaultCreated = "VaultCreated";
    public const string Unlock = "Unlock";
    public const string Lock = "Lock";
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string RecoveryUsed = "RecoveryUsed";
    public const string MasterPasswordChanged = "MasterPasswordChanged";
    public const string RecoveryKeyRegenerated = "RecoveryKeyRegenerated";
    public const string AssetCreated = "AssetCreated";
    public const string AssetUpdated = "AssetUpdated";
    public const string AssetDeleted = "AssetDeleted";
    public const string SensitivePropertyViewed = "SensitivePropertyViewed";
    public const string RelationshipCreated = "RelationshipCreated";
    public const string RelationshipDeleted = "RelationshipDeleted";
    public const string CredentialCreated = "CredentialCreated";
    public const string CredentialUpdated = "CredentialUpdated";
    public const string CredentialDeleted = "CredentialDeleted";
    public const string SecretChanged = "SecretChanged";
    public const string SecretViewed = "SecretViewed";
    public const string SecretCopied = "SecretCopied";
    public const string SecretVersionRestored = "SecretVersionRestored";
    public const string DocumentationCreated = "DocumentationCreated";
    public const string DocumentationUpdated = "DocumentationUpdated";
    public const string DocumentationDeleted = "DocumentationDeleted";
    public const string AttachmentAdded = "AttachmentAdded";
    public const string AttachmentExported = "AttachmentExported";
    public const string AttachmentDeleted = "AttachmentDeleted";
    public const string BackupCreated = "BackupCreated";
    public const string BackupRestored = "BackupRestored";
    public const string BackupVerified = "BackupVerified";
    public const string Export = "Export";
    public const string Import = "Import";
    public const string SettingsChanged = "SettingsChanged";
    public const string AuditVerified = "AuditVerified";

    /// <summary>Événements de sécurité mis en avant dans l'interface.</summary>
    public static readonly IReadOnlySet<string> Security = new HashSet<string>
    {
        VaultCreated, Unlock, Lock, AuthenticationFailed, RecoveryUsed, MasterPasswordChanged,
        RecoveryKeyRegenerated, SecretViewed, SecretCopied, SensitivePropertyViewed, Export, Import,
        BackupRestored,
    };
}

public static class CredentialTypes
{
    public const string Password = "password";
    public const string Ssh = "ssh_account";
    public const string SshKey = "ssh_private_key";
    public const string Certificate = "certificate";
    public const string ApiToken = "api_token";
    public const string Database = "database";
    public const string Vpn = "vpn";
    public const string Admin = "admin_account";
    public const string Application = "application";
    public const string Custom = "custom";

    public static readonly IReadOnlyList<(string Key, string Label, string Icon)> All =
    [
        (Password, "Identifiant / mot de passe", "key"),
        (Ssh, "Compte SSH", "terminal"),
        (SshKey, "Clé SSH privée", "fingerprint"),
        (Certificate, "Certificat", "certificate"),
        (ApiToken, "Jeton d'API", "token"),
        (Database, "Accès base de données", "database"),
        (Vpn, "Accès VPN", "shield"),
        (Admin, "Compte administrateur", "crown"),
        (Application, "Identifiant d'application", "app"),
        (Custom, "Secret personnalisé", "lock"),
    ];

    public static string LabelOf(string key) => All.FirstOrDefault(t => t.Key == key).Label ?? key;
    public static string IconOf(string key) => All.FirstOrDefault(t => t.Key == key).Icon ?? "key";

    /// <summary>Types dont le secret est multi-ligne (clé, certificat).</summary>
    public static bool IsMultiline(string key) => key is SshKey or Certificate;
}

public static class DocumentTypes
{
    public static readonly IReadOnlyList<string> All =
        ["Procédure", "Configuration", "Note", "Runbook", "Architecture", "Incident", "Référence"];
}

/// <summary>Types de relations entre ressources. Les relations hiérarchiques alimentent l'arborescence.</summary>
public static class RelationshipTypes
{
    public const string Contains = "CONTAINS";
    public const string Hosts = "HOSTS";
    public const string Runs = "RUNS";
    public const string Uses = "USES";
    public const string ConnectsTo = "CONNECTS_TO";
    public const string DependsOn = "DEPENDS_ON";
    public const string BacksUpTo = "BACKS_UP_TO";
    public const string ReplicatesTo = "REPLICATES_TO";
    public const string Protects = "PROTECTS";

    public sealed record Definition(string Key, string Forward, string Inverse, bool IsHierarchical);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(Contains, "contient", "contenu dans", true),
        new(Hosts, "héberge", "hébergé sur", true),
        new(Runs, "exécute", "exécuté par", true),
        new(Uses, "utilise", "utilisé par", false),
        new(DependsOn, "dépend de", "requis par", false),
        new(ConnectsTo, "connecté à", "connecté à", false),
        new(BacksUpTo, "sauvegardé vers", "reçoit les sauvegardes de", false),
        new(ReplicatesTo, "réplique vers", "réplica de", false),
        new(Protects, "protège", "protégé par", false),
    ];

    public static Definition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
    public static bool IsHierarchical(string key) => Find(key)?.IsHierarchical ?? false;
    public static bool IsKnown(string key) => Find(key) is not null;

    /// <summary>Relation hiérarchique proposée par défaut selon le type du parent.</summary>
    public static string DefaultFor(string parentTypeKey) => parentTypeKey switch
    {
        AssetTypeKeys.Server or AssetTypeKeys.Hypervisor or AssetTypeKeys.Storage or AssetTypeKeys.CloudResource => Hosts,
        AssetTypeKeys.VirtualMachine or AssetTypeKeys.ContainerPlatform or AssetTypeKeys.Container => Runs,
        AssetTypeKeys.Stack => Contains,
        _ => Contains,
    };
}

public static class SettingKeys
{
    public const string VaultSettings = "vault.settings";
    public const string SchemaVersion = "schema.version";
    public const string AuthFailures = "auth.failures";
    public const string LastBackup = "backup.last";
}

public static class Limits
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 4000;
    public const int MasterPasswordMinLength = 12;
    public const long AttachmentMaxBytes = 10 * 1024 * 1024;
    public const int AuditTextMaxLength = 160;
}
