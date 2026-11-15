using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Domain.Entities;
using InfraVault.Domain.Interfaces;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InfraVault.Application.Services;

/// <summary>
/// Métadonnées d'audit : uniquement un libellé (nom d'entité) et un détail court, assainis et tronqués.
/// Il n'existe volontairement aucun moyen d'y placer une valeur libre volumineuse ou un secret.
/// </summary>
public static class AuditMetadata
{
    private sealed record Payload(string? Label, string? Detail);

    public static string? Build(string? label, string? detail)
    {
        label = Sanitize(label);
        detail = Sanitize(detail);
        return label is null && detail is null ? null : JsonSerializer.Serialize(new Payload(label, detail));
    }

    public static (string? Label, string? Detail) Parse(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return (null, null);
        try
        {
            var p = JsonSerializer.Deserialize<Payload>(metadata);
            return (p?.Label, p?.Detail);
        }
        catch (JsonException) { return (null, null); }
    }

    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sb = new StringBuilder(Math.Min(value.Length, Limits.AuditTextMaxLength));
        foreach (var c in value)
        {
            if (sb.Length >= Limits.AuditTextMaxLength) { sb.Append('…'); break; }
            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString().Trim();
    }
}

public static class AuditLabels
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        [AuditEventTypes.VaultCreated] = "Coffre créé",
        [AuditEventTypes.Unlock] = "Déverrouillage",
        [AuditEventTypes.Lock] = "Verrouillage",
        [AuditEventTypes.AuthenticationFailed] = "Échec d'authentification",
        [AuditEventTypes.RecoveryUsed] = "Récupération du coffre",
        [AuditEventTypes.MasterPasswordChanged] = "Mot de passe maître modifié",
        [AuditEventTypes.RecoveryKeyRegenerated] = "Clé de récupération régénérée",
        [AuditEventTypes.AssetCreated] = "Ressource créée",
        [AuditEventTypes.AssetUpdated] = "Ressource modifiée",
        [AuditEventTypes.AssetDeleted] = "Ressource supprimée",
        [AuditEventTypes.SensitivePropertyViewed] = "Propriété sensible consultée",
        [AuditEventTypes.RelationshipCreated] = "Relation créée",
        [AuditEventTypes.RelationshipDeleted] = "Relation supprimée",
        [AuditEventTypes.CredentialCreated] = "Identifiant créé",
        [AuditEventTypes.CredentialUpdated] = "Identifiant modifié",
        [AuditEventTypes.CredentialDeleted] = "Identifiant supprimé",
        [AuditEventTypes.SecretChanged] = "Secret modifié",
        [AuditEventTypes.SecretViewed] = "Secret consulté",
        [AuditEventTypes.SecretCopied] = "Secret copié",
        [AuditEventTypes.SecretVersionRestored] = "Version de secret restaurée",
        [AuditEventTypes.DocumentationCreated] = "Document créé",
        [AuditEventTypes.DocumentationUpdated] = "Document modifié",
        [AuditEventTypes.DocumentationDeleted] = "Document supprimé",
        [AuditEventTypes.AttachmentAdded] = "Pièce jointe ajoutée",
        [AuditEventTypes.AttachmentExported] = "Pièce jointe exportée",
        [AuditEventTypes.AttachmentDeleted] = "Pièce jointe supprimée",
        [AuditEventTypes.BackupCreated] = "Sauvegarde créée",
        [AuditEventTypes.BackupRestored] = "Sauvegarde restaurée",
        [AuditEventTypes.BackupVerified] = "Sauvegarde vérifiée",
        [AuditEventTypes.Export] = "Export",
        [AuditEventTypes.Import] = "Import",
        [AuditEventTypes.SettingsChanged] = "Paramètres modifiés",
        [AuditEventTypes.AuditVerified] = "Journal vérifié",
    };

    public static string Of(string eventType) => Labels.GetValueOrDefault(eventType, eventType);
    public static IReadOnlyDictionary<string, string> All => Labels;
}

