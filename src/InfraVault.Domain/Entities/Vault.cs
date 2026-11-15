namespace InfraVault.Domain.Entities;

/// <summary>
/// Coffre-fort : porte uniquement les métadonnées cryptographiques (enveloppes de la DEK).
/// Ni le Master Password, ni la KEK, ni la DEK en clair ne sont jamais persistés.
/// </summary>
public class Vault
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    // Enveloppe principale : DEK chiffrée par la KEK dérivée du Master Password (Argon2id).
    public byte[] Salt { get; set; } = [];
    public string KdfAlgorithm { get; set; } = "Argon2id";
    public string KdfParameters { get; set; } = "";
    public byte[] EncryptedDek { get; set; } = [];
    public byte[] DekNonce { get; set; } = [];
    public byte[] DekTag { get; set; } = [];
    public int CryptoVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }

    // Enveloppe de récupération : la même DEK chiffrée par une clé dérivée de la clé de récupération.
    public byte[]? RecoverySalt { get; set; }
    public byte[]? RecoveryEncryptedDek { get; set; }
    public byte[]? RecoveryNonce { get; set; }
    public byte[]? RecoveryTag { get; set; }
    public DateTime? RecoveryCreatedAt { get; set; }

    // Scellé du journal d'audit : HMAC (clé dérivée de la DEK) de la tête de chaîne.
    public long AuditSealSequence { get; set; }
    public byte[]? AuditSeal { get; set; }

    public bool HasRecoveryKey => RecoveryEncryptedDek is { Length: > 0 };
}

/// <summary>Utilisateur local. Prépare l'évolution vers le mode équipe.</summary>
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Paramètre non sensible du coffre (clé/valeur JSON).</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
