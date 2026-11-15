using System.Text.Json;

namespace InfraVault.Domain.ValueObjects;

/// <summary>Paramètres Argon2id. Persistés avec le coffre pour pouvoir évoluer sans casser les coffres existants.</summary>
public sealed record KdfParameters(int MemoryKiB, int Iterations, int Parallelism, int OutputLength = 32)
{
    public const int MinMemoryKiB = 8 * 1024;
    public const int MaxMemoryKiB = 1024 * 1024;
    public const int MaxIterations = 64;
    public const int MaxParallelism = 16;

    /// <summary>Valeurs par défaut : 64 Mio, 3 passes, 4 voies (au-delà du minimum OWASP pour Argon2id).</summary>
    public static KdfParameters Default { get; } = new(64 * 1024, 3, 4);

    public string ToJson() => JsonSerializer.Serialize(this);

    public static KdfParameters FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<KdfParameters>(json)
                ?? throw new FormatException("Paramètres KDF illisibles.");
        p.EnsureValid();
        return p;
    }

    /// <summary>Borne les paramètres lus sur disque : évite qu'un fichier altéré ne provoque un déni de service.</summary>
    public void EnsureValid()
    {
        if (MemoryKiB is < MinMemoryKiB or > MaxMemoryKiB
            || Iterations is < 1 or > MaxIterations
            || Parallelism is < 1 or > MaxParallelism
            || OutputLength != 32)
        {
            throw new FormatException("Paramètres KDF hors limites.");
        }
    }
}
