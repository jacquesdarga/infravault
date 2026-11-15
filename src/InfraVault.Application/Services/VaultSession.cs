using System.Security.Cryptography;
using System.Text;
using InfraVault.Application.Interfaces;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Services;

/// <summary>
/// Détient la DEK et ses sous-clés dans des tampons épinglés (jamais déplacés par le GC),
/// effacés à zéro au verrouillage. Toute opération de chiffrement passe par ici.
/// </summary>
public sealed class VaultSession(IEncryptionService encryption) : IVaultSession, IDisposable
{
    private readonly object _gate = new();
    private byte[]? _dek;
    private byte[]? _indexKey;
    private byte[]? _auditKey;

    public bool IsUnlocked { get { lock (_gate) return _dek is not null; } }
    public Guid VaultId { get; private set; }
    public Guid? UserId { get; private set; }
    public string VaultName { get; private set; } = "";
    public string UserName { get; private set; } = "";
    public DateTime? UnlockedAtUtc { get; private set; }
    public event EventHandler? StateChanged;

    public void Open(Guid vaultId, string vaultName, Guid userId, string userName, ReadOnlySpan<byte> dek)
    {
        if (dek.Length != CryptoConstants.KeySizeBytes)
            throw new ArgumentException("Taille de clé invalide.", nameof(dek));

        lock (_gate)
        {
            Wipe();
            _dek = Pinned(CryptoConstants.KeySizeBytes);
            dek.CopyTo(_dek);
            _indexKey = Pinned(CryptoConstants.KeySizeBytes);
            _auditKey = Pinned(CryptoConstants.KeySizeBytes);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _dek, _indexKey, salt: [], info: "infravault/v1/blind-index"u8);
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _dek, _auditKey, salt: [], info: "infravault/v1/audit-seal"u8);
            VaultId = vaultId;
            VaultName = vaultName;
            UserId = userId;
            UserName = userName;
            UnlockedAtUtc = DateTime.UtcNow;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Lock()
    {
        bool wasUnlocked;
        lock (_gate)
        {
            wasUnlocked = _dek is not null;
            Wipe();
            UnlockedAtUtc = null;
        }
        if (wasUnlocked) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EnsureUnlocked()
    {
        if (!IsUnlocked) throw new VaultLockedException();
    }

    public byte[] Seal(ReadOnlySpan<byte> plaintext, string associatedData)
    {
        lock (_gate)
        {
            var key = _dek ?? throw new VaultLockedException();
            return encryption.Seal(key, plaintext, Encoding.UTF8.GetBytes(associatedData));
        }
    }

    public byte[] SealString(string plaintext, string associatedData)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return Seal(bytes, associatedData); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public byte[] Open(ReadOnlySpan<byte> box, string associatedData)
    {
        lock (_gate)
        {
            var key = _dek ?? throw new VaultLockedException();
            return encryption.Open(key, box, Encoding.UTF8.GetBytes(associatedData));
        }
    }

    public string OpenString(ReadOnlySpan<byte> box, string associatedData)
    {
        var bytes = Open(box, associatedData);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public byte[] BlindIndex(string value)
    {
        lock (_gate)
        {
            var key = _indexKey ?? throw new VaultLockedException();
            return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        }
    }

    public byte[] AuditMac(ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            var key = _auditKey ?? throw new VaultLockedException();
            return HMACSHA256.HashData(key, data);
        }
    }

    public T UseKey<T>(KeyFunc<T> func)
    {
        lock (_gate)
        {
            var key = _dek ?? throw new VaultLockedException();
            return func(key);
        }
    }

    public void Dispose() => Lock();

    private static byte[] Pinned(int length) => GC.AllocateArray<byte>(length, pinned: true);

    private void Wipe()
    {
        foreach (var buffer in new[] { _dek, _indexKey, _auditKey })
        {
            if (buffer is not null) CryptographicOperations.ZeroMemory(buffer);
        }
        _dek = _indexKey = _auditKey = null;
    }
}
