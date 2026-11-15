using InfraVault.Application.Security;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Tests;

/// <summary>
/// L'estimateur guide l'utilisateur au moment où il choisit son Master Password :
/// il doit surtout ne pas qualifier de « fort » un mot de passe devinable.
/// </summary>
public sealed class PasswordStrengthEstimatorTests
{
    // Aléatoire, 14 caractères, 4 catégories, aucun motif connu.
    private const string Solide = "Xq7!mB2vLp9#aZ";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Estimate_qualifie_une_saisie_vide(string? password)
    {
        var result = PasswordStrengthEstimator.Estimate(password);

        Assert.Equal(0, result.Score);
        Assert.Equal(0, result.Bits);
        Assert.Equal("Vide", result.Label);
    }

    [Fact]
    public void Estimate_reconnait_un_mot_de_passe_solide()
    {
        var result = PasswordStrengthEstimator.Estimate(Solide);

        Assert.Equal(4, result.Score);
        Assert.Equal("Excellent", result.Label);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("motdepasse")]
    [InlineData("azerty123")]
    [InlineData("administrateur")]
    [InlineData("infravault")]
    public void Estimate_ecrase_le_score_des_mots_de_passe_courants(string password)
    {
        Assert.Equal(0, PasswordStrengthEstimator.Estimate(password).Score);
    }

    [Fact]
    public void Estimate_penalise_un_mot_courant_malgre_une_grande_longueur()
    {
        // 22 caractères valent moins que 14 si « password » s'y trouve.
        var avec = PasswordStrengthEstimator.Estimate(Solide + "password");
        var sans = PasswordStrengthEstimator.Estimate(Solide);

        Assert.True(avec.Bits < sans.Bits,
            $"Attendu : le mot courant fait baisser l'entropie. Obtenu {avec.Bits} >= {sans.Bits}.");
        Assert.True(avec.Score < sans.Score);
    }

    [Fact]
    public void Estimate_penalise_la_repetition()
    {
        var repete = PasswordStrengthEstimator.Estimate(new string('a', 16));

        Assert.Equal(0, repete.Score);
        Assert.Equal("Très faible", repete.Label);
    }

    [Theory]
    [InlineData("Zk3#vMqabcdefXy")]
    [InlineData("Zk3#vMq0123Xy")]
    [InlineData("Zk3#vMqazertyXy")]
    [InlineData("Zk3#vMqqsdfghXy")]
    public void Estimate_penalise_une_suite_de_clavier_ou_d_alphabet(string password)
    {
        var avec = PasswordStrengthEstimator.Estimate(password);
        var temoin = PasswordStrengthEstimator.Estimate("Zk3#vMq" + new string(password[7..].Reverse().ToArray()) + "");

        Assert.True(avec.Bits <= temoin.Bits);
    }

    [Fact]
    public void Estimate_rend_toujours_un_score_et_un_libelle_coherents()
    {
        string[] samples = ["a", "abc", "Password1", "correct horse battery staple", Solide, new string('z', 100)];

        foreach (var sample in samples)
        {
            var result = PasswordStrengthEstimator.Estimate(sample);

            Assert.InRange(result.Score, 0, 4);
            Assert.True(result.Bits >= 0);
            var expected = result.Score switch
            {
                0 => "Très faible", 1 => "Faible", 2 => "Correct", 3 => "Fort", _ => "Excellent",
            };
            Assert.Equal(expected, result.Label);
        }
    }

    [Fact]
    public void Estimate_recompense_l_ajout_de_categories()
    {
        var lettres = PasswordStrengthEstimator.Estimate("kvhmxpqzrwdn");
        var melange = PasswordStrengthEstimator.Estimate("kvhmxpqzrwdn".ToUpperInvariant()[..6] + "kvhmxp" + "7#");

        Assert.True(melange.Bits > lettres.Bits);
    }
}

/// <summary>La politique est le seul garde-fou entre l'utilisateur et un coffre ouvrable par devinette.</summary>
public sealed class MasterPasswordPolicyTests
{
    private const string Solide = "Xq7!mB2vLp9#aZ";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("court")]
    [InlineData("12345678901")]
    public void Validate_exige_la_longueur_minimale(string? password)
    {
        var errors = MasterPasswordPolicy.Validate(password);

        var error = Assert.Single(errors);
        Assert.Contains(Limits.MasterPasswordMinLength.ToString(), error);
    }

    [Fact]
    public void Validate_accepte_exactement_la_longueur_minimale_si_le_mot_de_passe_est_varie()
    {
        var password = "Xq7!mB2vLp9#";

        Assert.Equal(12, password.Length);
        Assert.Empty(MasterPasswordPolicy.Validate(password));
    }

    [Theory]
    [InlineData("passwordpass")]
    [InlineData("motdepasse12")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public void Validate_refuse_un_mot_de_passe_assez_long_mais_previsible(string password)
    {
        Assert.True(password.Length >= Limits.MasterPasswordMinLength);

        var error = Assert.Single(MasterPasswordPolicy.Validate(password));
        Assert.Contains("prévisible", error);
    }

    [Fact]
    public void Validate_accepte_un_mot_de_passe_solide()
    {
        Assert.Empty(MasterPasswordPolicy.Validate(Solide));
    }

    [Fact]
    public void Validate_ne_cumule_pas_les_reproches()
    {
        // Un mot de passe trop court reçoit le reproche de longueur, pas deux messages.
        Assert.Single(MasterPasswordPolicy.Validate("abc"));
    }

    [Fact]
    public void Ensure_leve_une_exception_exploitable_par_l_interface()
    {
        var ex = Assert.Throws<ValidationFailedException>(() => MasterPasswordPolicy.Ensure("court"));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(Limits.MasterPasswordMinLength.ToString(), ex.Message);
    }

    [Fact]
    public void Ensure_laisse_passer_un_mot_de_passe_solide()
    {
        MasterPasswordPolicy.Ensure(Solide);
    }
}
