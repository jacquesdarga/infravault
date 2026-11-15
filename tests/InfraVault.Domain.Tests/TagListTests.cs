using InfraVault.Domain.ValueObjects;

namespace InfraVault.Domain.Tests;

/// <summary>Les tags servent au filtrage : deux écritures d'un même tag doivent devenir un seul tag.</summary>
public sealed class TagListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_rend_une_liste_vide_pour_une_saisie_vide(string? input)
    {
        Assert.Empty(TagList.Parse(input));
    }

    [Fact]
    public void Parse_met_en_minuscules_et_retire_le_diese()
    {
        Assert.Equal(["prod", "web"], TagList.Parse("#Prod, WEB"));
    }

    [Fact]
    public void Parse_accepte_la_virgule_et_le_point_virgule()
    {
        Assert.Equal(["a", "b", "c"], TagList.Parse("a, b; c"));
    }

    [Fact]
    public void Parse_supprime_les_doublons_de_casse()
    {
        Assert.Equal(["prod"], TagList.Parse("prod, PROD, #Prod"));
    }

    [Fact]
    public void Parse_ignore_les_elements_vides()
    {
        Assert.Equal(["a", "b"], TagList.Parse("a, , ;; b,"));
    }

    [Fact]
    public void Parse_ignore_un_diese_seul()
    {
        Assert.Empty(TagList.Parse("#"));
    }

    [Fact]
    public void Normalize_rend_null_quand_il_ne_reste_aucun_tag()
    {
        Assert.Null(TagList.Normalize(null));
        Assert.Null(TagList.Normalize("  ,  ;  "));
    }

    [Fact]
    public void Normalize_est_idempotent()
    {
        var once = TagList.Normalize("#Prod, WEB; prod");

        Assert.Equal("prod, web", once);
        Assert.Equal(once, TagList.Normalize(once));
    }

    [Fact]
    public void Rename_remplace_le_tag_en_conservant_la_position()
    {
        Assert.Equal("production, web", TagList.Rename("prod, web", "prod", "production"));
    }

    [Fact]
    public void Rename_normalise_le_nouveau_tag()
    {
        Assert.Equal("production, web", TagList.Rename("prod, web", "prod", "  PRODUCTION  "));
    }

    [Fact]
    public void Rename_trouve_le_tag_quelle_que_soit_la_casse_demandee()
    {
        Assert.Equal("production, web", TagList.Rename("prod, web", "PROD", "production"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_vers_un_nom_vide_supprime_le_tag(string? newTag)
    {
        Assert.Equal("web", TagList.Rename("prod, web", "prod", newTag));
    }

    [Fact]
    public void Rename_rend_null_quand_il_supprime_le_dernier_tag()
    {
        Assert.Null(TagList.Rename("prod", "prod", null));
    }

    [Fact]
    public void Rename_laisse_la_valeur_intacte_quand_le_tag_est_absent()
    {
        Assert.Equal("Prod, WEB", TagList.Rename("Prod, WEB", "staging", "preprod"));
    }

    [Fact]
    public void Rename_fusionne_quand_le_nouveau_nom_existe_deja()
    {
        // Renommer « prod » en « web » alors que « web » existe ne doit pas créer un doublon.
        Assert.Equal("web", TagList.Rename("prod, web", "prod", "web"));
    }
}
