namespace InfraVault.Domain.ValueObjects;

/// <summary>Tags stockés sous forme normalisée « a, b, c » (minuscules, sans doublon).</summary>
public static class TagList
{
    public static IReadOnlyList<string> Parse(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return [];
        return tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.TrimStart('#').ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
    }

    public static string? Normalize(string? tags)
    {
        var list = Parse(tags);
        return list.Count == 0 ? null : string.Join(", ", list);
    }

    public static string? Rename(string? tags, string oldTag, string? newTag)
    {
        var list = Parse(tags).ToList();
        var index = list.IndexOf(oldTag.ToLowerInvariant());
        if (index < 0) return tags;
        if (string.IsNullOrWhiteSpace(newTag)) list.RemoveAt(index);
        else list[index] = newTag.Trim().ToLowerInvariant();
        return list.Count == 0 ? null : string.Join(", ", list.Distinct());
    }
}
