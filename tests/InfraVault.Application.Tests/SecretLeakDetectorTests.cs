using InfraVault.Application.Security;

namespace InfraVault.Application.Tests;

/// <summary>
/// §7 : la documentation ne doit pas devenir une porte dérobée pour stocker des secrets en clair.
/// Le détecteur doit attraper les formes courantes sans crier au loup sur de la prose normale.
/// </summary>
public sealed class SecretLeakDetectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LooksLikeSecret_est_faux_sur_une_saisie_vide(string? content)
    {
        Assert.False(SecretLeakDetector.LooksLikeSecret(content));
    }

    [Theory]
    [InlineData("password: hunter2xyz")]
    [InlineData("password=hunter2xyz")]
    [InlineData("passwd : correct-horse")]
    [InlineData("pwd=Tr0ub4dor3x")]
    [InlineData("mot de passe : SuperSecret1")]
    [InlineData("motdepasse: SuperSecret1")]
    [InlineData("mdp = SuperSecret1")]
    [InlineData("secret: abcdef123456")]
    [InlineData("token: abcdef123456")]
    [InlineData("api_key = 1234567890abcdef")]
    [InlineData("api-key: 1234567890abcdef")]
    [InlineData("apikey:1234567890abcdef")]
    public void LooksLikeSecret_detecte_une_affectation_de_secret(string content)
    {
        Assert.True(SecretLeakDetector.LooksLikeSecret(content));
    }

    [Theory]
    [InlineData("PASSWORD: Secret123456")]
    [InlineData("Mot De Passe = Secret123456")]
    [InlineData("Api_Key: Secret123456")]
    public void LooksLikeSecret_ignore_la_casse(string content)
    {
        Assert.True(SecretLeakDetector.LooksLikeSecret(content));
    }

    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----")]
    [InlineData("-----BEGIN EC PRIVATE KEY-----")]
    public void LooksLikeSecret_detecte_une_cle_privee(string content)
    {
        Assert.True(SecretLeakDetector.LooksLikeSecret(content));
    }

    [Fact]
    public void LooksLikeSecret_detecte_une_cle_privee_au_milieu_d_un_document()
    {
        var doc = "Procédure de restauration\n\nÉtape 3 : copier la clé\n-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n";

        Assert.True(SecretLeakDetector.LooksLikeSecret(doc));
    }

    [Theory]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("ghp_1234567890abcdefghijklmnopqrstuvwx")]
    [InlineData("gho_1234567890abcdefghijklmnopqrstuvwx")]
    [InlineData("glpat-1234567890abcdefghijkl")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc")]
    public void LooksLikeSecret_detecte_les_jetons_reconnaissables(string content)
    {
        Assert.True(SecretLeakDetector.LooksLikeSecret(content));
    }

    [Theory]
    [InlineData("Le mot de passe est stocké dans InfraVault, fiche « SRV-WEB-01 ».")]
    [InlineData("Pensez à changer le password tous les 90 jours.")]
    [InlineData("Ce runbook décrit la rotation des tokens d'API.")]
    [InlineData("Procédure de redémarrage du serveur de production.")]
    [InlineData("L'API key se trouve dans le coffre.")]
    public void LooksLikeSecret_ne_se_declenche_pas_sur_de_la_prose(string content)
    {
        Assert.False(SecretLeakDetector.LooksLikeSecret(content));
    }

    [Fact]
    public void LooksLikeSecret_exige_une_valeur_de_longueur_credible()
    {
        // Moins de 6 caractères après le signe : trop court pour être un vrai secret.
        Assert.False(SecretLeakDetector.LooksLikeSecret("password: abc"));
        Assert.True(SecretLeakDetector.LooksLikeSecret("password: abcdef"));
    }
}
