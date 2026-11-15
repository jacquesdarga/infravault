namespace InfraVault.Shared.Exceptions;

/// <summary>Erreur métier dont le message (français) peut être affiché tel quel à l'utilisateur.</summary>
public class InfraVaultException : Exception
{
    public InfraVaultException(string message) : base(message) { }
    public InfraVaultException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Une opération nécessitant le coffre déverrouillé a été tentée alors qu'il est verrouillé.</summary>
public sealed class VaultLockedException() : InfraVaultException("Le coffre est verrouillé.");

public sealed class InvalidMasterPasswordException()
    : InfraVaultException("Mot de passe maître incorrect.");

public sealed class InvalidRecoveryKeyException()
    : InfraVaultException("Clé de récupération invalide.");

/// <summary>Trop de tentatives échouées : il faut patienter avant de réessayer.</summary>
public sealed class AuthenticationThrottledException(TimeSpan retryAfter)
    : InfraVaultException($"Trop de tentatives échouées. Réessayez dans {Math.Ceiling(retryAfter.TotalSeconds)} s.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Un contenu chiffré a été altéré, tronqué ou déchiffré avec la mauvaise clé.</summary>
public sealed class IntegrityException : InfraVaultException
{
    public IntegrityException(string message = "Contrôle d'intégrité échoué : la donnée chiffrée est altérée ou la clé est incorrecte.")
        : base(message) { }
    public IntegrityException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ValidationFailedException(IReadOnlyList<string> errors)
    : InfraVaultException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class NotFoundException(string what) : InfraVaultException($"{what} introuvable.");

public sealed class BackupException : InfraVaultException
{
    public BackupException(string message) : base(message) { }
    public BackupException(string message, Exception inner) : base(message, inner) { }
}
