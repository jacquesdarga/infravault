using InfraVault.Application.Interfaces;
using InfraVault.Domain.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Application.Services;

public sealed record TopologyNode(
    Guid Id, string Name, string TypeKey, string TypeName, string Icon, string Color, string? Hostname, string? IpAddress,
    string Status, string Environment, bool InternetExposed, string? PublicAddress, IReadOnlyList<string> ExposedPorts,
    IReadOnlyList<string> ListeningPorts, IReadOnlyDictionary<string, string> Properties, IReadOnlyList<TopologyNode> Children)
{
    public IEnumerable<TopologyNode> SelfAndDescendants() => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);
}

/// <summary>Flux orienté source → cible, avec les ports empruntés.</summary>
public sealed record TopologyFlow(Guid SourceId, string SourceName, Guid TargetId, string TargetName, string Type,
    string Label, IReadOnlyList<string> Ports, string? Description);

/// <summary>Point d'entrée depuis Internet.</summary>
public sealed record TopologyExposure(Guid AssetId, string AssetName, string TypeName, string? PublicAddress,
    IReadOnlyList<string> Ports, string Location);

/// <summary>Stack de conteneurs : ses membres et leurs échanges internes.</summary>
public sealed record TopologyStack(TopologyNode Stack, string Location, IReadOnlyList<TopologyNode> Members,
    IReadOnlyList<TopologyFlow> InternalFlows, IReadOnlyList<TopologyFlow> ExternalFlows);

public sealed record TopologyModel(string Title, string VaultName, DateTime GeneratedAtUtc, IReadOnlyList<TopologyNode> Roots,
    IReadOnlyList<TopologyFlow> Flows, IReadOnlyList<TopologyExposure> Exposures, IReadOnlyList<TopologyStack> Stacks)
{
    public IEnumerable<TopologyNode> AllNodes() => Roots.SelectMany(r => r.SelfAndDescendants());
    public int NodeCount => AllNodes().Count();
}

/// <summary>Rapport d'architecture (Word). Implémenté côté infrastructure technique.</summary>
public interface IArchitectureReportWriter
{
    /// <summary>Produit un .docx contenant le schéma (PNG optionnel) et les tableaux de flux, ports et inventaire.</summary>
    byte[] Write(TopologyModel model, byte[]? diagramPng, int diagramWidthPx, int diagramHeightPx);
}

