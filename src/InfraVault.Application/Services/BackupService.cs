using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Security;
using InfraVault.Domain.Entities;
using InfraVault.Domain.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InfraVault.Application.Services;

/// <summary>
/// Sauvegardes chiffrées (.ivbak), restauration, vérification d'intégrité et exports.
///
/// Format : « IVBAK1 » | longueur d'en-tête (int32 LE) | en-tête JSON | charge utile scellée | SHA-256.
/// La charge utile (instantané SQLite compressé) est chiffrée en AES-256-GCM par une clé de sauvegarde
/// aléatoire, elle-même enveloppée par la DEK. L'en-tête embarque les enveloppes de la DEK : une
/// sauvegarde se restaure donc sur n'importe quel poste avec le Master Password (ou la clé de
/// récupération) en vigueur au moment de la sauvegarde. L'en-tête est authentifié (AAD).
/// </summary>
public sealed class BackupService(
    IVaultDbFactory dbFactory, IVaultSession session, IEncryptionService encryption, IKeyDerivationService kdf,
    IDatabaseFiles databaseFiles, IFileStorage files, IAppPaths paths, VaultService vaultService,
    CredentialService credentialService, ISearchIndex searchIndex, AuditService audit, IClock clock,
    ILogger<BackupService> logger)
{
    public const string Extension = ".ivbak";
    public const string SecretsExtension = ".ivsec";
    private static readonly byte[] Magic = "IVBAK1"u8.ToArray();
    private const int HashSize = 32;
    private const int MaxHeaderSize = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public event EventHandler? BackupCompleted;

    private sealed record BackupHeader
    {
        public int Format { get; init; } = 1;
        public Guid VaultId { get; init; }
        public string VaultName { get; init; } = "";
        public DateTime CreatedAtUtc { get; init; }
        public string AppVersion { get; init; } = AppInfo.Version;
        public bool Automatic { get; init; }
        public string Compression { get; init; } = "gzip";
        public string KdfAlgorithm { get; init; } = "";
        public string KdfParameters { get; init; } = "";
        public byte[] Salt { get; init; } = [];
        public byte[] EncryptedDek { get; init; } = [];
        public byte[] DekNonce { get; init; } = [];
        public byte[] DekTag { get; init; } = [];
        public byte[]? RecoverySalt { get; init; }
        public byte[]? RecoveryEncryptedDek { get; init; }
        public byte[]? RecoveryNonce { get; init; }
        public byte[]? RecoveryTag { get; init; }
        public byte[] WrappedKey { get; init; } = [];
        public int Assets { get; init; }
        public int Credentials { get; init; }
        public int Documents { get; init; }

        public Vault ToVaultEnvelope() => new()
        {
            Id = VaultId, Name = VaultName, Salt = Salt, KdfAlgorithm = KdfAlgorithm, KdfParameters = KdfParameters,
            EncryptedDek = EncryptedDek, DekNonce = DekNonce, DekTag = DekTag, RecoverySalt = RecoverySalt,
            RecoveryEncryptedDek = RecoveryEncryptedDek, RecoveryNonce = RecoveryNonce, RecoveryTag = RecoveryTag,
        };
    }

    public async Task<string> GetBackupDirectoryAsync(CancellationToken ct = default)
    {
        var settings = await vaultService.GetSettingsAsync(ct).ConfigureAwait(false);
        return settings.BackupDirectory ?? paths.DefaultBackupDirectory;
    }

    // ───────────────────────── Création ─────────────────────────

    public async Task<BackupInfo> CreateBackupAsync(string? directory = null, bool automatic = false, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        var settings = await vaultService.GetSettingsAsync(ct).ConfigureAwait(false);
        directory ??= settings.BackupDirectory ?? paths.DefaultBackupDirectory;
        files.EnsureDirectory(directory);
        var now = clock.UtcNow;

        BackupHeader header;
        await using (var db = dbFactory.Create())
        {
            var vault = await db.Vaults.AsNoTracking().FirstAsync(ct).ConfigureAwait(false);
            header = new BackupHeader
            {
                VaultId = vault.Id, VaultName = vault.Name, CreatedAtUtc = now, Automatic = automatic,
                KdfAlgorithm = vault.KdfAlgorithm, KdfParameters = vault.KdfParameters, Salt = vault.Salt,
                EncryptedDek = vault.EncryptedDek, DekNonce = vault.DekNonce, DekTag = vault.DekTag,
                RecoverySalt = vault.RecoverySalt, RecoveryEncryptedDek = vault.RecoveryEncryptedDek,
                RecoveryNonce = vault.RecoveryNonce, RecoveryTag = vault.RecoveryTag,
                Assets = await db.Assets.CountAsync(ct).ConfigureAwait(false),
                Credentials = await db.Credentials.CountAsync(ct).ConfigureAwait(false),
                Documents = await db.Documentations.CountAsync(ct).ConfigureAwait(false),
            };
        }

        var temp = files.CreateTempFile();
        byte[] content;
        try
        {
            await databaseFiles.SnapshotAsync(temp, ct).ConfigureAwait(false);
            var snapshot = await files.ReadAsync(temp, ct).ConfigureAwait(false);
            var backupKey = RandomNumberGenerator.GetBytes(CryptoConstants.KeySizeBytes);
            try
            {
                header = header with { WrappedKey = session.Seal(backupKey, Aad.BackupKey) };
                content = await Task.Run(() => Pack(header, Compress(snapshot), backupKey), ct).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(backupKey);
            }
        }
        finally
        {
            files.Delete(temp);
        }

        var name = $"InfraVault_{VaultService.Slug(header.VaultName)}_{now:yyyyMMdd-HHmmss}{(automatic ? "_auto" : "")}{Extension}";
        var path = Path.Combine(directory, name);
        await files.WriteAtomicAsync(path, content, ct).ConfigureAwait(false);

        await using (var db = dbFactory.Create())
        {
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.LastBackup, ct).ConfigureAwait(false);
            if (row is null) db.Settings.Add(new Setting { Key = SettingKeys.LastBackup, Value = now.ToString("O") });
            else row.Value = now.ToString("O");
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (automatic) Prune(directory, header.VaultId, settings.BackupRetention);
        await audit.RecordAsync(AuditEventTypes.BackupCreated, EntityTypes.Backup, label: name,
            detail: automatic ? "Automatique" : "Manuelle", ct: ct).ConfigureAwait(false);
        logger.LogInformation("Sauvegarde créée ({Size} octets, automatique={Automatic})", content.Length, automatic);
        BackupCompleted?.Invoke(this, EventArgs.Empty);
        return ToInfo(path, content.LongLength, header);
    }

    /// <summary>Lance une sauvegarde automatique si l'intervalle configuré est écoulé.</summary>
    public async Task<bool> RunAutomaticIfDueAsync(CancellationToken ct = default)
    {
        if (!session.IsUnlocked) return false;
        var settings = await vaultService.GetSettingsAsync(ct).ConfigureAwait(false);
        if (!settings.AutoBackupEnabled) return false;
        var last = await vaultService.GetLastBackupAsync(ct).ConfigureAwait(false);
        if (last is not null && clock.UtcNow - last < TimeSpan.FromHours(settings.AutoBackupIntervalHours)) return false;
        await CreateBackupAsync(automatic: true, ct: ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Versionnement : ne conserve que les N sauvegardes automatiques les plus récentes du coffre.</summary>
    private void Prune(string directory, Guid vaultId, int retention)
    {
        try
        {
            var old = files.List(directory, "*_auto" + Extension)
                .OrderByDescending(f => f.LastWriteUtc).Skip(retention).ToList();
            foreach (var file in old) files.Delete(file.Path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Purge des anciennes sauvegardes impossible pour le coffre {VaultId}", vaultId);
        }
    }

    // ───────────────────────── Inventaire & vérification ─────────────────────────

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(string? directory = null, CancellationToken ct = default)
    {
        directory ??= await GetBackupDirectoryAsync(ct).ConfigureAwait(false);
        var result = new List<BackupInfo>();
        foreach (var file in files.List(directory, "*" + Extension))
        {
            try
            {
                var header = await ReadHeaderAsync(file.Path, ct).ConfigureAwait(false);
                result.Add(ToInfo(file.Path, file.Length, header));
            }
            catch (Exception ex) when (ex is BackupException or IOException or JsonException)
            {
                logger.LogWarning("Fichier de sauvegarde illisible ignoré : {File}", Path.GetFileName(file.Path));
            }
        }
        return result.OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    public async Task<BackupInfo> InspectAsync(string path, CancellationToken ct = default)
    {
        var header = await ReadHeaderAsync(path, ct).ConfigureAwait(false);
        var length = files.List(Path.GetDirectoryName(path)!, Path.GetFileName(path)).FirstOrDefault()?.Length ?? 0;
        return ToInfo(path, length, header);
    }

    /// <summary>
    /// Vérifie l'empreinte du fichier ; si le coffre ouvert est celui de la sauvegarde,
    /// déchiffre en plus la charge utile pour une vérification cryptographique complète.
    /// </summary>
    public async Task<BackupVerification> VerifyAsync(string path, CancellationToken ct = default)
    {
        BackupVerification result;
        try
        {
            var content = await files.ReadAsync(path, ct).ConfigureAwait(false);
            var (header, headerBytes, payload) = Unpack(content);
            if (session.IsUnlocked && header.VaultId == session.VaultId)
            {
                try
                {
                    var key = session.Open(header.WrappedKey, Aad.BackupKey);
                    try { await Task.Run(() => Decompress(encryption.Open(key, payload, headerBytes)), ct).ConfigureAwait(false); }
                    finally { CryptographicOperations.ZeroMemory(key); }
                    result = new BackupVerification(true, true, "Sauvegarde intègre : empreinte valide et déchiffrement complet réussi.");
                }
                catch (IntegrityException)
                {
                    result = new BackupVerification(false, false,
                        "Empreinte valide, mais le contenu ne se déchiffre pas avec le coffre ouvert (sauvegarde altérée ou issue d'une autre clé).");
                }
            }
            else
            {
                result = new BackupVerification(true, false,
                    "Empreinte valide. La vérification complète du contenu nécessite le mot de passe du coffre sauvegardé.");
            }
        }
        catch (BackupException ex)
        {
            result = new BackupVerification(false, false, ex.Message);
        }

        if (session.IsUnlocked)
            await audit.RecordAsync(AuditEventTypes.BackupVerified, EntityTypes.Backup, success: result.IsValid,
                label: Path.GetFileName(path), ct: ct).ConfigureAwait(false);
        return result;
    }

    // ───────────────────────── Restauration ─────────────────────────

    /// <summary>
    /// Restaure une sauvegarde à la place du coffre courant (conservé à côté). Le coffre est verrouillé
    /// à l'issue : il se rouvre avec le Master Password en vigueur dans la sauvegarde.
    /// </summary>
    public async Task<string?> RestoreAsync(string path, string secret, bool secretIsRecoveryKey = false, CancellationToken ct = default)
    {
        var content = await files.ReadAsync(path, ct).ConfigureAwait(false);
        var (header, headerBytes, payload) = Unpack(content);
        var envelope = header.ToVaultEnvelope();

        byte[] snapshot;
        byte[]? dek = null, key = null;
        try
        {
            try
            {
                dek = secretIsRecoveryKey
                    ? AuthenticationService.UnwrapWithRecoveryKey(envelope, secret, encryption)
                    : await Task.Run(() => AuthenticationService.UnwrapWithPassword(envelope, secret, kdf, encryption), ct).ConfigureAwait(false);
            }
            catch (IntegrityException)
            {
                if (secretIsRecoveryKey) throw new InvalidRecoveryKeyException();
                throw new InvalidMasterPasswordException();
            }

            try
            {
                key = encryption.Open(dek, header.WrappedKey, Encoding.UTF8.GetBytes(Aad.BackupKey));
                var k = key;
                snapshot = await Task.Run(() => Decompress(encryption.Open(k, payload, headerBytes)), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IntegrityException or InvalidDataException)
            {
                throw new BackupException("La sauvegarde est altérée : son contenu ne peut pas être déchiffré.", ex);
            }
        }
        finally
        {
            if (dek is not null) CryptographicOperations.ZeroMemory(dek);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }

        var temp = files.CreateTempFile();
        try
        {
            await files.WriteAtomicAsync(temp, snapshot, ct).ConfigureAwait(false);
            var vaultId = await databaseFiles.ValidateAsync(temp, ct).ConfigureAwait(false);
            if (vaultId != header.VaultId)
                throw new BackupException("La base contenue dans la sauvegarde est invalide ou ne correspond pas à son en-tête.");

            if (session.IsUnlocked)
                await audit.RecordAsync(AuditEventTypes.Lock, EntityTypes.Vault, session.VaultId, detail: "Restauration").ConfigureAwait(false);
            session.Lock();
            var previous = await databaseFiles.ReplaceDatabaseAsync(temp, ct).ConfigureAwait(false);
            await dbFactory.InitializeAsync(ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditEventTypes.BackupRestored, EntityTypes.Backup, label: Path.GetFileName(path), ct: ct).ConfigureAwait(false);
            logger.LogInformation("Sauvegarde restaurée pour le coffre {VaultId}", header.VaultId);
            return previous;
        }
        finally
        {
            files.Delete(temp);
        }
    }

    // ───────────────────────── Exports / imports ─────────────────────────

    private sealed record ExportedAsset(Guid Id, string Type, string Name, string? Hostname, string? IpAddress,
        string? Description, string Environment, string Status, string? Tags, bool InternetExposed, string? PublicAddress,
        string? ExposedPorts, string? Icon, string? IconColor, Dictionary<string, string?> Properties);

    private sealed record ExportedRelationship(Guid Source, Guid Target, string Type, string? Ports, string? Description);

    private sealed record AssetExport(string Format, int Version, DateTime ExportedAtUtc, string VaultName,
        List<ExportedAsset> Assets, List<ExportedRelationship> Relationships);

    /// <summary>Export JSON des ressources (sélectif si <paramref name="assetIds"/> est fourni). Jamais de secret ni de propriété sensible.</summary>
    public async Task<int> ExportAssetsAsync(string path, IReadOnlyCollection<Guid>? assetIds = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Key, ct).ConfigureAwait(false);
        var query = db.Assets.AsNoTracking().Include(a => a.Properties).AsQueryable();
        if (assetIds is { Count: > 0 }) query = query.Where(a => assetIds.Contains(a.Id));
        var assets = await query.OrderBy(a => a.Name).ToListAsync(ct).ConfigureAwait(false);
        var ids = assets.Select(a => a.Id).ToHashSet();
        var relations = (await db.AssetRelationships.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .Where(r => ids.Contains(r.SourceAssetId) && ids.Contains(r.TargetAssetId)).ToList();

        var export = new AssetExport("infravault-assets", 1, clock.UtcNow, session.VaultName,
            assets.Select(a => new ExportedAsset(a.Id, types[a.AssetTypeId], a.Name, a.Hostname, a.IpAddress, a.Description,
                a.Environment, a.Status, a.Tags, a.InternetExposed, a.PublicAddress, a.ExposedPorts, a.Icon, a.IconColor,
                a.Properties.Where(p => !p.IsSensitive).ToDictionary(p => p.PropertyKey, p => p.PropertyValue))).ToList(),
            relations.Select(r => new ExportedRelationship(r.SourceAssetId, r.TargetAssetId, r.RelationshipType, r.Ports, r.Description)).ToList());

        await files.WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(export, Json), ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.Export, EntityTypes.Asset, label: Path.GetFileName(path),
            detail: $"{assets.Count} ressources (JSON)", ct: ct).ConfigureAwait(false);
        return assets.Count;
    }

    public async Task<ImportResult> ImportAssetsAsync(string path, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        AssetExport? export;
        try { export = JsonSerializer.Deserialize<AssetExport>(await files.ReadAsync(path, ct).ConfigureAwait(false)); }
        catch (JsonException ex) { throw new BackupException("Ce fichier n'est pas un export de ressources InfraVault valide.", ex); }
        if (export is not { Format: "infravault-assets" } || export.Assets is null)
            throw new BackupException("Ce fichier n'est pas un export de ressources InfraVault valide.");

        var warnings = new List<string>();
        int created = 0, skipped = 0;
        var now = clock.UtcNow;
        await using var db = dbFactory.Create();
        var types = await db.AssetTypes.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var custom = types.First(t => t.Key == AssetTypeKeys.Custom);
        var existing = (await db.Assets.Select(a => a.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var validator = new AssetInputValidator();
        var imported = new List<(Asset Asset, AssetType Type)>();

        foreach (var a in export.Assets)
        {
            if (existing.Contains(a.Id)) { skipped++; continue; }
            var type = types.FirstOrDefault(t => t.Key == a.Type);
            if (type is null)
            {
                type = custom;
                warnings.Add($"« {a.Name} » : catégorie « {a.Type} » inconnue, importée comme ressource personnalisée.");
            }
            var input = new AssetInput
            {
                AssetTypeId = type.Id, Name = a.Name ?? "", Hostname = a.Hostname, IpAddress = a.IpAddress,
                Description = a.Description, Environment = string.IsNullOrWhiteSpace(a.Environment) ? "Production" : a.Environment,
                Status = AssetStatuses.All.Contains(a.Status) ? a.Status : AssetStatuses.Unknown,
            };
            var validation = validator.Validate(input);
            if (!validation.IsValid)
            {
                skipped++;
                warnings.Add($"« {a.Name} » ignorée : {validation.Errors[0].ErrorMessage}");
                continue;
            }

            var schema = FieldDefinition.Deserialize(type.FieldSchema);
            var asset = new Asset
            {
                Id = a.Id == Guid.Empty ? Guid.NewGuid() : a.Id, VaultId = session.VaultId, AssetTypeId = type.Id,
                Name = input.Name.Trim(), Hostname = input.Hostname?.Trim(), IpAddress = input.IpAddress?.Trim(),
                Description = a.Description, Environment = input.Environment, Status = input.Status,
                Tags = TagList.Normalize(a.Tags), InternetExposed = a.InternetExposed, PublicAddress = a.PublicAddress,
                ExposedPorts = PortList.Normalize(a.ExposedPorts), Icon = a.Icon, IconColor = a.IconColor,
                CreatedAt = now, UpdatedAt = now,
            };
            foreach (var (key, value) in a.Properties ?? [])
            {
                // Une propriété déclarée sensible dans le schéma local n'est jamais importée en clair.
                if (string.IsNullOrWhiteSpace(value) || schema.Any(f => f.Key == key && f.IsSensitive)) continue;
                asset.Properties.Add(new AssetProperty { Id = Guid.NewGuid(), AssetId = asset.Id, PropertyKey = key, PropertyValue = value });
            }
            db.Assets.Add(asset);
            existing.Add(asset.Id);
            imported.Add((asset, type));
            created++;
        }

        var knownRelations = (await db.AssetRelationships.AsNoTracking()
                .Select(r => new { r.SourceAssetId, r.TargetAssetId, r.RelationshipType }).ToListAsync(ct).ConfigureAwait(false))
            .Select(r => (r.SourceAssetId, r.TargetAssetId, r.RelationshipType)).ToHashSet();
        foreach (var r in export.Relationships ?? [])
        {
            if (!existing.Contains(r.Source) || !existing.Contains(r.Target) || r.Source == r.Target
                || !RelationshipTypes.IsKnown(r.Type) || !knownRelations.Add((r.Source, r.Target, r.Type))) continue;
            db.AssetRelationships.Add(new AssetRelationship
            {
                Id = Guid.NewGuid(), SourceAssetId = r.Source, TargetAssetId = r.Target, RelationshipType = r.Type,
                Ports = PortList.Normalize(r.Ports), Description = r.Description, CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await searchIndex.UpsertManyAsync(imported.Select(i => SearchDocuments.ForAsset(i.Asset, i.Type, i.Asset.Properties)), ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.Import, EntityTypes.Asset, label: Path.GetFileName(path),
            detail: $"{created} ressources importées, {skipped} ignorées", ct: ct).ConfigureAwait(false);
        return new ImportResult(created, skipped, warnings);
    }

    private sealed record ExportedSecret(string Name, string Type, string? Username, string Secret, string? Notes,
        string? Url, int? Port, string? Tags, DateTime? ExpiresAt, List<Guid> AssetIds);

    private sealed record SecretsFile(string Format, int Version, DateTime ExportedAtUtc, string KdfAlgorithm,
        string KdfParameters, byte[] Salt, byte[] Payload);

    /// <summary>
    /// Export des secrets : uniquement chiffré (Argon2id + AES-256-GCM) par une phrase de passe d'export.
    /// Il n'existe aucun export de secrets en clair.
    /// </summary>
    public async Task<int> ExportSecretsAsync(string path, string passphrase, IReadOnlyCollection<Guid>? credentialIds = null, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        MasterPasswordPolicy.Ensure(passphrase);
        await using var db = dbFactory.Create();
        var query = db.Credentials.AsNoTracking().Include(c => c.AssetLinks).AsQueryable();
        if (credentialIds is { Count: > 0 }) query = query.Where(c => credentialIds.Contains(c.Id));
        var credentials = await query.OrderBy(c => c.Name).ToListAsync(ct).ConfigureAwait(false);

        var secrets = credentials.Select(c => new ExportedSecret(c.Name, c.CredentialType,
            c.UsernameEncrypted is null ? null : session.OpenString(c.UsernameEncrypted, Aad.CredentialUsername(c.Id)),
            session.OpenString(c.EncryptedSecret, Aad.CredentialSecret(c.Id)),
            c.NotesEncrypted is null ? null : session.OpenString(c.NotesEncrypted, Aad.CredentialNotes(c.Id)),
            c.Url, c.Port, c.Tags, c.ExpiresAt, c.AssetLinks.Select(l => l.AssetId).ToList())).ToList();

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(secrets);
        var salt = RandomNumberGenerator.GetBytes(CryptoConstants.SaltSizeBytes);
        var parameters = kdf.DefaultParameters;
        var password = Encoding.UTF8.GetBytes(passphrase);
        byte[]? key = null;
        try
        {
            key = await Task.Run(() => kdf.DeriveKey(password, salt, parameters), ct).ConfigureAwait(false);
            var file = new SecretsFile("infravault-secrets", 1, clock.UtcNow, CryptoConstants.KdfAlgorithm, parameters.ToJson(), salt,
                encryption.Seal(key, plaintext, "infravault-secrets:v1"u8));
            await files.WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(file, Json), ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(password);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }

        await audit.RecordAsync(AuditEventTypes.Export, EntityTypes.Credential, label: Path.GetFileName(path),
            detail: $"{credentials.Count} identifiants (chiffré)", ct: ct).ConfigureAwait(false);
        return credentials.Count;
    }

    public async Task<ImportResult> ImportSecretsAsync(string path, string passphrase, CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        SecretsFile? file;
        try { file = JsonSerializer.Deserialize<SecretsFile>(await files.ReadAsync(path, ct).ConfigureAwait(false)); }
        catch (JsonException ex) { throw new BackupException("Ce fichier n'est pas un export de secrets InfraVault valide.", ex); }
        if (file is not { Format: "infravault-secrets" }) throw new BackupException("Ce fichier n'est pas un export de secrets InfraVault valide.");

        List<ExportedSecret> secrets;
        var password = Encoding.UTF8.GetBytes(passphrase ?? "");
        byte[]? key = null;
        try
        {
            var parameters = KdfParameters.FromJson(file.KdfParameters);
            key = await Task.Run(() => kdf.DeriveKey(password, file.Salt, parameters), ct).ConfigureAwait(false);
            var plaintext = encryption.Open(key, file.Payload, "infravault-secrets:v1"u8);
            try { secrets = JsonSerializer.Deserialize<List<ExportedSecret>>(plaintext) ?? []; }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (IntegrityException)
        {
            throw new InfraVaultException("Phrase de passe incorrecte ou fichier altéré.");
        }
        catch (FormatException ex)
        {
            throw new BackupException("Les paramètres cryptographiques de l'export sont invalides.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }

        HashSet<(string, string)> existing;
        await using (var db = dbFactory.Create())
        {
            existing = (await db.Credentials.AsNoTracking().Select(c => new { c.Name, c.CredentialType }).ToListAsync(ct).ConfigureAwait(false))
                .Select(c => (c.Name, c.CredentialType)).ToHashSet();
        }

        int created = 0, skipped = 0;
        var warnings = new List<string>();
        foreach (var s in secrets)
        {
            if (existing.Contains((s.Name, s.Type))) { skipped++; continue; }
            try
            {
                await credentialService.SaveAsync(new CredentialInput
                {
                    Name = s.Name, Type = CredentialTypes.All.Any(t => t.Key == s.Type) ? s.Type : CredentialTypes.Custom,
                    Username = s.Username, Secret = s.Secret, Notes = s.Notes, Url = s.Url, Port = s.Port, Tags = s.Tags,
                    ExpiresAt = s.ExpiresAt, AssetIds = s.AssetIds ?? [],
                }, ct).ConfigureAwait(false);
                created++;
            }
            catch (ValidationFailedException ex)
            {
                skipped++;
                warnings.Add($"« {s.Name} » ignoré : {ex.Errors[0]}");
            }
        }

        await audit.RecordAsync(AuditEventTypes.Import, EntityTypes.Credential, label: Path.GetFileName(path),
            detail: $"{created} identifiants importés, {skipped} ignorés", ct: ct).ConfigureAwait(false);
        return new ImportResult(created, skipped, warnings);
    }

    // ───────────────────────── Format binaire ─────────────────────────

    private byte[] Pack(BackupHeader header, byte[] compressed, byte[] backupKey)
    {
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);
        var payload = encryption.Seal(backupKey, compressed, headerBytes);
        var content = new byte[Magic.Length + 4 + headerBytes.Length + payload.Length + HashSize];
        var span = content.AsSpan();
        Magic.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[Magic.Length..], headerBytes.Length);
        headerBytes.CopyTo(span[(Magic.Length + 4)..]);
        payload.CopyTo(span[(Magic.Length + 4 + headerBytes.Length)..]);
        SHA256.HashData(span[..^HashSize], span[^HashSize..]);
        return content;
    }

    private static (BackupHeader Header, byte[] HeaderBytes, byte[] Payload) Unpack(byte[] content)
    {
        var (header, headerBytes) = ParseHeader(content);
        var bodyStart = Magic.Length + 4 + headerBytes.Length;
        if (content.Length < bodyStart + HashSize + 29)
            throw new BackupException("Le fichier de sauvegarde est tronqué.");
        var expected = SHA256.HashData(content.AsSpan(0, content.Length - HashSize));
        if (!CryptographicOperations.FixedTimeEquals(expected, content.AsSpan(content.Length - HashSize)))
            throw new BackupException("L'empreinte de la sauvegarde ne correspond pas : le fichier est corrompu ou a été modifié.");
        return (header, headerBytes, content[bodyStart..^HashSize]);
    }

    private static (BackupHeader Header, byte[] HeaderBytes) ParseHeader(ReadOnlySpan<byte> content)
    {
        if (content.Length < Magic.Length + 4 || !content[..Magic.Length].SequenceEqual(Magic))
            throw new BackupException("Ce fichier n'est pas une sauvegarde InfraVault.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(content[Magic.Length..]);
        if (length is <= 0 or > MaxHeaderSize || content.Length < Magic.Length + 4 + length)
            throw new BackupException("L'en-tête de la sauvegarde est invalide.");
        var headerBytes = content.Slice(Magic.Length + 4, length).ToArray();
        try
        {
            var header = JsonSerializer.Deserialize<BackupHeader>(headerBytes);
            if (header is not { Format: 1 }) throw new BackupException("Version de sauvegarde non prise en charge.");
            return (header, headerBytes);
        }
        catch (JsonException ex)
        {
            throw new BackupException("L'en-tête de la sauvegarde est illisible.", ex);
        }
    }

    private async Task<BackupHeader> ReadHeaderAsync(string path, CancellationToken ct)
    {
        var prefix = await files.ReadPrefixAsync(path, Magic.Length + 4, ct).ConfigureAwait(false);
        if (prefix.Length < Magic.Length + 4 || !prefix.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new BackupException("Ce fichier n'est pas une sauvegarde InfraVault.");
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(Magic.Length));
        if (length is <= 0 or > MaxHeaderSize) throw new BackupException("L'en-tête de la sauvegarde est invalide.");
        var head = await files.ReadPrefixAsync(path, Magic.Length + 4 + length, ct).ConfigureAwait(false);
        return ParseHeader(head).Header;
    }

    private static BackupInfo ToInfo(string path, long size, BackupHeader h) => new(
        path, Path.GetFileName(path), DateTime.SpecifyKind(h.CreatedAtUtc, DateTimeKind.Utc), size, h.Automatic, h.VaultId,
        h.VaultName, h.Assets, h.Credentials, h.Documents, h.AppVersion);

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
