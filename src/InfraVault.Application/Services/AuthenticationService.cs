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
/// Cycle de vie du coffre : création, déverrouillage, verrouillage, rotation du Master Password
/// et récupération. Implémente le chiffrement par enveloppe : Master Password → KEK (Argon2id) → DEK.
/// </summary>
public sealed class AuthenticationService(
    IVaultDbFactory dbFactory,
    IVaultSession session,
    IKeyDerivationService kdf,
    IEncryptionService encryption,
    AuditService audit,
    IClock clock,
    ILogger<AuthenticationService> logger)
{
    private sealed record FailureState(int Count, DateTime LastUtc);

    public async Task<bool> VaultExistsAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        return await db.Vaults.AnyAsync(ct).ConfigureAwait(false);
    }

    public async Task<VaultInfo?> GetVaultInfoAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var v = await db.Vaults.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return v is null
            ? null
            : new VaultInfo(v.Id, v.Name, Utc(v.CreatedAt), Utc(v.PasswordChangedAt), v.HasRecoveryKey,
                Utc(v.RecoveryCreatedAt), v.KdfAlgorithm, KdfParameters.FromJson(v.KdfParameters), v.CryptoVersion);
    }

    /// <summary>Crée le coffre, ouvre la session et renvoie la clé de récupération (affichée une seule fois).</summary>
    public async Task<CreateVaultResult> CreateVaultAsync(string vaultName, string masterPassword, CancellationToken ct = default)
    {
        vaultName = (vaultName ?? "").Trim();
        var errors = new List<string>();
        if (vaultName.Length is 0 or > Limits.NameMaxLength) errors.Add("Le nom du coffre est requis (120 caractères maximum).");
        errors.AddRange(MasterPasswordPolicy.Validate(masterPassword));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        await using var db = dbFactory.Create();
        if (await db.Vaults.AnyAsync(ct).ConfigureAwait(false))
            throw new InfraVaultException("Un coffre existe déjà à cet emplacement.");

        var now = clock.UtcNow;
        var vault = new Vault { Id = Guid.NewGuid(), Name = vaultName, CreatedAt = now, CryptoVersion = CryptoConstants.CryptoVersion };
        var dek = RandomNumberGenerator.GetBytes(CryptoConstants.KeySizeBytes);
        string recoveryKey;
        try
        {
            await WrapWithPasswordAsync(vault, dek, masterPassword, ct).ConfigureAwait(false);
            recoveryKey = WrapWithNewRecoveryKey(vault, dek, now);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = Environment.UserName,
                DisplayName = Environment.UserName,
                CreatedAt = now,
            };
            db.Vaults.Add(vault);
            db.Users.Add(user);
            SeedSystemTypes(db, []);
            db.Settings.Add(new Setting { Key = SettingKeys.VaultSettings, Value = JsonSerializer.Serialize(new VaultSettings()) });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            session.Open(vault.Id, vault.Name, user.Id, user.DisplayName, dek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }

        await audit.RecordAsync(AuditEventTypes.VaultCreated, EntityTypes.Vault, vault.Id, label: vault.Name, ct: ct).ConfigureAwait(false);
        logger.LogInformation("Coffre {VaultId} créé", vault.Id);
        return new CreateVaultResult(vault.Id, recoveryKey);
    }

    /// <summary>Déverrouille le coffre. Un mauvais mot de passe laisse le coffre fermé et est journalisé.</summary>
    public async Task UnlockAsync(string masterPassword, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var vault = await db.Vaults.FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw new InfraVaultException("Aucun coffre n'existe encore.");
        await EnsureNotThrottledAsync(db, ct).ConfigureAwait(false);

        byte[] dek;
        try
        {
            dek = await UnwrapWithPasswordAsync(vault, masterPassword, ct).ConfigureAwait(false);
        }
        catch (IntegrityException)
        {
            await RegisterFailureAsync(db, ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditEventTypes.AuthenticationFailed, EntityTypes.Vault, vault.Id, success: false,
                detail: "Mot de passe maître", ct: ct).ConfigureAwait(false);
            throw new InvalidMasterPasswordException();
        }

        try
        {
            await OpenSessionAsync(db, vault, dek, ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
        await audit.RecordAsync(AuditEventTypes.Unlock, EntityTypes.Vault, vault.Id, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Verrouille : l'événement est scellé avant l'effacement de la clé.</summary>
    public async Task LockAsync(string reason = "Manuel")
    {
        if (!session.IsUnlocked) return;
        await audit.RecordAsync(AuditEventTypes.Lock, EntityTypes.Vault, session.VaultId, detail: reason).ConfigureAwait(false);
        session.Lock();
    }

    /// <summary>Vérifie le Master Password sans modifier la session (ré-authentification avant action sensible).</summary>
    public async Task<bool> VerifyMasterPasswordAsync(string masterPassword, CancellationToken ct = default)
    {
        await using var db = dbFactory.Create();
        var vault = await db.Vaults.AsNoTracking().FirstAsync(ct).ConfigureAwait(false);
        try
        {
            var dek = await UnwrapWithPasswordAsync(vault, masterPassword, ct).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(dek);
            return true;
        }
        catch (IntegrityException)
        {
            await audit.RecordAsync(AuditEventTypes.AuthenticationFailed, EntityTypes.Vault, vault.Id, success: false,
                detail: "Ré-authentification", ct: ct).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>Rotation du Master Password : seule l'enveloppe de la DEK change, aucun secret n'est rechiffré.</summary>
    public async Task ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        MasterPasswordPolicy.Ensure(newPassword);
        await using var db = dbFactory.Create();
        var vault = await db.Vaults.FirstAsync(ct).ConfigureAwait(false);

        byte[] dek;
        try
        {
            dek = await UnwrapWithPasswordAsync(vault, currentPassword, ct).ConfigureAwait(false);
        }
        catch (IntegrityException)
        {
            await audit.RecordAsync(AuditEventTypes.AuthenticationFailed, EntityTypes.Vault, vault.Id, success: false,
                detail: "Changement de mot de passe", ct: ct).ConfigureAwait(false);
            throw new InvalidMasterPasswordException();
        }

        try
        {
            await WrapWithPasswordAsync(vault, dek, newPassword, ct).ConfigureAwait(false);
            vault.PasswordChangedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
        await audit.RecordAsync(AuditEventTypes.MasterPasswordChanged, EntityTypes.Vault, vault.Id, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Génère une nouvelle clé de récupération ; l'ancienne cesse immédiatement de fonctionner.</summary>
    public async Task<string> RegenerateRecoveryKeyAsync(CancellationToken ct = default)
    {
        session.EnsureUnlocked();
        await using var db = dbFactory.Create();
        var vault = await db.Vaults.FirstAsync(ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var key = session.UseKey(dek => WrapWithNewRecoveryKey(vault, dek, now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.RecordAsync(AuditEventTypes.RecoveryKeyRegenerated, EntityTypes.Vault, vault.Id, ct: ct).ConfigureAwait(false);
        return key;
    }

    /// <summary>
    /// Récupération : la clé de récupération ouvre la DEK, un nouveau Master Password est défini
    /// et une nouvelle clé de récupération remplace celle qui vient d'être utilisée.
    /// </summary>
    public async Task<string> RecoverAsync(string recoveryKey, string newMasterPassword, CancellationToken ct = default)
    {
        MasterPasswordPolicy.Ensure(newMasterPassword);
        await using var db = dbFactory.Create();
        var vault = await db.Vaults.FirstOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw new InfraVaultException("Aucun coffre n'existe encore.");
        await EnsureNotThrottledAsync(db, ct).ConfigureAwait(false);

        byte[]? dek = null;
        try
        {
            try
            {
                dek = UnwrapWithRecoveryKey(vault, recoveryKey, encryption);
            }
            catch (InfraVaultException)
            {
                await RegisterFailureAsync(db, ct).ConfigureAwait(false);
                await audit.RecordAsync(AuditEventTypes.AuthenticationFailed, EntityTypes.Vault, vault.Id, success: false,
                    detail: "Clé de récupération", ct: ct).ConfigureAwait(false);
                throw new InvalidRecoveryKeyException();
            }

            var now = clock.UtcNow;
            await WrapWithPasswordAsync(vault, dek, newMasterPassword, ct).ConfigureAwait(false);
            vault.PasswordChangedAt = now;
            var newRecoveryKey = WrapWithNewRecoveryKey(vault, dek, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await OpenSessionAsync(db, vault, dek, ct).ConfigureAwait(false);
            await audit.RecordAsync(AuditEventTypes.RecoveryUsed, EntityTypes.Vault, vault.Id, ct: ct).ConfigureAwait(false);
            return newRecoveryKey;
        }
        finally
        {
            if (dek is not null) CryptographicOperations.ZeroMemory(dek);
        }
    }

    // ───────────────────────── Enveloppes ─────────────────────────

    /// <summary>Ouvre l'enveloppe « mot de passe » d'un coffre (ou d'un en-tête de sauvegarde).</summary>
    public static byte[] UnwrapWithPassword(Vault vault, string masterPassword, IKeyDerivationService kdf, IEncryptionService encryption)
    {
        KdfParameters parameters;
        try { parameters = KdfParameters.FromJson(vault.KdfParameters); }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new IntegrityException("Les paramètres cryptographiques du coffre sont illisibles.", ex);
        }

        var password = Encoding.UTF8.GetBytes(masterPassword ?? "");
        byte[]? kek = null;
        try
        {
            kek = kdf.DeriveKey(password, vault.Salt, parameters);
            return encryption.DecryptDetached(kek, vault.EncryptedDek, vault.DekNonce, vault.DekTag, Aad.VaultDek(vault.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (kek is not null) CryptographicOperations.ZeroMemory(kek);
        }
    }

    public static byte[] UnwrapWithRecoveryKey(Vault vault, string recoveryKey, IEncryptionService encryption)
    {
        if (!vault.HasRecoveryKey || vault.RecoverySalt is null || vault.RecoveryNonce is null || vault.RecoveryTag is null)
            throw new InfraVaultException("Aucune clé de récupération n'est configurée pour ce coffre.");
        if (!RecoveryKey.TryParse(recoveryKey, out var keyBytes))
            throw new InvalidRecoveryKeyException();

        var kek = new byte[CryptoConstants.KeySizeBytes];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, keyBytes, kek, vault.RecoverySalt, "infravault/v1/recovery-kek"u8);
            return encryption.DecryptDetached(kek, vault.RecoveryEncryptedDek, vault.RecoveryNonce, vault.RecoveryTag,
                Aad.VaultRecovery(vault.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private Task<byte[]> UnwrapWithPasswordAsync(Vault vault, string masterPassword, CancellationToken ct)
        => Task.Run(() => UnwrapWithPassword(vault, masterPassword, kdf, encryption), ct);

    private async Task WrapWithPasswordAsync(Vault vault, byte[] dek, string masterPassword, CancellationToken ct)
    {
        var parameters = kdf.DefaultParameters;
        var salt = RandomNumberGenerator.GetBytes(CryptoConstants.SaltSizeBytes);
        var password = Encoding.UTF8.GetBytes(masterPassword);
        byte[]? kek = null;
        try
        {
            kek = await Task.Run(() => kdf.DeriveKey(password, salt, parameters), ct).ConfigureAwait(false);
            var (ciphertext, nonce, tag) = encryption.EncryptDetached(kek, dek, Aad.VaultDek(vault.Id));
            vault.Salt = salt;
            vault.KdfAlgorithm = CryptoConstants.KdfAlgorithm;
            vault.KdfParameters = parameters.ToJson();
            vault.EncryptedDek = ciphertext;
            vault.DekNonce = nonce;
            vault.DekTag = tag;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            if (kek is not null) CryptographicOperations.ZeroMemory(kek);
        }
    }

    private string WrapWithNewRecoveryKey(Vault vault, ReadOnlySpan<byte> dek, DateTime now)
    {
        var keyBytes = RandomNumberGenerator.GetBytes(CryptoConstants.RecoveryKeyBytes);
        var salt = RandomNumberGenerator.GetBytes(CryptoConstants.SaltSizeBytes);
        var kek = new byte[CryptoConstants.KeySizeBytes];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, keyBytes, kek, salt, "infravault/v1/recovery-kek"u8);
            var (ciphertext, nonce, tag) = encryption.EncryptDetached(kek, dek, Aad.VaultRecovery(vault.Id));
            vault.RecoverySalt = salt;
            vault.RecoveryEncryptedDek = ciphertext;
            vault.RecoveryNonce = nonce;
            vault.RecoveryTag = tag;
            vault.RecoveryCreatedAt = now;
            return RecoveryKey.Format(keyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private async Task OpenSessionAsync(IVaultDb db, Vault vault, byte[] dek, CancellationToken ct)
    {
        var user = await db.Users.OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is null)
        {
            user = new User { Id = Guid.NewGuid(), Username = Environment.UserName, DisplayName = Environment.UserName, CreatedAt = clock.UtcNow };
            db.Users.Add(user);
        }

        // Rattrapage : nouvelles catégories système livrées par une version plus récente.
        var existing = await db.AssetTypes.Select(t => t.Key).ToListAsync(ct).ConfigureAwait(false);
        SeedSystemTypes(db, existing);

        var failures = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.AuthFailures, ct).ConfigureAwait(false);
        if (failures is not null) db.Settings.Remove(failures);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        session.Open(vault.Id, vault.Name, user.Id, user.DisplayName, dek);
    }

    private static void SeedSystemTypes(IVaultDb db, IReadOnlyCollection<string> existingKeys)
    {
        var order = 0;
        foreach (var def in SystemAssetTypes.All)
        {
            order += 10;
            if (existingKeys.Contains(def.Key)) continue;
            db.AssetTypes.Add(new AssetType
            {
                Id = Guid.NewGuid(),
                Key = def.Key,
                Name = def.Name,
                Description = def.Description,
                Icon = def.Icon,
                Color = def.Color,
                IsSystem = true,
                SortOrder = order,
                FieldSchema = FieldDefinition.Serialize(def.Fields),
            });
        }
    }

    // ───────────────────────── Anti force brute ─────────────────────────

    /// <summary>Délai imposé après des échecs répétés : 1 s, 2 s, 4 s… plafonné à 60 s.</summary>
    public static TimeSpan DelayFor(int failures)
        => failures < 3 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, failures - 3)));

    private async Task EnsureNotThrottledAsync(IVaultDb db, CancellationToken ct)
    {
        var state = await ReadFailuresAsync(db, ct).ConfigureAwait(false);
        if (state is null) return;
        var remaining = state.LastUtc + DelayFor(state.Count) - clock.UtcNow;
        if (remaining > TimeSpan.Zero) throw new AuthenticationThrottledException(remaining);
    }

    private async Task RegisterFailureAsync(IVaultDb db, CancellationToken ct)
    {
        var state = await ReadFailuresAsync(db, ct).ConfigureAwait(false);
        var next = new FailureState((state?.Count ?? 0) + 1, clock.UtcNow);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.AuthFailures, ct).ConfigureAwait(false);
        if (row is null) db.Settings.Add(new Setting { Key = SettingKeys.AuthFailures, Value = JsonSerializer.Serialize(next) });
        else row.Value = JsonSerializer.Serialize(next);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task<FailureState?> ReadFailuresAsync(IVaultDb db, CancellationToken ct)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKeys.AuthFailures, ct).ConfigureAwait(false);
        if (row is null) return null;
        try { return JsonSerializer.Deserialize<FailureState>(row.Value); }
        catch (JsonException) { return null; }
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);
}
