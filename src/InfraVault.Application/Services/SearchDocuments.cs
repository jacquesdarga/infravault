using InfraVault.Application.Interfaces;
using InfraVault.Domain.Entities;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Application.Services;

/// <summary>
/// Fabrique les documents de l'index de recherche. Point unique garantissant qu'aucun secret,
/// aucune propriété sensible et aucun contenu déchiffré n'atteint l'index.
/// </summary>
public static class SearchDocuments
{
    public const string CredentialCategory = "Identifiants";
    public const string DocumentationCategory = "Documentation";

    public static SearchDocument ForAsset(Asset asset, AssetType type, IEnumerable<AssetProperty> properties)
    {
        var body = string.Join(" \n ", new[] { asset.Description, asset.PublicAddress, asset.ExposedPorts }
            .Concat(properties.Where(p => !p.IsSensitive).Select(p => p.PropertyValue))
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        var subtitle = string.Join(" · ", new[] { asset.Hostname, asset.IpAddress, asset.Environment }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        return new SearchDocument(EntityTypes.Asset, asset.Id, type.Name, asset.Name, subtitle, body, asset.Tags,
            asset.Icon ?? type.Icon, asset.IconColor ?? type.Color);
    }

    public static SearchDocument ForCredential(Credential credential, IEnumerable<string> linkedAssetNames)
    {
        var subtitle = string.Join(" · ", new[]
        {
            CredentialTypes.LabelOf(credential.CredentialType),
            credential.Url,
            credential.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return new SearchDocument(EntityTypes.Credential, credential.Id, CredentialCategory, credential.Name, subtitle,
            string.Join(" \n ", linkedAssetNames), credential.Tags, CredentialTypes.IconOf(credential.CredentialType), "amber");
    }

    public static SearchDocument ForDocument(Documentation document, IEnumerable<string> linkedAssetNames)
        => new(EntityTypes.Documentation, document.Id, DocumentationCategory, document.Title, document.DocumentType,
            string.Join(" \n ", linkedAssetNames), document.Tags, "file", "teal");

    public static async Task<IReadOnlyList<SearchDocument>> BuildAllAsync(IVaultDb db, CancellationToken ct)
    {
        var result = new List<SearchDocument>();
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var assets = await db.Assets.AsNoTracking().Include(a => a.Properties).ToListAsync(ct).ConfigureAwait(false);
        var names = assets.ToDictionary(a => a.Id, a => a.Name);
        result.AddRange(assets.Select(a => ForAsset(a, types[a.AssetTypeId], a.Properties)));

        var credentials = await db.Credentials.AsNoTracking().Include(c => c.AssetLinks).ToListAsync(ct).ConfigureAwait(false);
        result.AddRange(credentials.Select(c => ForCredential(c, c.AssetLinks.Select(l => names.GetValueOrDefault(l.AssetId, "")))));

        // Projection sans le contenu chiffré : inutile de le charger pour indexer des métadonnées.
        var documents = await db.Documentations.AsNoTracking()
            .Select(d => new { d.Id, d.Title, d.DocumentType, d.Tags })
            .ToListAsync(ct).ConfigureAwait(false);
        var docLinks = (await db.DocumentationAssetLinks.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(l => l.DocumentationId, l => l.AssetId);
        result.AddRange(documents.Select(d => ForDocument(
            new Documentation { Id = d.Id, Title = d.Title, DocumentType = d.DocumentType, Tags = d.Tags },
            docLinks[d.Id].Select(id => names.GetValueOrDefault(id, "")))));
        return result;
    }

    /// <summary>Normalise les tags et renvoie null si vide.</summary>
    public static string? Tags(string? raw) => TagList.Normalize(raw);
}
