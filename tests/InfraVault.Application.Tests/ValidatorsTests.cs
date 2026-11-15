using FluentValidation;
using InfraVault.Application.DTOs;
using InfraVault.Application.Services;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Tests;

public sealed class AssetInputValidatorTests
{
    private static readonly AssetInputValidator Validator = new();

    private static AssetInput Valid() => new()
    {
        AssetTypeId = Guid.NewGuid(),
        Name = "SRV-WEB-01",
        Environment = "Production",
        Status = AssetStatuses.Active,
    };

    private static IReadOnlyList<string> Errors(AssetInput input)
        => Validator.Validate(input).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Une_ressource_minimale_est_valide()
    {
        Assert.Empty(Errors(Valid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Le_nom_est_requis(string name)
    {
        Assert.Contains(Errors(Valid() with { Name = name }), e => e.Contains("nom de la ressource"));
    }

    [Fact]
    public void Le_nom_est_borne_a_120_caracteres()
    {
        Assert.Empty(Errors(Valid() with { Name = new string('a', Limits.NameMaxLength) }));
        Assert.NotEmpty(Errors(Valid() with { Name = new string('a', Limits.NameMaxLength + 1) }));
    }

    [Fact]
    public void La_categorie_est_requise()
    {
        Assert.Contains(Errors(Valid() with { AssetTypeId = Guid.Empty }), e => e.Contains("catégorie"));
    }

    [Fact]
    public void L_environnement_est_requis()
    {
        Assert.Contains(Errors(Valid() with { Environment = "" }), e => e.Contains("environnement"));
    }

    [Fact]
    public void Le_statut_doit_etre_connu()
    {
        Assert.All(AssetStatuses.All, status => Assert.Empty(Errors(Valid() with { Status = status })));
        Assert.Contains(Errors(Valid() with { Status = "Zombie" }), e => e.Contains("Statut inconnu"));
    }

    [Fact]
    public void La_description_est_bornee()
    {
        Assert.Empty(Errors(Valid() with { Description = new string('a', Limits.DescriptionMaxLength) }));
        Assert.NotEmpty(Errors(Valid() with { Description = new string('a', Limits.DescriptionMaxLength + 1) }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("srv01")]
    [InlineData("srv-01")]
    [InlineData("srv_01")]
    [InlineData("srv-01.example.com")]
    [InlineData("a")]
    [InlineData("10-0-0-1.static.example.org")]
    public void Un_hostname_plausible_est_accepte(string? hostname)
    {
        Assert.Empty(Errors(Valid() with { Hostname = hostname }));
    }

    [Theory]
    [InlineData("-srv")]
    [InlineData("srv-")]
    [InlineData("srv..com")]
    [InlineData("srv 01")]
    [InlineData("srv@01")]
    [InlineData("srv/01")]
    public void Un_hostname_invalide_est_refuse(string hostname)
    {
        Assert.Contains(Errors(Valid() with { Hostname = hostname }), e => e.Contains("hostname"));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.254")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("10.0.0.0/8")]
    [InlineData("10.0.0.1/32")]
    [InlineData("10.0.0.1/0")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:db8::/32")]
    [InlineData("fe80::1/128")]
    public void IsIpOrCidr_accepte_une_adresse_complete(string value)
    {
        Assert.True(AssetInputValidator.IsIpOrCidr(value));
    }

    [Theory]
    [InlineData("10")]
    [InlineData("10.1")]
    [InlineData("10.0.1")]
    [InlineData("10.0.0.1/33")]
    [InlineData("2001:db8::/129")]
    [InlineData("10.0.0.1/")]
    [InlineData("10.0.0.1/abc")]
    [InlineData("10.0.0.1/-1")]
    [InlineData("999.1.1.1")]
    [InlineData("pas une ip")]
    [InlineData("")]
    public void IsIpOrCidr_refuse_une_adresse_incomplete_ou_fausse(string value)
    {
        Assert.False(AssetInputValidator.IsIpOrCidr(value));
    }

    [Fact]
    public void Une_adresse_IP_invalide_remonte_un_message_explicite()
    {
        Assert.Contains(Errors(Valid() with { IpAddress = "10.1" }), e => e.Contains("adresse IP"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void L_adresse_IP_reste_facultative(string? ip)
    {
        Assert.Empty(Errors(Valid() with { IpAddress = ip }));
    }
}

public sealed class CredentialInputValidatorTests
{
    private static readonly CredentialInputValidator Validator = new();

    private static CredentialInput Valid() => new()
    {
        Name = "root sur SRV-WEB-01",
        Type = CredentialTypes.Password,
        Secret = "Xq7!mB2vLp9#aZ",
    };

    private static IReadOnlyList<string> Errors(CredentialInput input)
        => Validator.Validate(input).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Un_identifiant_minimal_est_valide()
    {
        Assert.Empty(Errors(Valid()));
    }

    [Fact]
    public void Le_nom_est_requis_et_borne()
    {
        Assert.Contains(Errors(Valid() with { Name = "" }), e => e.Contains("nom"));
        Assert.NotEmpty(Errors(Valid() with { Name = new string('a', Limits.NameMaxLength + 1) }));
    }

    [Fact]
    public void Tous_les_types_livres_sont_acceptes()
    {
        Assert.All(CredentialTypes.All, t => Assert.Empty(Errors(Valid() with { Type = t.Key })));
    }

    [Fact]
    public void Un_type_inconnu_est_refuse()
    {
        Assert.Contains(Errors(Valid() with { Type = "quantique" }), e => e.Contains("Type d'identifiant inconnu"));
    }

    [Fact]
    public void Le_secret_est_requis_a_la_creation()
    {
        Assert.Contains(Errors(Valid() with { Id = null, Secret = null }), e => e.Contains("requis"));
        Assert.Contains(Errors(Valid() with { Id = null, Secret = "" }), e => e.Contains("requis"));
    }

    [Fact]
    public void Le_secret_reste_facultatif_en_modification()
    {
        // Secret null en modification = « ne change pas le secret », pas une erreur.
        Assert.Empty(Errors(Valid() with { Id = Guid.NewGuid(), Secret = null }));
    }

    [Fact]
    public void Le_secret_est_borne_a_64_Ko()
    {
        Assert.Empty(Errors(Valid() with { Secret = new string('a', 64 * 1024) }));
        Assert.Contains(Errors(Valid() with { Secret = new string('a', 64 * 1024 + 1) }), e => e.Contains("volumineux"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(22)]
    [InlineData(65535)]
    public void Un_port_dans_la_plage_est_accepte(int port)
    {
        Assert.Empty(Errors(Valid() with { Port = port }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Un_port_hors_plage_est_refuse(int port)
    {
        Assert.Contains(Errors(Valid() with { Port = port }), e => e.Contains("port"));
    }

    [Fact]
    public void Le_port_reste_facultatif()
    {
        Assert.Empty(Errors(Valid() with { Port = null }));
    }

    [Fact]
    public void L_url_est_bornee()
    {
        Assert.Empty(Errors(Valid() with { Url = new string('a', 2000) }));
        Assert.Contains(Errors(Valid() with { Url = new string('a', 2001) }), e => e.Contains("URL"));
    }
}

public sealed class DocumentInputValidatorTests
{
    private static readonly DocumentInputValidator Validator = new();

    private static DocumentInput Valid() => new()
    {
        Title = "Procédure de redémarrage",
        Content = "1. Prévenir l'équipe.",
        DocumentType = "Procédure",
    };

    private static IReadOnlyList<string> Errors(DocumentInput input)
        => Validator.Validate(input).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Un_document_minimal_est_valide()
    {
        Assert.Empty(Errors(Valid()));
    }

    [Fact]
    public void Le_titre_est_requis_et_borne_a_200()
    {
        Assert.Contains(Errors(Valid() with { Title = "" }), e => e.Contains("titre"));
        Assert.Empty(Errors(Valid() with { Title = new string('a', 200) }));
        Assert.NotEmpty(Errors(Valid() with { Title = new string('a', 201) }));
    }

    [Fact]
    public void Le_type_de_document_est_requis()
    {
        Assert.Contains(Errors(Valid() with { DocumentType = "" }), e => e.Contains("type de document"));
    }

    [Fact]
    public void Tous_les_types_livres_sont_acceptes()
    {
        Assert.All(DocumentTypes.All, t => Assert.Empty(Errors(Valid() with { DocumentType = t })));
    }

    [Fact]
    public void Le_contenu_est_borne_a_2_millions_de_caracteres()
    {
        Assert.Empty(Errors(Valid() with { Content = new string('a', 2_000_000) }));
        Assert.Contains(Errors(Valid() with { Content = new string('a', 2_000_001) }), e => e.Contains("volumineux"));
    }

    [Fact]
    public void Un_contenu_vide_est_accepte()
    {
        // Une fiche peut être créée avant d'être rédigée.
        Assert.Empty(Errors(Valid() with { Content = "" }));
    }
}

public sealed class ValidationExtensionsTests
{
    [Fact]
    public void EnsureValid_ne_fait_rien_sur_une_entree_valide()
    {
        var validator = new DocumentInputValidator();

        validator.EnsureValid(new DocumentInput { Title = "Note", Content = "", DocumentType = "Note" });
    }

    [Fact]
    public void EnsureValid_leve_une_exception_portant_tous_les_messages()
    {
        var validator = new DocumentInputValidator();
        var invalide = new DocumentInput { Title = "", Content = "", DocumentType = "" };

        var ex = Assert.Throws<ValidationFailedException>(() => validator.EnsureValid(invalide));

        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains(ex.Errors, e => e.Contains("titre"));
        Assert.Contains(ex.Errors, e => e.Contains("type de document"));
    }

    [Fact]
    public void EnsureValid_ne_repete_pas_deux_fois_le_meme_message()
    {
        var validator = new AssetInputValidator();
        var invalide = new AssetInput { Name = "", AssetTypeId = Guid.Empty, Environment = "", Status = "Zombie" };

        var ex = Assert.Throws<ValidationFailedException>(() => validator.EnsureValid(invalide));

        Assert.Equal(ex.Errors.Count, ex.Errors.Distinct().Count());
    }
}
