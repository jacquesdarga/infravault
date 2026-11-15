using InfraVault.Application.Services;
using InfraVault.Shared.Constants;

namespace InfraVault.Application.Tests;

/// <summary>
/// Le journal d'audit est consultable et exportable : il ne doit jamais devenir un endroit
/// où un secret ou un contenu volumineux finit en clair.
/// </summary>
public sealed class AuditMetadataTests
{
    [Fact]
    public void Build_rend_null_quand_il_n_y_a_rien_a_journaliser()
    {
        Assert.Null(AuditMetadata.Build(null, null));
        Assert.Null(AuditMetadata.Build("", "   "));
    }

    [Fact]
    public void Build_puis_Parse_redonne_les_memes_valeurs()
    {
        var metadata = AuditMetadata.Build("SRV-WEB-01", "Déverrouillage manuel");

        var (label, detail) = AuditMetadata.Parse(metadata);

        Assert.Equal("SRV-WEB-01", label);
        Assert.Equal("Déverrouillage manuel", detail);
    }

    [Fact]
    public void Build_accepte_un_seul_des_deux_champs()
    {
        var (label, detail) = AuditMetadata.Parse(AuditMetadata.Build("SRV-WEB-01", null));

        Assert.Equal("SRV-WEB-01", label);
        Assert.Null(detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_tolere_une_absence_de_metadonnees(string? metadata)
    {
        Assert.Equal((null, null), AuditMetadata.Parse(metadata));
    }

    [Theory]
    [InlineData("{ pas du json")]
    [InlineData("[]")]
    [InlineData("12")]
    public void Parse_ne_jette_pas_sur_des_metadonnees_illisibles(string metadata)
    {
        // Une ligne d'audit abîmée ne doit pas empêcher d'afficher tout le journal.
        var (label, detail) = AuditMetadata.Parse(metadata);

        Assert.Null(label);
        Assert.Null(detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Sanitize_rend_null_pour_une_valeur_vide(string? value)
    {
        Assert.Null(AuditMetadata.Sanitize(value));
    }

    [Fact]
    public void Sanitize_remplace_les_caracteres_de_controle_par_des_espaces()
    {
        // Sans cela, un nom contenant \n casserait l'affichage ou l'export du journal.
        Assert.Equal("ligne 1 ligne 2", AuditMetadata.Sanitize("ligne 1\nligne 2"));
        Assert.Equal("a b", AuditMetadata.Sanitize("a\tb"));
        Assert.Equal("a b", AuditMetadata.Sanitize("a\u0000b"));
    }

    [Fact]
    public void Sanitize_supprime_les_espaces_de_bord()
    {
        Assert.Equal("SRV-WEB-01", AuditMetadata.Sanitize("   SRV-WEB-01   "));
    }

    [Fact]
    public void Sanitize_tronque_une_valeur_trop_longue()
    {
        var result = AuditMetadata.Sanitize(new string('a', 500));

        Assert.NotNull(result);
        Assert.Equal(Limits.AuditTextMaxLength + 1, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Sanitize_ne_tronque_pas_a_la_limite_exacte()
    {
        var exact = new string('a', Limits.AuditTextMaxLength);

        var result = AuditMetadata.Sanitize(exact);

        Assert.Equal(exact, result);
        Assert.DoesNotContain("…", result);
    }

    [Fact]
    public void Build_tronque_les_deux_champs()
    {
        var (label, detail) = AuditMetadata.Parse(
            AuditMetadata.Build(new string('a', 500), new string('b', 500)));

        Assert.Equal(Limits.AuditTextMaxLength + 1, label!.Length);
        Assert.Equal(Limits.AuditTextMaxLength + 1, detail!.Length);
    }
}
