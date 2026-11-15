using InfraVault.Domain.ValueObjects;

namespace InfraVault.Domain.Tests;

/// <summary>
/// Les ports sont saisis à la main par un administrateur pressé : la normalisation doit accepter
/// les formes courantes sans jamais perdre silencieusement ce qu'elle n'a pas compris.
/// </summary>
public sealed class PortListTests
{
    [Fact]
    public void Parse_normalise_l_exemple_de_reference()
    {
        var result = PortList.Parse("443, 80/tcp; 53/udp, 8000-8100");

        Assert.Equal(["443/tcp", "80/tcp", "53/udp", "8000-8100/tcp"], result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Parse_rend_une_liste_vide_pour_une_saisie_vide(string? input)
    {
        Assert.Empty(PortList.Parse(input));
    }

    [Fact]
    public void Parse_sous_entend_tcp_quand_le_protocole_est_absent()
    {
        Assert.Equal(["22/tcp"], PortList.Parse("22"));
    }

    [Theory]
    [InlineData("53/UDP", "53/udp")]
    [InlineData("53/Udp", "53/udp")]
    [InlineData("443/TCP", "443/tcp")]
    [InlineData("5060/SCTP", "5060/sctp")]
    public void Parse_met_le_protocole_en_minuscules(string input, string expected)
    {
        Assert.Equal([expected], PortList.Parse(input));
    }

    [Theory]
    [InlineData("80,443")]
    [InlineData("80;443")]
    [InlineData("80 443")]
    [InlineData("80\n443")]
    [InlineData("80\t443")]
    [InlineData("  80 ,  443  ")]
    public void Parse_accepte_tous_les_separateurs_courants(string input)
    {
        Assert.Equal(["80/tcp", "443/tcp"], PortList.Parse(input));
    }

    [Fact]
    public void Parse_conserve_les_plages()
    {
        Assert.Equal(["8000-8100/tcp"], PortList.Parse("8000-8100"));
        Assert.Equal(["30000-32767/udp"], PortList.Parse("30000-32767/udp"));
    }

    [Fact]
    public void Parse_supprime_les_doublons_apres_normalisation()
    {
        // « 443 » et « 443/tcp » désignent la même chose une fois normalisés.
        Assert.Equal(["443/tcp"], PortList.Parse("443, 443/tcp, 443"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("65535")]
    public void Parse_accepte_les_bornes_valides(string input)
    {
        Assert.Equal([input + "/tcp"], PortList.Parse(input));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("70000")]
    [InlineData("8000-99999")]
    public void Parse_conserve_tel_quel_un_port_hors_plage(string input)
    {
        // Hors 1-65535 : on ne normalise pas, mais on ne jette pas non plus la saisie —
        // l'utilisateur doit pouvoir voir et corriger ce qu'il a tapé.
        Assert.Equal([input], PortList.Parse(input));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("443/quic")]
    [InlineData("n/a")]
    public void Parse_conserve_tel_quel_ce_qu_il_ne_reconnait_pas(string input)
    {
        Assert.Equal([input], PortList.Parse(input));
    }

    [Fact]
    public void Parse_tronque_un_element_non_reconnu_trop_long()
    {
        var long_ = new string('x', 120);

        var result = PortList.Parse(long_);

        Assert.Equal([new string('x', 40)], result);
    }

    [Fact]
    public void Normalize_rend_null_quand_il_n_y_a_rien_a_normaliser()
    {
        Assert.Null(PortList.Normalize(null));
        Assert.Null(PortList.Normalize(""));
        Assert.Null(PortList.Normalize("  ,  ;  "));
    }

    [Fact]
    public void Normalize_joint_les_elements_par_une_virgule_et_une_espace()
    {
        Assert.Equal("80/tcp, 443/tcp", PortList.Normalize("80;443"));
    }

    [Fact]
    public void Normalize_est_idempotent()
    {
        // Normaliser deux fois ne doit rien changer : sinon chaque enregistrement modifierait la donnée.
        var once = PortList.Normalize("443, 80/tcp; 53/udp, 8000-8100");

        Assert.Equal(once, PortList.Normalize(once));
    }

    [Theory]
    [InlineData("8100-8000", "8000-8100/tcp")]
    [InlineData("32767-30000/udp", "30000-32767/udp")]
    [InlineData("65535-1", "1-65535/tcp")]
    public void Parse_remet_une_plage_inversee_dans_l_ordre(string input, string expected)
    {
        Assert.Equal([expected], PortList.Parse(input));
    }

    [Theory]
    [InlineData("80-80", "80/tcp")]
    [InlineData("80-80/udp", "80/udp")]
    public void Parse_reduit_une_plage_d_un_seul_port(string input, string expected)
    {
        // « 80-80 » et « 80 » désignent la même chose : une seule écriture doit subsister.
        Assert.Equal([expected], PortList.Parse(input));
    }

    [Theory]
    [InlineData("0080", "80/tcp")]
    [InlineData("00443/udp", "443/udp")]
    [InlineData("00001", "1/tcp")]
    [InlineData("0080-0443", "80-443/tcp")]
    public void Parse_supprime_les_zeros_de_tete(string input, string expected)
    {
        Assert.Equal([expected], PortList.Parse(input));
    }

    [Fact]
    public void Parse_conserve_tel_quel_un_nombre_trop_long_pour_etre_un_port()
    {
        // Au-delà de 5 chiffres l'expression ne reconnaît plus rien, même si la valeur est petite.
        Assert.Equal(["000080"], PortList.Parse("000080"));
    }

    [Theory]
    [InlineData("443, 0443, 00443")]
    [InlineData("8000-8100, 8100-8000")]
    [InlineData("80-80, 80, 0080")]
    public void Parse_reconnait_les_ecritures_equivalentes_comme_un_doublon(string input)
    {
        // La normalisation n'a d'intérêt que si elle fait converger les écritures d'une même chose.
        Assert.Single(PortList.Parse(input));
    }

    [Theory]
    [InlineData("8100-8000")]
    [InlineData("80-80")]
    [InlineData("0080")]
    [InlineData("443, 0443, 8100-8000, 80-80")]
    public void Normalize_reste_idempotent_sur_les_formes_corrigees(string input)
    {
        var once = PortList.Normalize(input);

        Assert.Equal(once, PortList.Normalize(once));
    }
}
