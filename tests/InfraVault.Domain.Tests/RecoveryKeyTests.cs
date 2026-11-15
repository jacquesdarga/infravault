using System.Security.Cryptography;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;

namespace InfraVault.Domain.Tests;

/// <summary>
/// La clé de récupération est le dernier recours de l'utilisateur : une clé qu'il recopie mal
/// et qui est refusée à tort lui fait perdre son coffre définitivement.
/// </summary>
public sealed class RecoveryKeyTests
{
    private static byte[] SampleKey()
    {
        var bytes = new byte[CryptoConstants.RecoveryKeyBytes];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 11 + 3);
        return bytes;
    }

    [Fact]
    public void Format_produit_32_symboles_en_groupes_de_4()
    {
        var text = RecoveryKey.Format(SampleKey());

        // 20 octets = 160 bits = 32 symboles Base32, séparés par 7 tirets.
        Assert.Equal(39, text.Length);
        Assert.Equal(7, text.Count(c => c == '-'));
        Assert.All(text.Split('-'), group => Assert.Equal(4, group.Length));
    }

    [Fact]
    public void Format_n_utilise_que_l_alphabet_Base32_sans_caracteres_ambigus()
    {
        var text = RecoveryKey.Format(SampleKey()).Replace("-", "");

        Assert.All(text, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
        // 0, 1 et 8 sont exclus de l'alphabet : ils se confondent avec O, I et B.
        Assert.DoesNotContain('0', text);
        Assert.DoesNotContain('1', text);
        Assert.DoesNotContain('8', text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(32)]
    public void Format_refuse_une_longueur_inattendue(int length)
    {
        Assert.Throws<ArgumentException>(() => RecoveryKey.Format(new byte[length]));
    }

    [Fact]
    public void Format_puis_TryParse_redonne_les_memes_octets()
    {
        var original = SampleKey();

        Assert.True(RecoveryKey.TryParse(RecoveryKey.Format(original), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void TryParse_accepte_une_cle_aleatoire_quelle_qu_elle_soit()
    {
        // Le découpage en groupes de 5 bits ne doit dépendre d'aucune valeur d'octet particulière.
        for (var i = 0; i < 200; i++)
        {
            var original = RandomNumberGenerator.GetBytes(CryptoConstants.RecoveryKeyBytes);

            Assert.True(RecoveryKey.TryParse(RecoveryKey.Format(original), out var parsed));
            Assert.Equal(original, parsed);
        }
    }

    [Fact]
    public void TryParse_ignore_la_casse_les_tirets_et_les_espaces()
    {
        var original = SampleKey();
        var text = RecoveryKey.Format(original);

        Assert.True(RecoveryKey.TryParse(text.ToLowerInvariant(), out var lower));
        Assert.True(RecoveryKey.TryParse(text.Replace("-", ""), out var noDash));
        Assert.True(RecoveryKey.TryParse(text.Replace("-", " "), out var spaced));
        Assert.True(RecoveryKey.TryParse("  " + text + "\r\n", out var padded));

        Assert.Equal(original, lower);
        Assert.Equal(original, noDash);
        Assert.Equal(original, spaced);
        Assert.Equal(original, padded);
    }

    [Fact]
    public void TryParse_corrige_les_confusions_de_lecture_courantes()
    {
        var original = SampleKey();
        var text = RecoveryKey.Format(original);

        // L'utilisateur qui recopie à la main écrit facilement 0 pour O, 1 pour I et 8 pour B.
        var mistyped = text.Replace('O', '0').Replace('I', '1').Replace('B', '8');

        Assert.True(RecoveryKey.TryParse(mistyped, out var parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_refuse_une_saisie_vide(string? text)
    {
        Assert.False(RecoveryKey.TryParse(text, out var bytes));
        Assert.Empty(bytes);
    }

    [Fact]
    public void TryParse_refuse_un_caractere_hors_alphabet()
    {
        var text = RecoveryKey.Format(SampleKey());
        var corrupted = "9" + text[1..];

        Assert.False(RecoveryKey.TryParse(corrupted, out _));
    }

    [Fact]
    public void TryParse_refuse_une_cle_tronquee_ou_trop_longue()
    {
        var text = RecoveryKey.Format(SampleKey());

        Assert.False(RecoveryKey.TryParse(text[..^1], out _));
        Assert.False(RecoveryKey.TryParse(text + "A", out _));
    }
}
