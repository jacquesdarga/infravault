using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace InfraVault.Domain.Entities;

/// <summary>
/// Événement d'audit. Les événements forment une chaîne de hachage : chaque événement
/// engage le précédent, ce qui rend détectable toute modification ou suppression silencieuse.
/// Aucun secret ne doit jamais figurer dans <see cref="Metadata"/>.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid VaultId { get; set; }
    public Guid? UserId { get; set; }
    public long Sequence { get; set; }
    public string EventType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Result { get; set; } = "Success";
    public string? Metadata { get; set; }
    public byte[] PreviousHash { get; set; } = [];
    public byte[] Hash { get; set; } = [];

    /// <summary>Représentation canonique engagée dans la chaîne.</summary>
    public string Canonical() => string.Join('|',
        Sequence.ToString(CultureInfo.InvariantCulture),
        Id.ToString("N"),
        VaultId.ToString("N"),
        UserId?.ToString("N") ?? "",
        EventType,
        EntityType,
        EntityId?.ToString("N") ?? "",
        DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        Result,
        Metadata ?? "");

    public byte[] ComputeHash()
    {
        var canonical = Encoding.UTF8.GetBytes(Canonical());
        var buffer = new byte[PreviousHash.Length + canonical.Length];
        PreviousHash.CopyTo(buffer, 0);
        canonical.CopyTo(buffer, PreviousHash.Length);
        return SHA256.HashData(buffer);
    }
}
