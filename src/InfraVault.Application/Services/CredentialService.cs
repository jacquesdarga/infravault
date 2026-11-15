using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Security;
using InfraVault.Domain.Entities;
using InfraVault.Domain.Enums;
using InfraVault.Domain.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Application.Services;

public enum SecretAccess { View, Copy, Internal }

/// <summary>
/// Gestionnaire de secrets. Les secrets ne sont déchiffrés qu'à la demande, chaque consultation
/// ou copie est journalisée, et chaque modification crée une version (les anciennes restent chiffrées).
/// </summary>
public sealed class CredentialService(
    IVaultDbFactory dbFactory, IVaultSession session, AuditService audit, ISearchIndex searchIndex, IClock clock)
{
    private static readonly CredentialInputValidator Validator = new();

    public event EventHandler? Changed;

    private CredentialSummaryDto ToSummary(Credential c, int linkCount) => new(
        c.Id, c.Name, c.CredentialType, CredentialTypes.LabelOf(c.CredentialType), CredentialTypes.IconOf(c.CredentialType),
        c.Url, c.Port, c.Tags, Utc(c.ExpiresAt), ExpiryRules.Evaluate(Utc(c.ExpiresAt), clock.UtcNow),
        Utc(c.UpdatedAt), c.CurrentVersion, linkCount, c.UsernameEncrypted is not null);

    // ───────────────────────── Lecture (métadonnées uniquement) ─────────────────────────

    public async Task<IReadOnlyList<CredentialSummaryDto>> ListAsync(string? text = null, string? type = null,
        Guid? assetId = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var q = db.Credentials.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(type)) q = q.Where(c => c.CredentialType == type);
        if (assetId is { } aid) q = q.Where(c => c.AssetLinks.Any(l => l.AssetId == aid));
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(t) || (c.Url != null && c.Url.ToLower().Contains(t))
                                                          || (c.Tags != null && c.Tags.Contains(t)));
        }
        var rows = await q.OrderBy(c => c.Name)
            .Select(c => new { Credential = c, Links = c.AssetLinks.Count })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => ToSummary(r.Credential, r.Links)).ToList();
    }

    public async Task<CredentialDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var c = await db.Credentials.AsNoTracking().Include(x => x.AssetLinks).FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (c is null) return null;

        var versions = await db.CredentialVersions.AsNoTracking().Where(v => v.CredentialId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new { v.Id, v.VersionNumber, v.ChangeReason, v.CreatedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        var assetIds = c.AssetLinks.Select(l => l.AssetId).ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);

        return new CredentialDetailDto(ToSummary(c, c.AssetLinks.Count), Utc(c.CreatedAt), c.NotesEncrypted is not null,
            assets.Select(a => InfrastructureService.ToSummary(a, types[a.AssetTypeId])).ToList(),
            versions.Select(v => new CredentialVersionDto(v.Id, v.VersionNumber, v.ChangeReason, Utc(v.CreatedAt), v.VersionNumber == c.CurrentVersion)).ToList());
    }

    public async Task<IReadOnlyList<CredentialSummaryDto>> GetExpiringAsync(int withinDays, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var limit = clock.UtcNow.AddDays(withinDays);
        var rows = await db.Credentials.AsNoTracking().Where(c => c.ExpiresAt != null && c.ExpiresAt <= limit)
            .OrderBy(c => c.ExpiresAt).Select(c => new { Credential = c, Links = c.AssetLinks.Count })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => ToSummary(r.Credential, r.Links)).ToList();
    }

    // ───────────────────────── Déchiffrement à la demande ─────────────────────────

    public async Task<string> RevealSecretAsync(Guid id, SecretAccess access, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var c = await db.Credentials.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new { x.Name, x.EncryptedSecret }).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Identifiant");
        var secret = session.OpenString(c.EncryptedSecret, Aad.CredentialSecret(id));
        if (access != SecretAccess.Internal)
        {
            await audit.RecordAsync(access == SecretAccess.Copy ? AuditEventTypes.SecretCopied : AuditEventTypes.SecretViewed,
                EntityTypes.Credential, id, label: c.Name, ct: ct).ConfigureAwait(false);
        }
        return secret;
    }

    public async Task<string?> RevealUsernameAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var box = await db.Credentials.AsNoTracking().Where(x => x.Id == id).Select(x => x.UsernameEncrypted).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return box is null ? null : session.OpenString(box, Aad.CredentialUsername(id));
    }

    public async Task<string?> RevealNotesAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var box = await db.Credentials.AsNoTracking().Where(x => x.Id == id).Select(x => x.NotesEncrypted).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return box is null ? null : session.OpenString(box, Aad.CredentialNotes(id));
    }

    public async Task<CredentialEditData> GetEditDataAsync(Guid id, CancellationToken ct = default)
        => new(await RevealUsernameAsync(id, ct).ConfigureAwait(false), await RevealNotesAsync(id, ct).ConfigureAwait(false));

    public async Task<string> RevealVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var v = await db.CredentialVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Version");
        var name = await db.Credentials.Where(c => c.Id == v.CredentialId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var secret = session.OpenString(v.EncryptedSecret, Aad.CredentialVersion(v.Id));
        await audit.RecordAsync(AuditEventTypes.SecretViewed, EntityTypes.Credential, v.CredentialId, label: name,
            detail: $"Version {v.VersionNumber}", ct: ct).ConfigureAwait(false);
        return secret;
    }

    // ───────────────────────── Écriture ─────────────────────────

    public async Task<Guid> SaveAsync(CredentialInput input, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        Validator.EnsureValid(input);
        await using var db = dbFactory.Create();
        var now = clock.UtcNow;
        var created = input.Id is null;
        var secretChanged = false;

        Credential c;
        if (created)
        {
            c = new Credential { Id = Guid.NewGuid(), VaultId = session.VaultId, CreatedAt = now };
            db.Credentials.Add(c);
        }
        else
        {
            c = await db.Credentials.Include(x => x.AssetLinks).FirstOrDefaultAsync(x => x.Id == input.Id, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Identifiant");
        }

        c.Name = input.Name.Trim();
        c.CredentialType = input.Type;
        c.Url = Clean(input.Url);
        c.Port = input.Port;
        c.Tags = TagList.Normalize(input.Tags);
        c.ExpiresAt = input.ExpiresAt?.ToUniversalTime();
        c.UpdatedAt = now;

        var username = Clean(input.Username);
        c.UsernameEncrypted = username is null ? null : session.SealString(username, Aad.CredentialUsername(c.Id));
        c.UsernameIndex = username is null ? null : session.BlindIndex(username);
        var notes = Clean(input.Notes);
        c.NotesEncrypted = notes is null ? null : session.SealString(notes, Aad.CredentialNotes(c.Id));

        if (input.Secret is not null)
        {
            var isNew = created || session.OpenString(c.EncryptedSecret, Aad.CredentialSecret(c.Id)) != input.Secret;
            if (isNew)
            {
                secretChanged = !created;
                AddVersion(db, c, input.Secret, created ? "Création initiale" : Clean(input.ChangeReason) ?? "Rotation du secret", now);
            }
        }

        // Synchronisation des ressources associées (un identifiant peut couvrir plusieurs ressources).
        var wanted = input.AssetIds.Distinct().ToHashSet();
        if (wanted.Count > 0)
        {
            var existingIds = await db.Assets.Where(a => wanted.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
            wanted.IntersectWith(existingIds);
        }
        foreach (var link in c.AssetLinks.Where(l => !wanted.Contains(l.AssetId)).ToList())
        {
            c.AssetLinks.Remove(link);
            db.CredentialAssetLinks.Remove(link);
        }
        foreach (var assetId in wanted.Where(a => c.AssetLinks.All(l => l.AssetId != a)))
        {
            var link = new CredentialAssetLink { CredentialId = c.Id, AssetId = assetId };
            c.AssetLinks.Add(link);
            if (!created) db.CredentialAssetLinks.Add(link);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReindexAsync(db, c, ct).ConfigureAwait(false);

        await audit.RecordAsync(created ? AuditEventTypes.CredentialCreated : AuditEventTypes.CredentialUpdated,
            EntityTypes.Credential, c.Id, label: c.Name, detail: CredentialTypes.LabelOf(c.CredentialType), ct: ct).ConfigureAwait(false);
        if (secretChanged)
            await audit.RecordAsync(AuditEventTypes.SecretChanged, EntityTypes.Credential, c.Id, label: c.Name,
                detail: $"Version {c.CurrentVersion}", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return c.Id;
    }

    /// <summary>Restaure une ancienne version : elle devient une nouvelle version, l'historique reste intact.</summary>
    public async Task RestoreVersionAsync(Guid credentialId, Guid versionId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var c = await db.Credentials.FirstOrDefaultAsync(x => x.Id == credentialId, ct).ConfigureAwait(false) ?? throw new NotFoundException("Identifiant");
        var v = await db.CredentialVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId && x.CredentialId == credentialId, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Version");
        if (v.VersionNumber == c.CurrentVersion) throw new InfraVaultException("Cette version est déjà la version courante.");

        var secret = session.OpenString(v.EncryptedSecret, Aad.CredentialVersion(v.Id));
        var now = clock.UtcNow;
        AddVersion(db, c, secret, $"Restauration de la version {v.VersionNumber}", now);
        c.UpdatedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.SecretVersionRestored, EntityTypes.Credential, c.Id, label: c.Name,
            detail: $"Version {v.VersionNumber} → version {c.CurrentVersion}", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var c = await db.Credentials.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (c is null) return;
        db.Credentials.Remove(c); // versions et liens supprimés en cascade
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await searchIndex.RemoveAsync(EntityTypes.Credential, id, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.CredentialDeleted, EntityTypes.Credential, id, label: c.Name, ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task LinkAssetAsync(Guid credentialId, Guid assetId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var c = await db.Credentials.Include(x => x.AssetLinks).FirstOrDefaultAsync(x => x.Id == credentialId, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Identifiant");
        if (!await db.Assets.AnyAsync(a => a.Id == assetId, ct).ConfigureAwait(false)) throw new NotFoundException("Ressource");
        if (c.AssetLinks.Any(l => l.AssetId == assetId)) return;
        db.CredentialAssetLinks.Add(new CredentialAssetLink { CredentialId = credentialId, AssetId = assetId });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        c = await db.Credentials.AsNoTracking().Include(x => x.AssetLinks).FirstAsync(x => x.Id == credentialId, ct).ConfigureAwait(false);
        await ReindexAsync(db, c, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.CredentialUpdated, EntityTypes.Credential, credentialId, label: c.Name, detail: "Ressource associée", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UnlinkAssetAsync(Guid credentialId, Guid assetId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var link = await db.CredentialAssetLinks.FirstOrDefaultAsync(l => l.CredentialId == credentialId && l.AssetId == assetId, ct).ConfigureAwait(false);
        if (link is null) return;
        db.CredentialAssetLinks.Remove(link);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var c = await db.Credentials.AsNoTracking().Include(x => x.AssetLinks).FirstAsync(x => x.Id == credentialId, ct).ConfigureAwait(false);
        await ReindexAsync(db, c, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.CredentialUpdated, EntityTypes.Credential, credentialId, label: c.Name, detail: "Ressource détachée", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddVersion(IVaultDb db, Credential c, string secret, string reason, DateTime now)
    {
        c.CurrentVersion++;
        c.EncryptedSecret = session.SealString(secret, Aad.CredentialSecret(c.Id));
        var versionId = Guid.NewGuid();
        db.CredentialVersions.Add(new CredentialVersion
        {
            Id = versionId,
            CredentialId = c.Id,
            VersionNumber = c.CurrentVersion,
            ChangeReason = AuditMetadata.Sanitize(reason),
            CreatedAt = now,
            EncryptedSecret = session.SealString(secret, Aad.CredentialVersion(versionId)),
        });
    }

    private async Task ReindexAsync(IVaultDb db, Credential c, CancellationToken ct)
    {
        var ids = c.AssetLinks.Select(l => l.AssetId).ToList();
        var names = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        await searchIndex.UpsertAsync(SearchDocuments.ForCredential(c, names), ct).ConfigureAwait(false);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);
}
