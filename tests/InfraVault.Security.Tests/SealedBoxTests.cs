using System.Security.Cryptography;
using System.Text;
using InfraVault.Application.Interfaces;
using InfraVault.Infrastructure.Cryptography;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Security.Tests;

/// <summary>
/// §37 : « Toute altération d'un ciphertext doit être détectée ».
/// Chaque test abîme un octet précis de la boîte scellée et exige un refus net —
/// jamais un déchiffrement silencieux en données fausses.
/// </summary>
public sealed class SealedBoxTests
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService();

    private const int HeaderSize = 1 + CryptoConstants.NonceSizeBytes + CryptoConstants.TagSizeBytes;

    private static byte[] Key(byte seed = 1)
    {
        var key = new byte[CryptoConstants.KeySizeBytes];
        Array.Fill(key, seed);
        return key;
    }

    private static byte[] Aad(string text = "contexte:1") => Encoding.UTF8.GetBytes(text);

    private static byte[] Plaintext(string text = "mot de passe du routeur") => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void Seal_puis_Open_redonne_le_texte_clair()
    {
        var box = Encryption.Seal(Key(), Plaintext(), Aad());

        Assert.Equal(Plaintext(), Encryption.Open(Key(), box, Aad()));
    }

    [Fact]
    public void La_boite_respecte_le_format_version_nonce_tag_chiffre()
    {
        var plaintext = Plaintext();

        var box = Encryption.Seal(Key(), plaintext, Aad());

        Assert.Equal(HeaderSize + plaintext.Length, box.Length);
        Assert.Equal(CryptoConstants.SealedBoxVersion, box[0]);
    }

    [Fact]
    public void Le_chiffre_ne_laisse_pas_transparaitre_le_clair()
    {
        var plaintext = Plaintext();

        var box = Encryption.Seal(Key(), plaintext, Aad());

        Assert.DoesNotContain(Encoding.UTF8.GetString(plaintext), Encoding.UTF8.GetString(box));
    }

    [Fact]
    public void Deux_chiffrements_du_meme_clair_different()
    {
        // Nonce aléatoire à chaque fois : sans cela, deux fiches au même mot de passe
        // produiraient le même chiffré et la base trahirait les doublons.
        var first = Encryption.Seal(Key(), Plaintext(), Aad());
        var second = Encryption.Seal(Key(), Plaintext(), Aad());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Les_nonces_ne_se_repetent_pas()
    {
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 500; i++)
        {
            var box = Encryption.Seal(Key(), Plaintext(), Aad());
            nonces.Add(Convert.ToHexString(box.AsSpan(1, CryptoConstants.NonceSizeBytes)));
        }

        // Une répétition de nonce sous la même clé casserait complètement AES-GCM.
        Assert.Equal(500, nonces.Count);
    }

    [Fact]
    public void Une_mauvaise_cle_est_refusee()
    {
        var box = Encryption.Seal(Key(1), Plaintext(), Aad());

        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(2), box, Aad()));
    }

    [Fact]
    public void Des_donnees_associees_differentes_sont_refusees()
    {
        var box = Encryption.Seal(Key(), Plaintext(), Aad("credential:aaa:secret"));

        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), box, Aad("credential:bbb:secret")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(20)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(35)]
    public void Un_seul_octet_modifie_suffit_a_faire_echouer_l_ouverture(int index)
    {
        // index 0 = version, 1-12 = nonce, 13-28 = tag, 29+ = chiffré.
        var box = Encryption.Seal(Key(), Plaintext(), Aad());
        box[index] ^= 0x01;

        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), box, Aad()));
    }

    [Fact]
    public void Tous_les_octets_sont_couverts_par_le_controle_d_integrite()
    {
        var reference = Encryption.Seal(Key(), Plaintext(), Aad());

        for (var index = 0; index < reference.Length; index++)
        {
            var box = (byte[])reference.Clone();
            box[index] ^= 0xFF;

            Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), box, Aad()));
        }
    }

    [Fact]
    public void Une_boite_tronquee_est_refusee()
    {
        var box = Encryption.Seal(Key(), Plaintext(), Aad());

        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), box.AsSpan(0, HeaderSize - 1).ToArray(), Aad()));
        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), [], Aad()));
        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), box.AsSpan(0, box.Length - 1).ToArray(), Aad()));
    }

    [Fact]
    public void Une_version_de_format_inconnue_est_refusee()
    {
        // Un format futur ne doit pas être interprété au hasard par une version ancienne.
        var box = Encryption.Seal(Key(), Plaintext(), Aad());
        box[0] = 2;

        Assert.Throws<IntegrityException>(() => Encryption.Open(Key(), box, Aad()));
    }

    [Fact]
    public void Un_clair_vide_reste_chiffrable()
    {
        var box = Encryption.Seal(Key(), [], Aad());

        Assert.Equal(HeaderSize, box.Length);
        Assert.Empty(Encryption.Open(Key(), box, Aad()));
    }

    [Fact]
    public void Un_contenu_volumineux_traverse_sans_perte()
    {
        var plaintext = RandomNumberGenerator.GetBytes(512 * 1024);

        var box = Encryption.Seal(Key(), plaintext, Aad());

        Assert.Equal(plaintext, Encryption.Open(Key(), box, Aad()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void Une_cle_qui_n_est_pas_de_256_bits_est_refusee(int keyLength)
    {
        Assert.Throws<ArgumentException>(() => Encryption.Seal(new byte[keyLength], Plaintext(), Aad()));
    }

    [Fact]
    public void EncryptDetached_puis_DecryptDetached_redonne_le_clair()
    {
        var (ciphertext, nonce, tag) = Encryption.EncryptDetached(Key(), Plaintext(), Aad());

        Assert.Equal(CryptoConstants.NonceSizeBytes, nonce.Length);
        Assert.Equal(CryptoConstants.TagSizeBytes, tag.Length);
        Assert.Equal(Plaintext(), Encryption.DecryptDetached(Key(), ciphertext, nonce, tag, Aad()));
    }

    [Fact]
    public void DecryptDetached_refuse_un_nonce_ou_un_tag_de_mauvaise_taille()
    {
        var (ciphertext, nonce, tag) = Encryption.EncryptDetached(Key(), Plaintext(), Aad());

        Assert.Throws<IntegrityException>(() => Encryption.DecryptDetached(Key(), ciphertext, new byte[11], tag, Aad()));
        Assert.Throws<IntegrityException>(() => Encryption.DecryptDetached(Key(), ciphertext, nonce, new byte[15], Aad()));
    }

    [Fact]
    public void DecryptDetached_refuse_un_tag_d_un_autre_message()
    {
        var first = Encryption.EncryptDetached(Key(), Plaintext("premier secret"), Aad());
        var second = Encryption.EncryptDetached(Key(), Plaintext("second secret"), Aad());

        Assert.Throws<IntegrityException>(() =>
            Encryption.DecryptDetached(Key(), first.Ciphertext, first.Nonce, second.Tag, Aad()));
    }
}
