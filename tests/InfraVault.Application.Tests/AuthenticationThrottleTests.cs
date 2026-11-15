using InfraVault.Application.Services;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Tests;

/// <summary>
/// Le délai progressif est la seule défense contre une attaque hors ligne menée depuis l'interface.
/// Trop court, il ne freine rien ; sans plafond, il transforme une faute de frappe en coffre inutilisable.
/// </summary>
public sealed class AuthenticationThrottleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Les_deux_premieres_erreurs_ne_sont_pas_penalisees(int failures)
    {
        // Se tromper une ou deux fois de mot de passe est normal.
        Assert.Equal(TimeSpan.Zero, AuthenticationService.DelayFor(failures));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 16)]
    [InlineData(8, 32)]
    public void Le_delai_double_a_chaque_echec_supplementaire(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), AuthenticationService.DelayFor(failures));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Le_delai_est_plafonne_a_60_secondes(int failures)
    {
        // Sans plafond, 2^50 secondes fermerait le coffre pour toujours.
        Assert.Equal(TimeSpan.FromSeconds(60), AuthenticationService.DelayFor(failures));
    }

    [Fact]
    public void Le_delai_ne_decroit_jamais()
    {
        var previous = TimeSpan.Zero;
        for (var failures = 0; failures <= 100; failures++)
        {
            var delay = AuthenticationService.DelayFor(failures);

            Assert.True(delay >= previous, $"Le délai a diminué après {failures} échecs.");
            previous = delay;
        }
    }

    [Fact]
    public void Un_compteur_negatif_ne_penalise_pas()
    {
        Assert.Equal(TimeSpan.Zero, AuthenticationService.DelayFor(-1));
    }

    [Fact]
    public void L_exception_annonce_le_temps_restant_a_l_utilisateur()
    {
        var ex = new AuthenticationThrottledException(TimeSpan.FromSeconds(7.2));

        Assert.Equal(TimeSpan.FromSeconds(7.2), ex.RetryAfter);
        // Arrondi à la seconde supérieure : on ne promet jamais un délai plus court que la réalité.
        Assert.Contains("8", ex.Message);
    }
}
