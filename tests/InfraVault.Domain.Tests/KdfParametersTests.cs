using System.Text.Json;
using InfraVault.Domain.ValueObjects;

namespace InfraVault.Domain.Tests;

/// <summary>
/// Les paramètres Argon2id sont lus depuis le fichier du coffre : un fichier altéré ne doit pas
/// pouvoir imposer un coût de dérivation absurde (déni de service) ni une clé trop courte.
/// </summary>
public sealed class KdfParametersTests
{
    [Fact]
    public void Default_respecte_les_recommandations_OWASP()
    {
        var p = KdfParameters.Default;

        Assert.Equal(64 * 1024, p.MemoryKiB);
        Assert.Equal(3, p.Iterations);
        Assert.Equal(4, p.Parallelism);
        Assert.Equal(32, p.OutputLength);
    }

    [Fact]
    public void Default_est_valide()
    {
        KdfParameters.Default.EnsureValid();
    }

    [Fact]
    public void ToJson_puis_FromJson_redonne_les_memes_parametres()
    {
        var original = new KdfParameters(128 * 1024, 4, 2);

        var restored = KdfParameters.FromJson(original.ToJson());

        Assert.Equal(original, restored);
    }

    [Fact]
    public void FromJson_valide_ce_qu_il_relit()
    {
        // Un coffre dont le fichier a été modifié pour exiger 4 Gio de RAM ne doit pas être ouvert.
        var hostile = new KdfParameters(4 * 1024 * 1024, 3, 4).ToJson();

        Assert.Throws<FormatException>(() => KdfParameters.FromJson(hostile));
    }

    [Fact]
    public void FromJson_refuse_un_document_null()
    {
        Assert.Throws<FormatException>(() => KdfParameters.FromJson("null"));
    }

    [Fact]
    public void FromJson_refuse_un_document_sans_valeurs()
    {
        // Tous les champs à zéro : hors bornes, donc refusé.
        Assert.Throws<FormatException>(() => KdfParameters.FromJson("{}"));
    }

    [Fact]
    public void FromJson_leve_une_JsonException_sur_un_document_illisible()
    {
        // Documenté ici parce que l'appelant doit attraper FormatException *et* JsonException.
        Assert.Throws<JsonException>(() => KdfParameters.FromJson("{ pas du json"));
    }

    [Theory]
    [InlineData(KdfParameters.MinMemoryKiB)]
    [InlineData(KdfParameters.MaxMemoryKiB)]
    [InlineData(64 * 1024)]
    public void EnsureValid_accepte_la_memoire_dans_les_bornes(int memoryKiB)
    {
        new KdfParameters(memoryKiB, 3, 4).EnsureValid();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(KdfParameters.MinMemoryKiB - 1)]
    [InlineData(KdfParameters.MaxMemoryKiB + 1)]
    public void EnsureValid_refuse_une_memoire_hors_bornes(int memoryKiB)
    {
        Assert.Throws<FormatException>(() => new KdfParameters(memoryKiB, 3, 4).EnsureValid());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(KdfParameters.MaxIterations)]
    public void EnsureValid_accepte_les_iterations_dans_les_bornes(int iterations)
    {
        new KdfParameters(64 * 1024, iterations, 4).EnsureValid();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(KdfParameters.MaxIterations + 1)]
    public void EnsureValid_refuse_des_iterations_hors_bornes(int iterations)
    {
        Assert.Throws<FormatException>(() => new KdfParameters(64 * 1024, iterations, 4).EnsureValid());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(KdfParameters.MaxParallelism)]
    public void EnsureValid_accepte_le_parallelisme_dans_les_bornes(int parallelism)
    {
        new KdfParameters(64 * 1024, 3, parallelism).EnsureValid();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(KdfParameters.MaxParallelism + 1)]
    public void EnsureValid_refuse_un_parallelisme_hors_bornes(int parallelism)
    {
        Assert.Throws<FormatException>(() => new KdfParameters(64 * 1024, 3, parallelism).EnsureValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(64)]
    public void EnsureValid_exige_une_cle_de_32_octets(int outputLength)
    {
        // AES-256 : toute autre longueur produirait une clé inutilisable ou affaiblie.
        Assert.Throws<FormatException>(() => new KdfParameters(64 * 1024, 3, 4, outputLength).EnsureValid());
    }
}
