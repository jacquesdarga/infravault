using System.Text;

namespace InfraVault.Application.Security;

/// <summary>
/// Données associées (AAD) liant chaque chiffré à son emplacement logique.
/// Un chiffré copié d'une ligne ou d'un champ vers un autre devient indéchiffrable :
/// l'échange silencieux de secrets entre deux fiches est donc détecté.
/// </summary>
public static class Aad
{
    public static byte[] VaultDek(Guid vaultId) => Encoding.UTF8.GetBytes($"infravault:v1:vault:{vaultId:N}:dek");
    public static byte[] VaultRecovery(Guid vaultId) => Encoding.UTF8.GetBytes($"infravault:v1:vault:{vaultId:N}:recovery");

    public static string CredentialUsername(Guid id) => $"credential:{id:N}:username";
    public static string CredentialSecret(Guid id) => $"credential:{id:N}:secret";
    public static string CredentialNotes(Guid id) => $"credential:{id:N}:notes";
    public static string CredentialVersion(Guid versionId) => $"credential-version:{versionId:N}";
    public static string AssetProperty(Guid assetId, string key) => $"asset:{assetId:N}:property:{key}";
    public static string DocumentContent(Guid id) => $"documentation:{id:N}:content";
    public static string DocumentVersion(Guid versionId) => $"documentation-version:{versionId:N}";
    public static string AttachmentName(Guid id) => $"attachment:{id:N}:name";
    public static string AttachmentContent(Guid id) => $"attachment:{id:N}:content";
    public const string BackupKey = "backup:key";
}
