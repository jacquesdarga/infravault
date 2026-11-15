namespace InfraVault.Domain.Interfaces;

/// <summary>Horloge injectable (UTC) pour rendre le temps testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
