using System.Text.RegularExpressions;

namespace InfraVault.Domain.ValueObjects;

/// <summary>
/// Liste de ports réseau saisie librement (« 443, 80/tcp; 53/udp, 8000-8100 ») et normalisée
/// en « 443/tcp, 80/tcp, 53/udp, 8000-8100/tcp ». Les éléments non reconnus sont conservés tels quels.
/// </summary>
public static partial class PortList
{
    [GeneratedRegex(@"^(\d{1,5})(?:-(\d{1,5}))?(?:/(tcp|udp|sctp))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PortPattern();

    public static IReadOnlyList<string> Parse(string? ports)
    {
        if (string.IsNullOrWhiteSpace(ports)) return [];
        var result = new List<string>();
        foreach (var raw in ports.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = PortPattern().Match(raw);
            string item;
            if (m.Success && InRange(m.Groups[1].Value) && (!m.Groups[2].Success || InRange(m.Groups[2].Value)))
            {
                var protocol = m.Groups[3].Success ? m.Groups[3].Value.ToLowerInvariant() : "tcp";
                var first = int.Parse(m.Groups[1].Value);
                if (m.Groups[2].Success)
                {
                    // Une plage saisie à l'envers (« 8100-8000 ») est remise dans l'ordre.
                    var second = int.Parse(m.Groups[2].Value);
                    item = first == second ? $"{first}/{protocol}" : $"{Math.Min(first, second)}-{Math.Max(first, second)}/{protocol}";
                }
                else
                {
                    item = $"{first}/{protocol}";
                }
            }
            else
            {
                item = raw.Length > 40 ? raw[..40] : raw;
            }
            if (!result.Contains(item)) result.Add(item);
        }
        return result;
    }

    public static string? Normalize(string? ports)
    {
        var list = Parse(ports);
        return list.Count == 0 ? null : string.Join(", ", list);
    }

    private static bool InRange(string value) => int.TryParse(value, out var port) && port is >= 1 and <= 65535;
}
