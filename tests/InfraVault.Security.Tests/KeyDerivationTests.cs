using System.Text;
using InfraVault.Domain.ValueObjects;
using InfraVault.Infrastructure.Cryptography;
using InfraVault.Shared.Constants;

namespace InfraVault.Security.Tests;

/// <summary>
/// Argon2id transforme le Master Password en KEK. Si la dérivation n'était pas déterministe,
/// aucun coffre ne se rouvrirait ; si elle ne dépendait pas du sel, deux coffres au même mot
/// de passe partageraient leur clé.
/// </summary>
public sealed class KeyDerivationTests
{
    // Paramètres volontairement minimaux : on teste la dérivation, pas la résistance.
    private static readonly KdfParameters Fast = new(8 * 1024, 1, 1);

    private static readonly Argon2KeyDerivationService Kdf = new(Fast);

    private static byte[] Password(string text = "Xq7!mB2vLp9#aZ") => Encoding.UTF8.GetBytes(text);

    private static byte[] Salt(byte seed = 7)
    {
        var salt = new byte[CryptoConstants.SaltSizeBytes];
        Array.Fill(salt, seed);
        return salt;
    }

    [Fact]
    public void La_derivation_est_deterministe()
    {
        var first = Kdf.DeriveKey(Password(), Salt(), Fast);
        var second = Kdf.DeriveKey(Password(), Salt(), Fast);

        Assert.Equal(first, second);
    }

    [Fact]
    public void La_cle_fait_256_bits()
    {
        Assert.Equal(CryptoConstants.KeySizeBytes, Kdf.DeriveKey(Password(), Salt(), Fast).Length);
    }

    [Fact]
    public void Un_mot_de_passe_different_donne_une_cle_differente()
    {
        var first = Kdf.DeriveKey(Password("Xq7!mB2vLp9#aZ"), Salt(), Fast);
        var second = Kdf.DeriveKey(Password("Xq7!mB2vLp9#aY"), Salt(), Fast);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Un_sel_different_donne_une_cle_differente()
    {
        // C'est ce qui empêche une table précalculée de servir sur plusieurs coffres.
        var first = Kdf.DeriveKey(Password(), Salt(7), Fast);
        var second = Kdf.DeriveKey(Password(), Salt(8), Fast);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(16 * 1024, 1, 1)]
    [InlineData(8 * 1024, 2, 1)]
    [InlineData(8 * 1024, 1, 2)]
    public void Des_parametres_differents_donnent_une_cle_differente(int memoryKiB, int iterations, int parallelism)
    {
        var reference = Kdf.DeriveKey(Password(), Salt(), Fast);
        var other = Kdf.DeriveKey(Password(), Salt(), new KdfParameters(memoryKiB, iterations, parallelism));

        Assert.NotEqual(reference, other);
    }

    [Fact]
    public void Un_sel_trop_court_est_refuse()
    {
        Assert.Throws<ArgumentException>(() => Kdf.DeriveKey(Password(), new byte[7], Fast));
    }

    [Fact]
    public void Des_parametres_hors_bornes_sont_refuses_avant_tout_calcul()
    {
        // Sans cette validation, un fichier de coffre altéré pourrait réclamer des gigaoctets de RAM.
        Assert.Throws<FormatException>(() => Kdf.DeriveKey(Password(), Salt(), new KdfParameters(1, 1, 1)));
        Assert.Throws<FormatException>(() => Kdf.DeriveKey(Password(), Salt(), new KdfParameters(8 * 1024, 0, 1)));
    }

    [Fact]
    public void Les_parametres_par_defaut_sont_ceux_du_coffre()
    {
        Assert.Equal(KdfParameters.Default, new Argon2KeyDerivationService().DefaultParameters);
        Assert.Equal(Fast, Kdf.DefaultParameters);
    }

    [Fact]
    public void L_algorithme_annonce_est_bien_Argon2id()
    {
        Assert.Equal("Argon2id", CryptoConstants.KdfAlgorithm);
    }
}
