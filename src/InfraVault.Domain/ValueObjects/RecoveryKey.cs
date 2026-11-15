using System.Text;
using InfraVault.Shared.Constants;

namespace InfraVault.Domain.ValueObjects;

/// <summary>
/// Clé de récupération : 160 bits aléatoires présentés en Base32 (RFC 4648) par groupes de 4,
/// par exemple <c>K7QF-2M4X-…</c>. Facile à recopier, impossible à deviner.
/// </summary>
public static class RecoveryKey
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != CryptoConstants.RecoveryKeyBytes)
            throw new ArgumentException("Longueur de clé de récupération inattendue.", nameof(bytes));

        var sb = new StringBuilder(40);
        int buffer = 0, bits = 0, count = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                if (count > 0 && count % 4 == 0) sb.Append('-');
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
                count++;
            }
        }
        return sb.ToString();
    }

    /// <summary>Décode une clé saisie par l'utilisateur (tirets, espaces et casse ignorés).</summary>
    public static bool TryParse(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text)) return false;

        var result = new byte[CryptoConstants.RecoveryKeyBytes];
        int buffer = 0, bits = 0, index = 0, symbols = 0;
        foreach (var raw in text)
        {
            if (raw is '-' or ' ' or '\t' or '\r' or '\n') continue;
            var c = char.ToUpperInvariant(raw);
            // Tolère les confusions de lecture les plus courantes.
            c = c switch { '0' => 'O', '1' => 'I', '8' => 'B', _ => c };
            var value = Alphabet.IndexOf(c);
            if (value < 0) return false;
            symbols++;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                if (index >= result.Length) return false;
                result[index++] = (byte)((buffer >> (bits - 8)) & 0xFF);
                bits -= 8;
            }
        }

        if (index != result.Length || symbols != CryptoConstants.RecoveryKeyBytes * 8 / 5) return false;
        bytes = result;
        return true;
    }
}
