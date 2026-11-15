using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfraVault.Domain.ValueObjects;

[JsonConverter(typeof(JsonStringEnumConverter<FieldKind>))]
public enum FieldKind { Text, Multiline, Number, Choice, Date, Url }

/// <summary>
/// Définition d'un champ spécifique à un type de ressource. C'est ce schéma (et non du code)
/// qui décrit un serveur, une VM, une base… : ajouter une catégorie ne demande aucune migration.
/// </summary>
public sealed record FieldDefinition(
    string Key,
    string Label,
    FieldKind Kind = FieldKind.Text,
    bool IsSensitive = false,
    string? Group = null,
    IReadOnlyList<string>? Options = null,
    string? Placeholder = null)
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Serialize(IEnumerable<FieldDefinition> fields) => JsonSerializer.Serialize(fields, Json);

    public static IReadOnlyList<FieldDefinition> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<FieldDefinition>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }
}
