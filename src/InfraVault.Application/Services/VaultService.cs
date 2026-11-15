using System.Text.Json;
using System.Text.RegularExpressions;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Domain.Entities;
using InfraVault.Domain.Enums;
using InfraVault.Domain.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Application.Services;

/// <summary>Administration du coffre : paramètres, catégories, tags, environnements et tableau de bord.</summary>
public sealed partial class VaultService(
    IVaultDbFactory dbFactory, IVaultSession session, AuditService audit, ISearchIndex searchIndex, IClock clock)
{
    private VaultSettings _cached = new();

    /// <summary>Dernière version connue des paramètres (lecture sans accès disque, ex. minuterie de verrouillage).</summary>
    public VaultSettings Current => _cached;
    public event EventHandler? SettingsChanged;

    // ───────────────────────── Paramètres ─────────────────────────

    public async Task<VaultSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeys.VaultSettings, ct).ConfigureAwait(false);
        var settings = new VaultSettings();
        if (row is not null)
        {
            try { settings = JsonSerializer.Deserialize<VaultSettings>(row.Value) ?? settings; }
            catch (JsonException) { /* paramètres illisibles : retour aux valeurs par défaut */ }
        }
        _cached = Normalize(settings);
        return _cached;
    }

    public async Task SaveSettingsAsync(VaultSettings settings, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        settings = Normalize(settings);
        await using var db = dbFactory.Create();
        var json = JsonSerializer.Serialize(settings);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.VaultSettings, ct).ConfigureAwait(false);
        if (row is null) db.Settings.Add(new Setting { Key = SettingKeys.VaultSettings, Value = json });
        else row.Value = json;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _cached = settings;
        await audit.RecordAsync(AuditEventTypes.SettingsChanged, EntityTypes.Settings, ct: ct).ConfigureAwait(false);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public static VaultSettings Normalize(VaultSettings s) => s with
    {
        AutoLockMinutes = Math.Clamp(s.AutoLockMinutes, 0, 24 * 60),
        ClipboardClearSeconds = Math.Clamp(s.ClipboardClearSeconds, 5, 600),
        SecretRevealSeconds = Math.Clamp(s.SecretRevealSeconds, 5, 300),
        AutoBackupIntervalHours = Math.Clamp(s.AutoBackupIntervalHours, 1, 24 * 30),
        BackupRetention = Math.Clamp(s.BackupRetention, 1, 200),
        BackupDirectory = string.IsNullOrWhiteSpace(s.BackupDirectory) ? null : s.BackupDirectory.Trim(),
        Environments = s.Environments.Select(e => e.Trim()).Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("Production").ToList(),
        Generator = s.Generator with { Length = Math.Clamp(s.Generator.Length, 8, 128) },
    };

    public async Task<DateTime?> GetLastBackupAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeys.LastBackup, ct).ConfigureAwait(false);
        return row is not null && DateTime.TryParse(row.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d.ToUniversalTime() : null;
    }

    public async Task RenameVaultAsync(string name, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        name = name.Trim();
        if (name.Length is 0 or > Limits.NameMaxLength) throw new ValidationFailedException(["Le nom du coffre est requis."]);
        await using var db = dbFactory.Create();
        var vault = await db.Vaults.FirstAsync(ct).ConfigureAwait(false);
        vault.Name = name;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.SettingsChanged, EntityTypes.Vault, vault.Id, label: name, detail: "Renommage", ct: ct).ConfigureAwait(false);
    }

    // ───────────────────────── Catégories ─────────────────────────

    public async Task<IReadOnlyList<AssetTypeDto>> GetAssetTypesAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct).ConfigureAwait(false);
        var counts = await db.Assets.AsNoTracking().GroupBy(a => a.AssetTypeId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        return types.Select(t => ToDto(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public static AssetTypeDto ToDto(AssetType t, int count = 0)
        => new(t.Id, t.Key, t.Name, t.Description, t.Icon, t.Color, t.IsSystem, t.SortOrder, FieldDefinition.Deserialize(t.FieldSchema), count);

    /// <summary>Crée ou modifie une catégorie. Les champs d'une catégorie système ne peuvent qu'être complétés.</summary>
    public async Task<Guid> SaveAssetTypeAsync(AssetTypeInput input, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        var name = input.Name.Trim();
        var errors = new List<string>();
        if (name.Length is 0 or > 60) errors.Add("Le nom de la catégorie est requis (60 caractères maximum).");

        var fields = new List<FieldDefinition>();
        foreach (var f in input.Fields)
        {
            var label = f.Label.Trim();
            if (label.Length == 0) continue;
            var key = string.IsNullOrWhiteSpace(f.Key) ? Slug(label) : f.Key;
            if (fields.Any(x => x.Key == key)) { errors.Add($"Le champ « {label} » est en double."); continue; }
            fields.Add(f with { Key = key, Label = label, Group = string.IsNullOrWhiteSpace(f.Group) ? "Général" : f.Group.Trim() });
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await using var db = dbFactory.Create();
        AssetType type;
        if (input.Id is { } id)
        {
            type = await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false) ?? throw new NotFoundException("Catégorie");
            if (type.IsSystem)
            {
                // Une catégorie système conserve ses champs d'origine ; on peut seulement en ajouter.
                var original = SystemAssetTypes.Find(type.Key)?.Fields ?? [];
                fields = original.Concat(fields.Where(f => original.All(o => o.Key != f.Key))).ToList();
            }
        }
        else
        {
            var key = "x_" + Slug(name);
            if (await db.AssetTypes.AnyAsync(t => t.Key == key || t.Name == name, ct).ConfigureAwait(false))
                throw new ValidationFailedException(["Une catégorie porte déjà ce nom."]);
            var maxOrder = await db.AssetTypes.MaxAsync(t => (int?)t.SortOrder, ct).ConfigureAwait(false) ?? 0;
            type = new AssetType { Id = Guid.NewGuid(), Key = key, IsSystem = false, SortOrder = maxOrder + 10 };
            db.AssetTypes.Add(type);
        }

        type.Name = name;
        type.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        type.Icon = input.Icon;
        type.Color = input.Color;
        type.FieldSchema = FieldDefinition.Serialize(fields);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.SettingsChanged, EntityTypes.AssetType, type.Id, label: name, detail: "Catégorie", ct: ct).ConfigureAwait(false);
        return type.Id;
    }

    public async Task DeleteAssetTypeAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var type = await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false) ?? throw new NotFoundException("Catégorie");
        if (type.IsSystem) throw new InfraVaultException("Les catégories système ne peuvent pas être supprimées.");
        if (await db.Assets.AnyAsync(a => a.AssetTypeId == id, ct).ConfigureAwait(false))
            throw new InfraVaultException("Cette catégorie est utilisée par des ressources. Reclassez-les ou supprimez-les d'abord.");
        db.AssetTypes.Remove(type);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.SettingsChanged, EntityTypes.AssetType, id, label: type.Name, detail: "Catégorie supprimée", ct: ct).ConfigureAwait(false);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    public static string Slug(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var ascii = new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                                                     != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        var slug = NonSlug().Replace(ascii.ToLowerInvariant(), "_").Trim('_');
        return slug.Length == 0 ? "champ" : slug;
    }

    // ───────────────────────── Tags ─────────────────────────

    public async Task<IReadOnlyList<TagUsage>> GetTagsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var usage = new Dictionary<string, int[]>();
        void Count(IEnumerable<string?> source, int slot)
        {
            foreach (var tag in source.SelectMany(TagList.Parse))
            {
                if (!usage.TryGetValue(tag, out var counts)) usage[tag] = counts = new int[3];
                counts[slot]++;
            }
        }

        Count(await db.Assets.AsNoTracking().Where(a => a.Tags != null).Select(a => a.Tags).ToListAsync(ct).ConfigureAwait(false), 0);
        Count(await db.Credentials.AsNoTracking().Where(a => a.Tags != null).Select(a => a.Tags).ToListAsync(ct).ConfigureAwait(false), 1);
        Count(await db.Documentations.AsNoTracking().Where(a => a.Tags != null).Select(a => a.Tags).ToListAsync(ct).ConfigureAwait(false), 2);
        return usage.OrderBy(u => u.Key).Select(u => new TagUsage(u.Key, u.Value[0], u.Value[1], u.Value[2])).ToList();
    }

    /// <summary>Renomme un tag partout (ou le supprime si <paramref name="newTag"/> est vide).</summary>
    public async Task<int> RenameTagAsync(string oldTag, string? newTag, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        oldTag = oldTag.Trim().ToLowerInvariant();
        var changed = 0;
        await using var db = dbFactory.Create();
        // Recherche large en SQL, filtrage exact en mémoire.
        foreach (var a in await db.Assets.Where(a => a.Tags != null && a.Tags.Contains(oldTag)).ToListAsync(ct).ConfigureAwait(false))
        {
            var updated = TagList.Rename(a.Tags, oldTag, newTag);
            if (updated != a.Tags) { a.Tags = updated; changed++; }
        }
        foreach (var c in await db.Credentials.Where(a => a.Tags != null && a.Tags.Contains(oldTag)).ToListAsync(ct).ConfigureAwait(false))
        {
            var updated = TagList.Rename(c.Tags, oldTag, newTag);
            if (updated != c.Tags) { c.Tags = updated; changed++; }
        }
        foreach (var d in await db.Documentations.Where(a => a.Tags != null && a.Tags.Contains(oldTag)).ToListAsync(ct).ConfigureAwait(false))
        {
            var updated = TagList.Rename(d.Tags, oldTag, newTag);
            if (updated != d.Tags) { d.Tags = updated; changed++; }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.SettingsChanged, EntityTypes.Settings, label: oldTag,
            detail: string.IsNullOrWhiteSpace(newTag) ? "Tag supprimé" : "Tag renommé", ct: ct).ConfigureAwait(false);
        return changed;
    }

    // ───────────────────────── Tableau de bord ─────────────────────────

    public async Task<DashboardDto> GetDashboardAsync(CredentialService credentials, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        var settings = await GetSettingsAsync(ct).ConfigureAwait(false);
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        var typeById = types.ToDictionary(t => t.Id);
        var counts = await db.Assets.AsNoTracking().GroupBy(a => a.AssetTypeId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        int CountOf(string key) => types.Where(t => t.Key == key).Sum(t => counts.GetValueOrDefault(t.Id));

        var byEnv = await db.Assets.AsNoTracking().GroupBy(a => a.Environment)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false);

        var recent = await db.Assets.AsNoTracking().OrderByDescending(a => a.UpdatedAt).Take(7).ToListAsync(ct).ConfigureAwait(false);
        var expiring = await credentials.GetExpiringAsync(ExpiryRules.WarningDays, ct).ConfigureAwait(false);
        var events = await audit.QueryAsync(new AuditQuery(Take: 9), ct).ConfigureAwait(false);
        var lastBackup = await GetLastBackupAsync(ct).ConfigureAwait(false);
        var overdue = settings.AutoBackupEnabled
                      && (lastBackup is null || clock.UtcNow - lastBackup > TimeSpan.FromHours(settings.AutoBackupIntervalHours * 2));

        return new DashboardDto(
            counts.Values.Sum(),
            CountOf(AssetTypeKeys.Server) + CountOf(AssetTypeKeys.Hypervisor),
            CountOf(AssetTypeKeys.VirtualMachine),
            CountOf(AssetTypeKeys.Application),
            CountOf(AssetTypeKeys.Database),
            await db.Credentials.CountAsync(ct).ConfigureAwait(false),
            await db.Documentations.CountAsync(ct).ConfigureAwait(false),
            await db.AssetRelationships.CountAsync(ct).ConfigureAwait(false),
            types.Where(t => counts.ContainsKey(t.Id))
                .Select(t => new TypeCount(t.Key, t.Name, t.Icon, t.Color, counts[t.Id])).ToList(),
            byEnv.OrderByDescending(e => e.Count).Select(e => (e.Key, e.Count)).ToList(),
            recent.Select(a => InfrastructureService.ToSummary(a, typeById[a.AssetTypeId])).ToList(),
            expiring.Take(6).ToList(),
            events,
            lastBackup,
            overdue);
    }

    /// <summary>Reconstruit entièrement l'index de recherche à partir des métadonnées.</summary>
    public async Task<int> RebuildSearchIndexAsync(CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var documents = await SearchDocuments.BuildAllAsync(db, ct).ConfigureAwait(false);
        await searchIndex.ClearAsync(ct).ConfigureAwait(false);
        await searchIndex.UpsertManyAsync(documents, ct).ConfigureAwait(false);
        return documents.Count;
    }

    /// <summary>Répare l'index s'il a divergé (arrêt brutal, restauration…).</summary>
    public async Task EnsureSearchIndexAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var expected = await db.Assets.CountAsync(ct).ConfigureAwait(false)
                       + await db.Credentials.CountAsync(ct).ConfigureAwait(false)
                       + await db.Documentations.CountAsync(ct).ConfigureAwait(false);
        if (await searchIndex.CountAsync(ct).ConfigureAwait(false) != expected)
            await RebuildSearchIndexAsync(ct).ConfigureAwait(false);
    }
}
