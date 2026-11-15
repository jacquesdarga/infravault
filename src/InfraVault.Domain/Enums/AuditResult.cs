namespace InfraVault.Domain.Enums;

public enum AuditResult { Success, Failure }

/// <summary>État d'expiration d'un identifiant, calculé à la lecture.</summary>
public enum ExpiryState { None, Valid, ExpiringSoon, Expired }

public static class ExpiryRules
{
    public const int WarningDays = 30;

    public static ExpiryState Evaluate(DateTime? expiresAtUtc, DateTime nowUtc)
    {
        if (expiresAtUtc is null) return ExpiryState.None;
        if (expiresAtUtc <= nowUtc) return ExpiryState.Expired;
        return expiresAtUtc <= nowUtc.AddDays(WarningDays) ? ExpiryState.ExpiringSoon : ExpiryState.Valid;
    }
}
