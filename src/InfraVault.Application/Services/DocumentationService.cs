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

/// <summary>Documentation technique : notes chiffrées, versions, liens vers les ressources et pièces jointes chiffrées.</summary>
public sealed class DocumentationService(
    IVaultDbFactory dbFactory, IVaultSession session, AuditService audit, ISearchIndex searchIndex, IClock clock)
{
    private static readonly DocumentInputValidator Validator = new();

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<DocumentSummaryDto>> ListAsync(string? text = null, string? documentType = null,
        Guid? assetId = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var q = db.Documentations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(documentType)) q = q.Where(d => d.DocumentType == documentType);
        if (assetId is { } aid) q = q.Where(d => d.AssetLinks.Any(l => l.AssetId == aid));
        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim().ToLower();
            q = q.Where(d => d.Title.ToLower().Contains(t) || (d.Tags != null && d.Tags.Contains(t)));
        }
        var rows = await q.OrderByDescending(d => d.UpdatedAt)
            .Select(d => new { d.Id, d.Title, d.DocumentType, d.Tags, d.UpdatedAt, d.CurrentVersion, Links = d.AssetLinks.Count, Files = d.Attachments.Count })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new DocumentSummaryDto(r.Id, r.Title, r.DocumentType, r.Tags, Utc(r.UpdatedAt), r.CurrentVersion, r.Links, r.Files)).ToList();
    }

    public async Task<DocumentDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var d = await db.Documentations.AsNoTracking().Include(x => x.AssetLinks).FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null) return null;

        var versions = await db.DocumentationVersions.AsNoTracking().Where(v => v.DocumentationId == id)
            .OrderByDescending(v => v.VersionNumber).Select(v => new { v.Id, v.VersionNumber, v.CreatedAt }).ToListAsync(ct).ConfigureAwait(false);
        var attachments = await db.Attachments.AsNoTracking().Where(a => a.DocumentationId == id).OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.FilenameEncrypted, a.MimeType, a.SizeBytes, a.CreatedAt }).ToListAsync(ct).ConfigureAwait(false);
        var assetIds = d.AssetLinks.Select(l => l.AssetId).ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);

        var summary = new DocumentSummaryDto(d.Id, d.Title, d.DocumentType, d.Tags, Utc(d.UpdatedAt), d.CurrentVersion, assets.Count, attachments.Count);
        return new DocumentDetailDto(summary, session.OpenString(d.ContentEncrypted, Aad.DocumentContent(d.Id)), Utc(d.CreatedAt),
            assets.Select(a => InfrastructureService.ToSummary(a, types[a.AssetTypeId])).ToList(),
            versions.Select(v => new DocumentVersionDto(v.Id, v.VersionNumber, Utc(v.CreatedAt), v.VersionNumber == d.CurrentVersion)).ToList(),
            attachments.Select(a => new AttachmentDto(a.Id, session.OpenString(a.FilenameEncrypted, Aad.AttachmentName(a.Id)), a.MimeType, a.SizeBytes, Utc(a.CreatedAt))).ToList());
    }

    public async Task<string> GetVersionContentAsync(Guid versionId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var v = await db.DocumentationVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Version");
        return session.OpenString(v.ContentEncrypted, Aad.DocumentVersion(v.Id));
    }

    public async Task<Guid> SaveAsync(DocumentInput input, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        Validator.EnsureValid(input);
        await using var db = dbFactory.Create();
        var now = clock.UtcNow;
        var created = input.Id is null;
        var content = input.Content ?? "";

        Documentation d;
        if (created)
        {
            d = new Documentation { Id = Guid.NewGuid(), VaultId = session.VaultId, CreatedAt = now };
            db.Documentations.Add(d);
        }
        else
        {
            d = await db.Documentations.Include(x => x.AssetLinks).FirstOrDefaultAsync(x => x.Id == input.Id, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Document");
        }

        d.Title = input.Title.Trim();
        d.DocumentType = input.DocumentType.Trim();
        d.Tags = TagList.Normalize(input.Tags);
        d.UpdatedAt = now;

        if (created || session.OpenString(d.ContentEncrypted, Aad.DocumentContent(d.Id)) != content)
        {
            d.CurrentVersion++;
            d.ContentEncrypted = session.SealString(content, Aad.DocumentContent(d.Id));
            var versionId = Guid.NewGuid();
            db.DocumentationVersions.Add(new DocumentationVersion
            {
                Id = versionId, DocumentationId = d.Id, VersionNumber = d.CurrentVersion, CreatedAt = now,
                ContentEncrypted = session.SealString(content, Aad.DocumentVersion(versionId)),
            });
        }

        var wanted = input.AssetIds.Distinct().ToHashSet();
        if (wanted.Count > 0)
        {
            var existingIds = await db.Assets.Where(a => wanted.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
            wanted.IntersectWith(existingIds);
        }
        foreach (var link in d.AssetLinks.Where(l => !wanted.Contains(l.AssetId)).ToList())
        {
            d.AssetLinks.Remove(link);
            db.DocumentationAssetLinks.Remove(link);
        }
        foreach (var assetId in wanted.Where(a => d.AssetLinks.All(l => l.AssetId != a)))
            d.AssetLinks.Add(new DocumentationAssetLink { DocumentationId = d.Id, AssetId = assetId });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReindexAsync(db, d, ct).ConfigureAwait(false);
        await audit.RecordAsync(created ? AuditEventTypes.DocumentationCreated : AuditEventTypes.DocumentationUpdated,
            EntityTypes.Documentation, d.Id, label: d.Title, detail: $"Version {d.CurrentVersion}", ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return d.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var d = await db.Documentations.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null) return;
        db.Documentations.Remove(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await searchIndex.RemoveAsync(EntityTypes.Documentation, id, ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.DocumentationDeleted, EntityTypes.Documentation, id, label: d.Title, ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task LinkAssetAsync(Guid documentId, Guid assetId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        if (await db.DocumentationAssetLinks.AnyAsync(l => l.DocumentationId == documentId && l.AssetId == assetId, ct).ConfigureAwait(false)) return;
        if (!await db.Assets.AnyAsync(a => a.Id == assetId, ct).ConfigureAwait(false)) throw new NotFoundException("Ressource");
        db.DocumentationAssetLinks.Add(new DocumentationAssetLink { DocumentationId = documentId, AssetId = assetId });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReindexAsync(db, documentId, ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UnlinkAssetAsync(Guid documentId, Guid assetId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var link = await db.DocumentationAssetLinks.FirstOrDefaultAsync(l => l.DocumentationId == documentId && l.AssetId == assetId, ct).ConfigureAwait(false);
        if (link is null) return;
        db.DocumentationAssetLinks.Remove(link);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await ReindexAsync(db, documentId, ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ───────────────────────── Pièces jointes ─────────────────────────

    public async Task<Guid> AddAttachmentAsync(Guid documentId, string fileName, byte[] content, string? mimeType = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        if (content.LongLength > Limits.AttachmentMaxBytes)
            throw new ValidationFailedException([$"La pièce jointe dépasse {Limits.AttachmentMaxBytes / (1024 * 1024)} Mo."]);
        fileName = Path.GetFileName(fileName.Trim());
        if (fileName.Length == 0) throw new ValidationFailedException(["Nom de fichier invalide."]);

        await using var db = dbFactory.Create();
        var title = await db.Documentations.Where(d => d.Id == documentId).Select(d => d.Title).FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw new NotFoundException("Document");
        var id = Guid.NewGuid();
        db.Attachments.Add(new Attachment
        {
            Id = id,
            DocumentationId = documentId,
            FilenameEncrypted = session.SealString(fileName, Aad.AttachmentName(id)),
            ContentEncrypted = session.Seal(content, Aad.AttachmentContent(id)),
            MimeType = string.IsNullOrWhiteSpace(mimeType) ? GuessMime(fileName) : mimeType,
            SizeBytes = content.LongLength,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.AttachmentAdded, EntityTypes.Documentation, documentId, label: title, ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public async Task<(string FileName, byte[] Content)> GetAttachmentAsync(Guid attachmentId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("Pièce jointe");
        var name = session.OpenString(a.FilenameEncrypted, Aad.AttachmentName(a.Id));
        var content = session.Open(a.ContentEncrypted, Aad.AttachmentContent(a.Id));
        await audit.RecordAsync(AuditEventTypes.AttachmentExported, EntityTypes.Documentation, a.DocumentationId, ct: ct).ConfigureAwait(false);
        return (name, content);
    }

    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct).ConfigureAwait(false);
        if (a is null) return;
        db.Attachments.Remove(a);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.AttachmentDeleted, EntityTypes.Documentation, a.DocumentationId, ct: ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReindexAsync(IVaultDb db, Guid documentId, CancellationToken ct)
    {
        var d = await db.Documentations.AsNoTracking().Include(x => x.AssetLinks)
            .Where(x => x.Id == documentId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (d is not null) await ReindexAsync(db, d, ct).ConfigureAwait(false);
    }

    private async Task ReindexAsync(IVaultDb db, Documentation d, CancellationToken ct)
    {
        var ids = d.AssetLinks.Select(l => l.AssetId).ToList();
        var names = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        await searchIndex.UpsertAsync(SearchDocuments.ForDocument(d, names), ct).ConfigureAwait(false);
    }

    private static string GuessMime(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".txt" or ".log" or ".conf" or ".cfg" or ".ini" => "text/plain",
        ".md" => "text/markdown",
        ".json" => "application/json",
        ".yml" or ".yaml" => "application/yaml",
        ".xml" => "application/xml",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".zip" => "application/zip",
        ".csv" => "text/csv",
        _ => "application/octet-stream",
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
