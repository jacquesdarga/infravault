using InfraVault.Application.Security;
using InfraVault.Shared.Exceptions;
using InfraVault.Tests.Shared;

namespace InfraVault.Security.Tests;

/// <summary>
/// Les données associées lient chaque chiffré à sa ligne et à son champ. Sans elles, un chiffré
/// recopié d'une fiche vers une autre s'ouvrirait normalement : il suffirait de déplacer des octets
/// dans la base pour échanger silencieusement deux secrets.
/// </summary>
public sealed class AadBindingTests
{
    [Fact]
    public async Task Un_secret_ne_s_ouvre_qu_avec_les_donnees_associees_d_origine()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var aad = Aad.CredentialSecret(Guid.NewGuid());

        var box = harness.Session.SealString("mot de passe du routeur", aad);

        Assert.Equal("mot de passe du routeur", harness.Session.OpenString(box, aad));
    }

    [Fact]
    public async Task Un_secret_deplace_vers_une_autre_fiche_devient_illisible()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var ficheA = Guid.NewGuid();
        var ficheB = Guid.NewGuid();

        var box = harness.Session.SealString("mot de passe du routeur", Aad.CredentialSecret(ficheA));

        Assert.Throws<IntegrityException>(() => harness.Session.OpenString(box, Aad.CredentialSecret(ficheB)));
    }

    [Fact]
    public async Task Un_secret_deplace_vers_un_autre_champ_de_la_meme_fiche_devient_illisible()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var fiche = Guid.NewGuid();

        var box = harness.Session.SealString("root", Aad.CredentialUsername(fiche));

        Assert.Throws<IntegrityException>(() => harness.Session.OpenString(box, Aad.CredentialSecret(fiche)));
        Assert.Throws<IntegrityException>(() => harness.Session.OpenString(box, Aad.CredentialNotes(fiche)));
    }

    [Fact]
    public async Task Une_propriete_de_ressource_est_liee_a_sa_ressource_et_a_sa_cle()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var ressourceA = Guid.NewGuid();
        var ressourceB = Guid.NewGuid();

        var box = harness.Session.SealString("secret-ipmi", Aad.AssetProperty(ressourceA, "ipmi_password"));

        Assert.Throws<IntegrityException>(
            () => harness.Session.OpenString(box, Aad.AssetProperty(ressourceB, "ipmi_password")));
        Assert.Throws<IntegrityException>(
            () => harness.Session.OpenString(box, Aad.AssetProperty(ressourceA, "bmc_password")));
    }

    [Fact]
    public void Chaque_emplacement_logique_produit_des_donnees_associees_distinctes()
    {
        var premier = Guid.NewGuid();
        var second = Guid.NewGuid();

        var contexts = new[]
        {
            Aad.CredentialUsername(premier), Aad.CredentialSecret(premier), Aad.CredentialNotes(premier),
            Aad.CredentialVersion(premier), Aad.DocumentContent(premier), Aad.DocumentVersion(premier),
            Aad.AttachmentName(premier), Aad.AttachmentContent(premier),
            Aad.AssetProperty(premier, "a"), Aad.AssetProperty(premier, "b"),
            Aad.CredentialSecret(second), Aad.BackupKey,
        };

        Assert.Equal(contexts.Length, contexts.Distinct().Count());
    }

    [Fact]
    public void Les_enveloppes_du_coffre_sont_liees_a_son_identifiant()
    {
        var premier = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.NotEqual(Aad.VaultDek(premier), Aad.VaultDek(second));
        Assert.NotEqual(Aad.VaultDek(premier), Aad.VaultRecovery(premier));
    }

    [Fact]
    public async Task L_index_aveugle_est_stable_mais_ne_revele_pas_la_valeur()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        var premier = harness.Session.BlindIndex("root");
        var second = harness.Session.BlindIndex("root");
        var autre = harness.Session.BlindIndex("admin");

        // Stable : c'est ce qui permet la recherche exacte sur un champ chiffré.
        Assert.Equal(premier, second);
        Assert.NotEqual(premier, autre);
        // Et il ne doit évidemment pas contenir la valeur elle-même.
        Assert.DoesNotContain("root", Convert.ToHexString(premier), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deux_coffres_differents_ne_produisent_pas_le_meme_index_aveugle()
    {
        await using var premier = await VaultHarness.StartUnlockedAsync();
        await using var second = await VaultHarness.StartUnlockedAsync();

        // Sinon, comparer deux bases révélerait que les mêmes comptes y figurent.
        Assert.NotEqual(premier.Session.BlindIndex("root"), second.Session.BlindIndex("root"));
    }
}