/// <summary>
/// Cartographie : transforme l'inventaire et ses relations en vue globale — emboîtement physique/logique
/// (datacenter → hôte → VM → stack → conteneur), flux avec ports, et exposition Internet.
/// </summary>
public sealed class TopologyService(
    IVaultDbFactory dbFactory, IVaultSession session, AuditService audit, IFileStorage files, IClock clock)
{
    private static readonly string[] PortPropertyKeys = ["port", "internal_ports"];

    /// <param name="scopeRootId">Limite la vue à une ressource et à tout ce qu'elle contient (null = tout le coffre).</param>
    public async Task<TopologyModel> BuildAsync(Guid? scopeRootId = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var assets = await db.Assets.AsNoTracking().Include(a => a.Properties).ToListAsync(ct).ConfigureAwait(false);
        var relations = await db.AssetRelationships.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var byId = assets.ToDictionary(a => a.Id);
        relations = relations.Where(r => byId.ContainsKey(r.SourceAssetId) && byId.ContainsKey(r.TargetAssetId)).ToList();

        // Arbre couvrant : chaque ressource est placée une seule fois, sous son premier parent hiérarchique.
        var parentOf = new Dictionary<Guid, Guid>();
        var extraFlows = new List<Domain.Entities.AssetRelationship>();
        foreach (var r in relations.Where(r => RelationshipTypes.IsHierarchical(r.RelationshipType)))
        {
            if (parentOf.ContainsKey(r.TargetAssetId) || WouldLoop(parentOf, r.SourceAssetId, r.TargetAssetId)) extraFlows.Add(r);
            else parentOf[r.TargetAssetId] = r.SourceAssetId;
        }
        var childrenOf = parentOf.ToLookup(p => p.Value, p => p.Key);

        TopologyNode Build(Guid id)
        {
            var a = byId[id];
            var t = types[a.AssetTypeId];
            var props = a.Properties.Where(p => !p.IsSensitive && !string.IsNullOrWhiteSpace(p.PropertyValue))
                .ToDictionary(p => p.PropertyKey, p => p.PropertyValue!);
            var listening = PortPropertyKeys.SelectMany(k => PortList.Parse(props.GetValueOrDefault(k))).Distinct().ToList();
            var kids = childrenOf[id].Select(c => byId[c])
                .OrderBy(c => types[c.AssetTypeId].SortOrder).ThenBy(c => c.Name).Select(c => Build(c.Id)).ToList();
            return new TopologyNode(a.Id, a.Name, t.Key, t.Name, a.Icon ?? t.Icon, a.IconColor ?? t.Color, a.Hostname, a.IpAddress,
                a.Status, a.Environment, a.InternetExposed, a.PublicAddress, PortList.Parse(a.ExposedPorts), listening, props, kids);
        }

        List<TopologyNode> roots;
        string title;
        if (scopeRootId is { } scope)
        {
            if (!byId.ContainsKey(scope)) throw new NotFoundException("Ressource");
            roots = [Build(scope)];
            title = byId[scope].Name;
        }
        else
        {
            roots = assets.Where(a => !parentOf.ContainsKey(a.Id))
                .OrderBy(a => types[a.AssetTypeId].SortOrder).ThenBy(a => a.Name).Select(a => Build(a.Id)).ToList();
            title = "Vue globale de l'infrastructure";
        }

        var inScope = roots.SelectMany(r => r.SelfAndDescendants()).ToDictionary(n => n.Id);
        string Location(Guid id)
        {
            var parts = new List<string>();
            var current = id;
            while (parentOf.TryGetValue(current, out var parent) && parts.Count < 16)
            {
                parts.Insert(0, byId[parent].Name);
                current = parent;
            }
            return parts.Count == 0 ? "—" : string.Join(" › ", parts);
        }

        var flows = relations.Where(r => !RelationshipTypes.IsHierarchical(r.RelationshipType)).Concat(extraFlows)
            .Where(r => inScope.ContainsKey(r.SourceAssetId) || inScope.ContainsKey(r.TargetAssetId))
            .Select(r => new TopologyFlow(r.SourceAssetId, byId[r.SourceAssetId].Name, r.TargetAssetId, byId[r.TargetAssetId].Name,
                r.RelationshipType, RelationshipTypes.Find(r.RelationshipType)?.Forward ?? r.RelationshipType,
                PortList.Parse(r.Ports), r.Description))
            .OrderBy(f => f.SourceName).ThenBy(f => f.TargetName).ToList();

        var exposures = inScope.Values.Where(n => n.InternetExposed)
            .Select(n => new TopologyExposure(n.Id, n.Name, n.TypeName, n.PublicAddress, n.ExposedPorts, Location(n.Id)))
            .OrderBy(e => e.AssetName).ToList();

        var stacks = inScope.Values.Where(n => n.TypeKey == AssetTypeKeys.Stack).OrderBy(n => n.Name)
            .Select(s =>
            {
                var members = s.SelfAndDescendants().Skip(1).ToList();
                var ids = members.Select(m => m.Id).ToHashSet();
                return new TopologyStack(s, Location(s.Id), members,
                    flows.Where(f => ids.Contains(f.SourceId) && ids.Contains(f.TargetId)).ToList(),
                    flows.Where(f => ids.Contains(f.SourceId) ^ ids.Contains(f.TargetId)).ToList());
            }).ToList();

        return new TopologyModel(title, session.VaultName, clock.UtcNow, roots, flows, exposures, stacks);
    }

    /// <summary>Enregistre un export (PNG ou DOCX) et le journalise.</summary>
    public async Task SaveExportAsync(string path, byte[] content, string kind, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await files.WriteAtomicAsync(path, content, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.Export, EntityTypes.Asset, label: Path.GetFileName(path),
            detail: $"Cartographie ({kind})", ct: ct).ConfigureAwait(false);
    }

    private static bool WouldLoop(Dictionary<Guid, Guid> parentOf, Guid parent, Guid child)
    {
        var current = parent;
        for (var i = 0; i < 64; i++)
        {
            if (current == child) return true;
            if (!parentOf.TryGetValue(current, out current)) return false;
        }
        return true;
    }
}
