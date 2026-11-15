using System.IO.Compression;
using System.Text;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using InfraVault.Application.UseCases;
using InfraVault.Tests.Shared;

namespace InfraVault.Infrastructure.Tests;

/// <summary>
/// Écritures de fichiers et manipulation du fichier SQLite. L'écriture atomique est ce qui
/// empêche une sauvegarde à moitié écrite de passer pour une sauvegarde valide.
/// </summary>
public sealed class FileStorageTests
{
    [Fact]
    public async Task Une_ecriture_atomique_produit_le_contenu_attendu()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "essai.bin");
        var content = Encoding.UTF8.GetBytes("contenu de test");

        await storage.WriteAtomicAsync(path, content);

        Assert.Equal(content, await storage.ReadAsync(path));
    }

    [Fact]
    public async Task Une_ecriture_atomique_ne_laisse_aucun_fichier_intermediaire()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "essai.bin");

        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("contenu"));

        Assert.Empty(Directory.GetFiles(harness.DataDirectory, "*.part"));
    }

    [Fact]
    public async Task Une_ecriture_atomique_remplace_un_fichier_existant()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "essai.bin");

        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("première version"));
        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("seconde"));

        Assert.Equal("seconde", Encoding.UTF8.GetString(await storage.ReadAsync(path)));
    }

    [Fact]
    public async Task Une_ecriture_atomique_cree_le_dossier_manquant()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "sous", "dossier", "essai.bin");

        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("contenu"));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ReadPrefix_ne_lit_que_le_debut_du_fichier()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "essai.bin");
        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("0123456789"));

        var prefix = await storage.ReadPrefixAsync(path, 4);

        // C'est ainsi qu'on lit l'en-tête d'une sauvegarde sans charger tout le fichier.
        Assert.Equal("0123", Encoding.UTF8.GetString(prefix));
    }

    [Fact]
    public async Task ReadPrefix_ne_depasse_pas_la_taille_du_fichier()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "essai.bin");
        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("court"));

        Assert.Equal("court", Encoding.UTF8.GetString(await storage.ReadPrefixAsync(path, 4096)));
    }

    [Fact]
    public async Task List_filtre_par_motif_et_tolere_un_dossier_absent()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var directory = Path.Combine(harness.DataDirectory, "sauvegardes");
        await storage.WriteAtomicAsync(Path.Combine(directory, "a.ivbak"), Encoding.UTF8.GetBytes("a"));
        await storage.WriteAtomicAsync(Path.Combine(directory, "b.ivbak"), Encoding.UTF8.GetBytes("b"));
        await storage.WriteAtomicAsync(Path.Combine(directory, "notes.txt"), Encoding.UTF8.GetBytes("c"));

        Assert.Equal(2, storage.List(directory, "*.ivbak").Count);
        Assert.Empty(storage.List(Path.Combine(harness.DataDirectory, "absent"), "*.ivbak"));
    }

    [Fact]
    public async Task Supprimer_un_fichier_absent_ne_leve_rien()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();
        var path = Path.Combine(harness.DataDirectory, "essai.bin");

        await storage.WriteAtomicAsync(path, Encoding.UTF8.GetBytes("contenu"));
        storage.Delete(path);
        storage.Delete(path);

        Assert.False(storage.Exists(path));
    }

    [Fact]
    public async Task Chaque_fichier_temporaire_a_un_nom_unique()
    {
        await using var harness = await VaultHarness.StartAsync();
        var storage = harness.Get<IFileStorage>();

        var names = Enumerable.Range(0, 50).Select(_ => storage.CreateTempFile()).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(50, names.Count);
    }
}

/// <summary>Instantané, validation et remplacement du fichier de coffre — le socle de la restauration.</summary>
public sealed class DatabaseFilesTests
{
    [Fact]
    public async Task Un_instantane_est_une_base_valide_portant_le_meme_coffre()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var files = harness.Get<IDatabaseFiles>();
        var snapshot = Path.Combine(harness.DataDirectory, "instantane.db");

