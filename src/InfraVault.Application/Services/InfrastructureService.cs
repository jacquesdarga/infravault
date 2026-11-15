using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Security;
using InfraVault.Domain.Entities;
using InfraVault.Domain.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Application.Services;

/// <summary>Inventaire : ressources, propriétés typées, arborescence et relations bidirectionnelles.</summary>
public sealed class InfrastructureService(
    IVaultDbFactory dbFactory, IVaultSession session, AuditService audit, ISearchIndex searchIndex, IClock clock)
{
    private static readonly AssetInputValidator Validator = new();
    private const int MaxTreeDepth = 24;

    public event EventHandler? Changed;

    public static AssetSummaryDto ToSummary(Asset a, AssetType t) => new(
        a.Id, a.Name, t.Key, t.Name, a.Icon ?? t.Icon, a.IconColor ?? t.Color, a.Hostname, a.IpAddress,
        a.Environment, a.Status, a.Tags, DateTime.SpecifyKind(a.UpdatedAt, DateTimeKind.Utc),
        a.InternetExposed, a.PublicAddress, a.ExposedPorts);

    // ───────────────────────── Lecture ─────────────────────────

    public async Task<IReadOnlyList<AssetSummaryDto>> ListAssetsAsync(AssetFilter? filter = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var q = db.Assets.AsNoTracking().AsQueryable();
        if (filter is not null)
        {
            if (!string.IsNullOrEmpty(filter.TypeKey))
            {
                var typeIds = types.Values.Where(t => t.Key == filter.TypeKey).Select(t => t.Id).ToList();
                q = q.Where(a => typeIds.Contains(a.AssetTypeId));
            }
            if (!string.IsNullOrEmpty(filter.Environment)) q = q.Where(a => a.Environment == filter.Environment);
            if (!string.IsNullOrEmpty(filter.Status)) q = q.Where(a => a.Status == filter.Status);
            if (!string.IsNullOrWhiteSpace(filter.Text))
            {
                var text = filter.Text.Trim().ToLower();
                q = q.Where(a => a.Name.ToLower().Contains(text)
                                 || (a.Hostname != null && a.Hostname.ToLower().Contains(text))
                                 || (a.IpAddress != null && a.IpAddress.Contains(text))
                                 || (a.Tags != null && a.Tags.Contains(text)));
            }
        }
        var assets = await q.OrderBy(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        return assets.Select(a => ToSummary(a, types[a.AssetTypeId])).ToList();
    }

    public async Task<AssetDetailDto?> GetAssetAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var asset = await db.Assets.AsNoTracking().Include(a => a.Properties).FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        if (asset is null) return null;
        var type = await db.AssetTypes.AsNoTracking().FirstAsync(t => t.Id == asset.AssetTypeId, ct).ConfigureAwait(false);
        var fields = FieldDefinition.Deserialize(type.FieldSchema);
        var byKey = asset.Properties.ToDictionary(p => p.PropertyKey);

        var properties = new List<AssetPropertyDto>();
        foreach (var f in fields)
        {
            byKey.Remove(f.Key, out var p);
            properties.Add(p is null
                ? new AssetPropertyDto(f.Key, f.Label, f.Group, f.Kind, f.IsSensitive, null, false)
                : new AssetPropertyDto(f.Key, f.Label, f.Group, f.Kind, p.IsSensitive, p.IsSensitive ? null : p.PropertyValue, true));
        }
        // Propriétés hors schéma (catégorie modifiée depuis) : toujours visibles, jamais perdues.
        foreach (var p in byKey.Values)
            properties.Add(new AssetPropertyDto(p.PropertyKey, p.PropertyKey, "Autres", FieldKind.Text, p.IsSensitive,
                p.IsSensitive ? null : p.PropertyValue, true));

        var path = await BuildPathAsync(db, id, ct).ConfigureAwait(false);
        return new AssetDetailDto(ToSummary(asset, type), type.Id, asset.Description, asset.Icon is not null || asset.IconColor is not null,
            DateTime.SpecifyKind(asset.CreatedAt, DateTimeKind.Utc), properties, path);
    }

    /// <summary>Déchiffre à la demande une propriété sensible (journalisé).</summary>
    public async Task<string?> RevealPropertyAsync(Guid assetId, string key, bool auditAccess = true, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var p = await db.AssetProperties.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AssetId == assetId && x.PropertyKey == key, ct).ConfigureAwait(false);
        if (p is null) return null;
        if (!p.IsSensitive || p.EncryptedValue is null) return p.PropertyValue;
        var value = session.OpenString(p.EncryptedValue, Aad.AssetProperty(assetId, key));
        if (auditAccess)
        {
            var name = await db.Assets.Where(a => a.Id == assetId).Select(a => a.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditEventTypes.SensitivePropertyViewed, EntityTypes.Asset, assetId, label: name, detail: key, ct: ct).ConfigureAwait(false);
        }
        return value;
    }

    // ───────────────────────── Écriture ─────────────────────────

    public async Task<Guid> SaveAssetAsync(AssetInput input, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        Validator.EnsureValid(input);
        await using var db = dbFactory.Create();
        var type = await db.AssetTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == input.AssetTypeId, ct).ConfigureAwait(false)
                   ?? throw new ValidationFailedException(["Catégorie inconnue."]);
        var fields = FieldDefinition.Deserialize(type.FieldSchema);
        var now = clock.UtcNow;

        Asset asset;
        var created = input.Id is null;
        if (created)
        {
            asset = new Asset { Id = Guid.NewGuid(), VaultId = session.VaultId, CreatedAt = now };
            db.Assets.Add(asset);
        }
        else
        {
            asset = await db.Assets.Include(a => a.Properties).FirstOrDefaultAsync(a => a.Id == input.Id, ct).ConfigureAwait(false)
                    ?? throw new NotFoundException("Ressource");
        }

        asset.AssetTypeId = type.Id;
        asset.Name = input.Name.Trim();
        asset.Hostname = Clean(input.Hostname);
        asset.IpAddress = Clean(input.IpAddress);
        asset.Description = Clean(input.Description);
        asset.Environment = input.Environment.Trim();
        asset.Status = input.Status;
        asset.Tags = TagList.Normalize(input.Tags);
        asset.InternetExposed = input.InternetExposed;
        asset.PublicAddress = input.InternetExposed ? Clean(input.PublicAddress) : null;
        asset.ExposedPorts = input.InternetExposed ? PortList.Normalize(input.ExposedPorts) : null;
        asset.Icon = Clean(input.Icon) is { } icon && icon != type.Icon ? icon : null;
        asset.IconColor = Clean(input.IconColor) is { } color && color != type.Color ? color : null;
        asset.UpdatedAt = now;

        foreach (var (key, raw) in input.Properties)
        {
            var field = fields.FirstOrDefault(f => f.Key == key);
            var existing = asset.Properties.FirstOrDefault(p => p.PropertyKey == key);
            var sensitive = field?.IsSensitive ?? existing?.IsSensitive ?? false;
            var value = Clean(raw);
            if (value is null)
            {
                if (existing is not null) { asset.Properties.Remove(existing); db.AssetProperties.Remove(existing); }
                continue;
            }
            if (existing is null)
            {
                existing = new AssetProperty { Id = Guid.NewGuid(), AssetId = asset.Id, PropertyKey = key };
                asset.Properties.Add(existing);
                db.AssetProperties.Add(existing);
            }
            existing.IsSensitive = sensitive;
            if (sensitive)
            {
                existing.PropertyValue = null;
                existing.EncryptedValue = session.SealString(value, Aad.AssetProperty(asset.Id, key));
            }
            else
            {
                existing.PropertyValue = value;
                existing.EncryptedValue = null;
            }
        }

        if (created && input.ParentId is { } parentId)
        {
            var parentType = await (from a in db.Assets join t in db.AssetTypes on a.AssetTypeId equals t.Id
                                    where a.Id == parentId select t.Key).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                             ?? throw new ValidationFailedException(["La ressource parente est introuvable."]);
            var relType = input.ParentRelationType is { } rt && RelationshipTypes.IsHierarchical(rt)
                ? rt : RelationshipTypes.DefaultFor(parentType);
            db.AssetRelationships.Add(new AssetRelationship
            {
                Id = Guid.NewGuid(), SourceAssetId = parentId, TargetAssetId = asset.Id, RelationshipType = relType, CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await searchIndex.UpsertAsync(SearchDocuments.ForAsset(asset, type, asset.Properties), ct).ConfigureAwait(false);
        await audit.RecordAsync(created ? AuditEventTypes.AssetCreated : AuditEventTypes.AssetUpdated, EntityTypes.Asset,
            asset.Id, label: asset.Name, detail: type.Name, ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return asset.Id;
    }

    /// <summary>Ce que la suppression d'une ressource entraînerait : à présenter avant confirmation.</summary>
    public async Task<DeleteImpact> GetDeleteImpactAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var name = await db.Assets.Where(a => a.Id == id).Select(a => a.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                   ?? throw new NotFoundException("Ressource");
        var hierarchical = RelationshipTypes.All.Where(d => d.IsHierarchical).Select(d => d.Key).ToList();
        var children = await db.AssetRelationships.CountAsync(r => r.SourceAssetId == id && hierarchical.Contains(r.RelationshipType), ct).ConfigureAwait(false);
        var total = await db.AssetRelationships.CountAsync(r => r.SourceAssetId == id || r.TargetAssetId == id, ct).ConfigureAwait(false);
        var creds = await db.CredentialAssetLinks.CountAsync(l => l.AssetId == id, ct).ConfigureAwait(false);
        var docs = await db.DocumentationAssetLinks.CountAsync(l => l.AssetId == id, ct).ConfigureAwait(false);
        return new DeleteImpact(name, children, total - children, creds, docs);
    }

    /// <summary>
    /// Suppression contrôlée : refuse de supprimer une ressource encore référencée tant que
    /// l'appelant n'a pas explicitement confirmé. Identifiants et documents liés sont conservés.
    /// </summary>
    public async Task DeleteAssetAsync(Guid id, bool confirmDependencies, CancellationToken ct = default)
    {
        var impact = await GetDeleteImpactAsync(id, ct).ConfigureAwait(false);
        if (impact.HasDependencies && !confirmDependencies)
            throw new InfraVaultException("Cette ressource possède des dépendances. Confirmez la suppression pour les détacher.");

        await using var db = dbFactory.Create();
        var asset = await db.Assets.Include(a => a.Properties).FirstAsync(a => a.Id == id, ct).ConfigureAwait(false);
        db.AssetRelationships.RemoveRange(await db.AssetRelationships.Where(r => r.SourceAssetId == id || r.TargetAssetId == id).ToListAsync(ct).ConfigureAwait(false));
        db.CredentialAssetLinks.RemoveRange(await db.CredentialAssetLinks.Where(l => l.AssetId == id).ToListAsync(ct).ConfigureAwait(false));
        db.DocumentationAssetLinks.RemoveRange(await db.DocumentationAssetLinks.Where(l => l.AssetId == id).ToListAsync(ct).ConfigureAwait(false));
        db.Assets.Remove(asset);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await searchIndex.RemoveAsync(EntityTypes.Asset, id, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.AssetDeleted, EntityTypes.Asset, id, label: impact.AssetName, ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ───────────────────────── Arborescence ─────────────────────────

    /// <summary>
    /// Construit l'arborescence à partir des relations hiérarchiques. Ce n'est pas un arbre strict :
    /// une ressource ayant plusieurs parents apparaît sous chacun d'eux.
    /// </summary>
    public async Task<IReadOnlyList<TreeNodeDto>> GetTreeAsync(CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var assets = await db.Assets.AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.AssetTypeId, a.Icon, a.IconColor, a.Status })
            .ToListAsync(ct).ConfigureAwait(false);
        var hierarchical = RelationshipTypes.All.Where(d => d.IsHierarchical).Select(d => d.Key).ToList();
        var relations = await db.AssetRelationships.AsNoTracking()
            .Where(r => hierarchical.Contains(r.RelationshipType))
            .Select(r => new { r.SourceAssetId, r.TargetAssetId, r.RelationshipType })
            .ToListAsync(ct).ConfigureAwait(false);

        var byId = assets.ToDictionary(a => a.Id);
        var children = relations.Where(r => byId.ContainsKey(r.SourceAssetId) && byId.ContainsKey(r.TargetAssetId))
            .ToLookup(r => r.SourceAssetId, r => (r.TargetAssetId, r.RelationshipType));
        var hasParent = relations.Select(r => r.TargetAssetId).ToHashSet();
        var visited = new HashSet<Guid>();

        TreeNodeDto Build(Guid id, string? relation, HashSet<Guid> path, int depth)
        {
            var a = byId[id];
            var t = types[a.AssetTypeId];
            visited.Add(id);
            var kids = new List<TreeNodeDto>();
            if (depth < MaxTreeDepth && path.Add(id))
            {
                foreach (var (childId, rel) in children[id]
                             .OrderBy(c => types[byId[c.TargetAssetId].AssetTypeId].SortOrder).ThenBy(c => byId[c.TargetAssetId].Name))
                {
                    if (!path.Contains(childId)) kids.Add(Build(childId, rel, path, depth + 1));
                }
                path.Remove(id);
            }
            return new TreeNodeDto(id, a.Name, t.Key, a.Icon ?? t.Icon, a.IconColor ?? t.Color, a.Status, relation, kids);
        }

        var ordered = assets.OrderBy(a => types[a.AssetTypeId].SortOrder).ThenBy(a => a.Name).ToList();
        var roots = ordered.Where(a => !hasParent.Contains(a.Id)).Select(a => Build(a.Id, null, [], 0)).ToList();
        // Filet de sécurité : des ressources prises dans un cycle n'ont pas de racine naturelle.
        foreach (var a in ordered.Where(a => !visited.Contains(a.Id)))
            if (!visited.Contains(a.Id)) roots.Add(Build(a.Id, null, [], 0));
        return roots;
    }

    private static async Task<IReadOnlyList<AssetSummaryDto>> BuildPathAsync(IVaultDb db, Guid id, CancellationToken ct)
    {
        var hierarchical = RelationshipTypes.All.Where(d => d.IsHierarchical).Select(d => d.Key).ToList();
        var path = new List<AssetSummaryDto>();
        var seen = new HashSet<Guid> { id };
        var current = id;
        for (var i = 0; i < MaxTreeDepth; i++)
        {
            var parentId = await db.AssetRelationships.AsNoTracking()
                .Where(r => r.TargetAssetId == current && hierarchical.Contains(r.RelationshipType))
                .OrderBy(r => r.CreatedAt).Select(r => (Guid?)r.SourceAssetId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (parentId is null || !seen.Add(parentId.Value)) break;
            var parent = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == parentId, ct).ConfigureAwait(false);
            if (parent is null) break;
            var type = await db.AssetTypes.AsNoTracking().FirstAsync(t => t.Id == parent.AssetTypeId, ct).ConfigureAwait(false);
            path.Insert(0, ToSummary(parent, type));
            current = parent.Id;
        }
        return path;
    }

    // ───────────────────────── Relations ─────────────────────────

    public async Task<IReadOnlyList<RelationshipDto>> GetRelationshipsAsync(Guid assetId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var relations = await db.AssetRelationships.AsNoTracking()
            .Where(r => r.SourceAssetId == assetId || r.TargetAssetId == assetId).ToListAsync(ct).ConfigureAwait(false);
        var otherIds = relations.Select(r => r.SourceAssetId == assetId ? r.TargetAssetId : r.SourceAssetId).Distinct().ToList();
        var others = await db.Assets.AsNoTracking().Where(a => otherIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);

        return relations.Select(r =>
            {
                var outgoing = r.SourceAssetId == assetId;
                var other = others[outgoing ? r.TargetAssetId : r.SourceAssetId];
                var def = RelationshipTypes.Find(r.RelationshipType);
                var label = def is null ? r.RelationshipType : outgoing ? def.Forward : def.Inverse;
                return new RelationshipDto(r.Id, r.RelationshipType, label, outgoing, def?.IsHierarchical ?? false,
                    ToSummary(other, types[other.AssetTypeId]), r.Description, r.Ports);
            })
            .OrderByDescending(r => r.IsOutgoing).ThenBy(r => r.Label).ThenBy(r => r.Other.Name).ToList();
    }

    public async Task<Guid> AddRelationshipAsync(Guid sourceId, Guid targetId, string type, string? description = null, string? ports = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        if (sourceId == targetId) throw new ValidationFailedException(["Une ressource ne peut pas être reliée à elle-même."]);
        if (!RelationshipTypes.IsKnown(type)) throw new ValidationFailedException(["Type de relation inconnu."]);

        await using var db = dbFactory.Create();
        var names = await db.Assets.Where(a => a.Id == sourceId || a.Id == targetId).ToDictionaryAsync(a => a.Id, a => a.Name, ct).ConfigureAwait(false);
        if (names.Count != 2) throw new ValidationFailedException(["L'une des ressources est introuvable."]);
        if (await db.AssetRelationships.AnyAsync(r => r.SourceAssetId == sourceId && r.TargetAssetId == targetId && r.RelationshipType == type, ct).ConfigureAwait(false))
            throw new ValidationFailedException(["Cette relation existe déjà."]);
        if (RelationshipTypes.IsHierarchical(type) && await IsAncestorAsync(db, targetId, sourceId, ct).ConfigureAwait(false))
            throw new ValidationFailedException(["Cette relation créerait une boucle dans l'arborescence."]);

        var relation = new AssetRelationship
        {
            Id = Guid.NewGuid(), SourceAssetId = sourceId, TargetAssetId = targetId, RelationshipType = type,
            Description = Clean(description), Ports = PortList.Normalize(ports), CreatedAt = clock.UtcNow,
        };
        db.AssetRelationships.Add(relation);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.RelationshipCreated, EntityTypes.Asset, sourceId,
            label: names[sourceId], detail: $"{type} → {names[targetId]}", ct: ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.RelationshipCreated, EntityTypes.Asset, targetId,
            label: names[targetId], detail: $"{names[sourceId]} → {type}", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return relation.Id;
    }

    public async Task RemoveRelationshipAsync(Guid relationshipId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var r = await db.AssetRelationships.FirstOrDefaultAsync(x => x.Id == relationshipId, ct).ConfigureAwait(false);
        if (r is null) return;
        var names = await db.Assets.Where(a => a.Id == r.SourceAssetId || a.Id == r.TargetAssetId).ToDictionaryAsync(a => a.Id, a => a.Name, ct).ConfigureAwait(false);
        db.AssetRelationships.Remove(r);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.RelationshipDeleted, EntityTypes.Asset, r.SourceAssetId,
            label: names.GetValueOrDefault(r.SourceAssetId), detail: $"{r.RelationshipType} → {names.GetValueOrDefault(r.TargetAssetId)}", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Vrai si <paramref name="candidate"/> est un ancêtre hiérarchique de <paramref name="of"/> (ou lui-même).</summary>
    private static async Task<bool> IsAncestorAsync(IVaultDb db, Guid candidate, Guid of, CancellationToken ct)
    {
        var hierarchical = RelationshipTypes.All.Where(d => d.IsHierarchical).Select(d => d.Key).ToList();
        var edges = await db.AssetRelationships.AsNoTracking().Where(r => hierarchical.Contains(r.RelationshipType))
            .Select(r => new { r.SourceAssetId, r.TargetAssetId }).ToListAsync(ct).ConfigureAwait(false);
        var parents = edges.ToLookup(e => e.TargetAssetId, e => e.SourceAssetId);
        var stack = new Stack<Guid>([of]);
        var seen = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == candidate) return true;
            if (!seen.Add(current)) continue;
            foreach (var parent in parents[current]) stack.Push(parent);
        }
        return false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
