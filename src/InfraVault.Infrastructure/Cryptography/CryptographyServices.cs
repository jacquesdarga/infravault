using System.Security.Cryptography;
using InfraVault.Application.Interfaces;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using Konscious.Security.Cryptography;

namespace InfraVault.Infrastructure.Cryptography;

/// <summary>
/// AES-256-GCM via les API cryptographiques de .NET. Nonce aléatoire de 96 bits tiré du CSPRNG à
/// chaque chiffrement, tag de 128 bits. Toute altération (chiffré, nonce, tag, AAD) ou mauvaise clé
/// se traduit par une <see cref="IntegrityException"/> — jamais par des données erronées.
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const int HeaderSize = 1 + CryptoConstants.NonceSizeBytes + CryptoConstants.TagSizeBytes;

    public byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        EnsureKey(key);
        var box = new byte[HeaderSize + plaintext.Length];
        box[0] = CryptoConstants.SealedBoxVersion;
        var nonce = box.AsSpan(1, CryptoConstants.NonceSizeBytes);
        var tag = box.AsSpan(1 + CryptoConstants.NonceSizeBytes, CryptoConstants.TagSizeBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, CryptoConstants.TagSizeBytes);
        aes.Encrypt(nonce, plaintext, box.AsSpan(HeaderSize), tag, associatedData);
        return box;
    }

    public byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> box, ReadOnlySpan<byte> associatedData)
    {
        EnsureKey(key);
        if (box.Length < HeaderSize || box[0] != CryptoConstants.SealedBoxVersion) throw new IntegrityException();
        return DecryptDetached(key, box[HeaderSize..], box.Slice(1, CryptoConstants.NonceSizeBytes),
            box.Slice(1 + CryptoConstants.NonceSizeBytes, CryptoConstants.TagSizeBytes), associatedData);
    }

    public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptDetached(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
        EnsureKey(key);
        var nonce = RandomNumberGenerator.GetBytes(CryptoConstants.NonceSizeBytes);
        var tag = new byte[CryptoConstants.TagSizeBytes];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, CryptoConstants.TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, nonce, tag);
    }

    public byte[] DecryptDetached(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> tag, ReadOnlySpan<byte> associatedData)
    {
        EnsureKey(key);
        if (nonce.Length != CryptoConstants.NonceSizeBytes || tag.Length != CryptoConstants.TagSizeBytes)
            throw new IntegrityException();
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, CryptoConstants.TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new IntegrityException("Contrôle d'intégrité échoué : la donnée chiffrée est altérée ou la clé est incorrecte.", ex);
        }
    }

    private static void EnsureKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != CryptoConstants.KeySizeBytes)
            throw new ArgumentException("La clé doit faire 256 bits.", nameof(key));
    }
}

/// <summary>Argon2id (RFC 9106) — implémentation managée Konscious, isolée derrière l'interface pour rester remplaçable.</summary>
public sealed class Argon2KeyDerivationService(KdfParameters? defaults = null) : IKeyDerivationService
{
    public KdfParameters DefaultParameters { get; } = defaults ?? KdfParameters.Default;

    public byte[] DeriveKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, KdfParameters parameters)
    {
        parameters.EnsureValid();
        if (salt.Length < 8) throw new ArgumentException("Salt trop court.", nameof(salt));
        var passwordCopy = password.ToArray();
        try
        {
            using var argon2 = new Argon2id(passwordCopy)
            {
                Salt = salt.ToArray(),
                DegreeOfParallelism = parameters.Parallelism,
                MemorySize = parameters.MemoryKiB,
                Iterations = parameters.Iterations,
            };
            return argon2.GetBytes(parameters.OutputLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordCopy);
        }
    }
}