        await files.SnapshotAsync(snapshot);

        Assert.True(File.Exists(snapshot));
        Assert.Equal(harness.VaultId, await files.ValidateAsync(snapshot));
    }

    [Fact]
    public async Task Un_instantane_peut_ecraser_un_precedent()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var files = harness.Get<IDatabaseFiles>();
        var snapshot = Path.Combine(harness.DataDirectory, "instantane.db");

        await files.SnapshotAsync(snapshot);
        await files.SnapshotAsync(snapshot);

        Assert.Equal(harness.VaultId, await files.ValidateAsync(snapshot));
    }

    [Fact]
    public async Task Un_fichier_qui_n_est_pas_une_base_est_rejete()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var files = harness.Get<IDatabaseFiles>();
        var path = Path.Combine(harness.DataDirectory, "pas-une-base.db");
        await File.WriteAllTextAsync(path, "ceci est du texte, pas du SQLite");

        Assert.Null(await files.ValidateAsync(path));
    }

    [Fact]
    public async Task Un_fichier_absent_est_rejete_sans_exception()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var files = harness.Get<IDatabaseFiles>();

        Assert.Null(await files.ValidateAsync(Path.Combine(harness.DataDirectory, "inexistant.db")));
    }

    [Fact]
    public async Task Une_base_tronquee_est_rejetee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var files = harness.Get<IDatabaseFiles>();
        var snapshot = Path.Combine(harness.DataDirectory, "instantane.db");
        await files.SnapshotAsync(snapshot);

        var bytes = await File.ReadAllBytesAsync(snapshot);
        await File.WriteAllBytesAsync(snapshot, bytes[..(bytes.Length / 2)]);

        Assert.Null(await files.ValidateAsync(snapshot));
    }

    [Fact]
    public async Task Remplacer_la_base_met_l_ancienne_de_cote()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var files = harness.Get<IDatabaseFiles>();
        var snapshot = Path.Combine(harness.DataDirectory, "instantane.db");
        await files.SnapshotAsync(snapshot);

        var previous = await files.ReplaceDatabaseAsync(snapshot);

        // L'ancienne base est conservée : une restauration malheureuse reste réparable.
        Assert.NotNull(previous);
        Assert.True(File.Exists(previous));
        Assert.Equal(harness.VaultId, await files.ValidateAsync(harness.Paths.DatabasePath));
    }
}

/// <summary>Le rapport d'architecture est livré en .docx, c'est-à-dire une archive ZIP OpenXML.</summary>
public sealed class ArchitectureReportTests
{
    private static async Task<byte[]> BuildReportAsync(VaultHarness harness)
    {
        await harness.Get<DemoDataSeeder>().SeedAsync();
        var model = await harness.Get<TopologyService>().BuildAsync();
        return harness.Get<IArchitectureReportWriter>().Write(model, null, 0, 0);
    }

    [Fact]
    public async Task Le_rapport_est_une_archive_OpenXML_lisible()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        var docx = await BuildReportAsync(harness);

        Assert.NotEmpty(docx);
        // Signature d'une archive ZIP.
        Assert.Equal((byte)'P', docx[0]);
        Assert.Equal((byte)'K', docx[1]);

        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("word/document.xml"));
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
    }

    [Fact]
    public async Task Le_rapport_nomme_les_ressources_de_l_inventaire()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        var docx = await BuildReportAsync(harness);

        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        await using var stream = archive.GetEntry("word/document.xml")!.Open();
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();

        Assert.Contains("Datacenter Principal", xml);
    }

    [Fact]
    public async Task La_cartographie_couvre_le_jeu_de_donnees_d_exemple()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Get<DemoDataSeeder>().SeedAsync();

        var model = await harness.Get<TopologyService>().BuildAsync();

        Assert.NotEmpty(model.Roots);
        Assert.True(model.NodeCount > 0, "La cartographie doit contenir des ressources.");
        Assert.Equal(harness.Session.VaultName, model.VaultName);
    }
}