/// <summary>
/// Journal d'audit en chaîne de hachage. La tête de chaîne est scellée par un HMAC dont la clé
/// dérive de la DEK : sans le coffre ouvert, on ne peut pas réécrire l'historique sans être détecté.
/// </summary>
public sealed class AuditService(
    IVaultDbFactory dbFactory, IVaultSession session, IClock clock, ILogger<AuditService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler? EventRecorded;

    public async Task RecordAsync(string eventType, string entityType, Guid? entityId = null, bool success = true,
        string? label = null, string? detail = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = dbFactory.Create();
            var vault = await db.Vaults.FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (vault is null) return;

            var last = await db.AuditEvents.AsNoTracking()
                .OrderByDescending(e => e.Sequence)
                .Select(e => new { e.Sequence, e.Hash })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            var now = clock.UtcNow;
            var ev = new AuditEvent
            {
                Id = Guid.NewGuid(),
                VaultId = vault.Id,
                UserId = session.IsUnlocked ? session.UserId : null,
                Sequence = (last?.Sequence ?? 0) + 1,
                EventType = eventType,
                EntityType = entityType,
                EntityId = entityId,
                Timestamp = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
                Result = success ? "Success" : "Failure",
                Metadata = AuditMetadata.Build(label, detail),
                PreviousHash = last?.Hash ?? new byte[32],
            };
            ev.Hash = ev.ComputeHash();
            db.AuditEvents.Add(ev);

            try
            {
                if (session.IsUnlocked)
                {
                    vault.AuditSeal = session.AuditMac(SealInput(ev.Hash, ev.Sequence));
                    vault.AuditSealSequence = ev.Sequence;
                }
            }
            catch (VaultLockedException)
            {
                // Verrouillage concurrent : l'événement reste chaîné, il sera scellé au prochain déverrouillage.
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Audit {EventType} {EntityType} {EntityId} {Result}", eventType, entityType, entityId, ev.Result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Écriture d'audit impossible pour {EventType}", eventType);
        }
        finally
        {
            _gate.Release();
        }
        EventRecorded?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<AuditEventDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var q = db.AuditEvents.AsNoTracking().AsQueryable();
        if (query.EntityId is { } id) q = q.Where(e => e.EntityId == id);
        if (!string.IsNullOrEmpty(query.EventType)) q = q.Where(e => e.EventType == query.EventType);
        if (query.FailuresOnly) q = q.Where(e => e.Result == "Failure");
        if (query.FromUtc is { } from) q = q.Where(e => e.Timestamp >= from);
        if (query.SecurityOnly)
        {
            var security = AuditEventTypes.Security.ToList();
            q = q.Where(e => security.Contains(e.EventType));
        }

        var events = await q.OrderByDescending(e => e.Sequence).Skip(query.Skip).Take(query.Take)
            .ToListAsync(ct).ConfigureAwait(false);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct).ConfigureAwait(false);

        return events.Select(e =>
        {
            var (label, detail) = AuditMetadata.Parse(e.Metadata);
            var user = e.UserId is { } uid && users.TryGetValue(uid, out var name) ? name : "—";
            return new AuditEventDto(e.Id, e.Sequence, e.EventType, AuditLabels.Of(e.EventType), e.EntityType, e.EntityId,
                DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc), e.Result == "Success", label, detail, user,
                AuditEventTypes.Security.Contains(e.EventType));
        }).ToList();
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.AuditEvents.LongCountAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Recalcule toute la chaîne et contrôle le scellé. Détecte modification, insertion et suppression.</summary>
    public async Task<AuditVerification> VerifyAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = dbFactory.Create();
            var vault = await db.Vaults.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (vault is null) return new AuditVerification(true, 0, null, false, "Aucun coffre.");

            long expected = 1, count = 0;
            var previous = new byte[32];
            byte[]? sealedHash = null;

            await foreach (var e in db.AuditEvents.AsNoTracking().OrderBy(e => e.Sequence).AsAsyncEnumerable().WithCancellation(ct))
            {
                if (e.Sequence != expected)
                    return Broken(count, expected, $"Événement n° {expected} manquant : le journal a été tronqué ou modifié.");
                if (!previous.AsSpan().SequenceEqual(e.PreviousHash) || !e.ComputeHash().AsSpan().SequenceEqual(e.Hash))
                    return Broken(count, e.Sequence, $"L'événement n° {e.Sequence} a été altéré.");
                if (e.Sequence == vault.AuditSealSequence) sealedHash = e.Hash;
                previous = e.Hash;
                expected++;
                count++;
            }

            var sealChecked = false;
            if (vault.AuditSeal is { Length: > 0 })
            {
                if (sealedHash is null)
                    return Broken(count, vault.AuditSealSequence, "Les événements scellés ont été supprimés.");
                if (session.IsUnlocked)
                {
                    var mac = session.AuditMac(SealInput(sealedHash, vault.AuditSealSequence));
                    if (!CryptographicOperations.FixedTimeEquals(mac, vault.AuditSeal))
                        return Broken(count, vault.AuditSealSequence, "Le scellé du journal ne correspond pas : l'historique a été réécrit.");
                    sealChecked = true;
                }
            }

            return new AuditVerification(true, count, null, sealChecked,
                sealChecked ? $"Chaîne intègre et scellé valide ({count} événements)." : $"Chaîne intègre ({count} événements).");
        }
        finally
        {
            _gate.Release();
        }

        static AuditVerification Broken(long count, long at, string message) => new(false, count, at, false, message);
    }

    private static byte[] SealInput(byte[] hash, long sequence)
    {
        var input = new byte[hash.Length + sizeof(long)];
        hash.CopyTo(input, 0);
        BinaryPrimitives.WriteInt64BigEndian(input.AsSpan(hash.Length), sequence);
        return input;
    }
}
