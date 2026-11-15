using InfraVault.Application.DTOs;
using InfraVault.Application.Security;
using InfraVault.Shared.Exceptions;

namespace InfraVault.Application.Tests;

/// <summary>
/// Le générateur est la source des secrets du coffre : une catégorie absente du résultat
/// ou une longueur silencieusement réduite affaiblit tout ce qui est stocké ensuite.
/// </summary>
public sealed class PasswordGeneratorTests
{
    private static PasswordGeneratorOptions Options(
        int length = 24, bool lower = true, bool upper = true, bool digits = true,
        bool symbols = true, bool excludeAmbiguous = false)
        => new()
        {
            Length = length,
            Lowercase = lower,
            Uppercase = upper,
            Digits = digits,
            Symbols = symbols,
            ExcludeAmbiguous = excludeAmbiguous,
        };

    [Fact]
    public void Generate_respecte_la_longueur_demandee()
    {
        Assert.Equal(24, PasswordGenerator.Generate(Options()).Length);
        Assert.Equal(64, PasswordGenerator.Generate(Options(length: 64)).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Generate_releve_une_longueur_trop_courte_au_minimum(int length)
    {
        // Avec 4 catégories actives, le minimum réel est 4 : un mot de passe plus court
        // ne pourrait pas contenir un caractère de chaque catégorie.
        Assert.Equal(4, PasswordGenerator.Generate(Options(length: length)).Length);
    }

    [Fact]
    public void Generate_plafonne_la_longueur()
    {
        Assert.Equal(PasswordGenerator.MaxLength, PasswordGenerator.Generate(Options(length: 10_000)).Length);
    }

    [Fact]
    public void Generate_contient_au_moins_un_caractere_de_chaque_categorie_retenue()
    {
        for (var i = 0; i < 100; i++)
        {
            var password = PasswordGenerator.Generate(Options(length: 8));

            Assert.Contains(password, c => PasswordGenerator.LowercaseChars.Contains(c));
            Assert.Contains(password, c => PasswordGenerator.UppercaseChars.Contains(c));
            Assert.Contains(password, c => PasswordGenerator.DigitChars.Contains(c));
            Assert.Contains(password, c => PasswordGenerator.SymbolChars.Contains(c));
        }
    }

    [Fact]
    public void Generate_n_utilise_que_les_categories_demandees()
    {
        var password = PasswordGenerator.Generate(Options(length: 40, upper: false, digits: false, symbols: false));

        Assert.All(password, c => Assert.Contains(c, PasswordGenerator.LowercaseChars));
    }

    [Fact]
    public void Generate_exclut_les_caracteres_ambigus_quand_c_est_demande()
    {
        for (var i = 0; i < 50; i++)
        {
            var password = PasswordGenerator.Generate(Options(length: 60, excludeAmbiguous: true));

            Assert.DoesNotContain(password, c => PasswordGenerator.AmbiguousChars.Contains(c));
        }
    }

    [Fact]
    public void Generate_refuse_de_produire_un_mot_de_passe_sans_aucune_categorie()
    {
        var options = Options(lower: false, upper: false, digits: false, symbols: false);

        var ex = Assert.Throws<InfraVaultException>(() => PasswordGenerator.Generate(options));
        Assert.Contains("catégorie", ex.Message);
    }

    [Fact]
    public void Generate_ne_produit_pas_deux_fois_le_meme_mot_de_passe()
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 200; i++) produced.Add(PasswordGenerator.Generate(Options()));

        // 24 caractères tirés au hasard : une collision signalerait un générateur cassé.
        Assert.Equal(200, produced.Count);
    }

    [Fact]
    public void Generate_ne_place_pas_les_categories_toujours_au_meme_endroit()
    {
        // Sans le mélange final, les premiers caractères suivraient toujours l'ordre des catégories.
        var firstChars = new HashSet<char>();
        for (var i = 0; i < 200; i++) firstChars.Add(PasswordGenerator.Generate(Options())[0]);

        Assert.True(firstChars.Count > 1, "Le premier caractère devrait varier d'un tirage à l'autre.");
    }

    [Fact]
    public void Pools_rend_une_entree_par_categorie_active()
    {
        Assert.Equal(4, PasswordGenerator.Pools(Options()).Count);
        Assert.Single(PasswordGenerator.Pools(Options(upper: false, digits: false, symbols: false)));
        Assert.Empty(PasswordGenerator.Pools(Options(lower: false, upper: false, digits: false, symbols: false)));
    }

    [Fact]
    public void EntropyBits_suit_la_taille_de_l_alphabet_et_la_longueur()
    {
        var options = Options(length: 10, upper: false, digits: false, symbols: false);

        Assert.Equal(10 * Math.Log2(26), PasswordGenerator.EntropyBits(options), 10);
    }

    [Fact]
    public void EntropyBits_est_nulle_sans_aucune_categorie()
    {
        Assert.Equal(0, PasswordGenerator.EntropyBits(Options(lower: false, upper: false, digits: false, symbols: false)));
    }

    [Fact]
    public void EntropyBits_augmente_avec_la_longueur_et_avec_les_categories()
    {
        var court = PasswordGenerator.EntropyBits(Options(length: 12));
        var long_ = PasswordGenerator.EntropyBits(Options(length: 24));
        var pauvre = PasswordGenerator.EntropyBits(Options(length: 24, symbols: false));

        Assert.True(long_ > court);
        Assert.True(long_ > pauvre);
    }
}
