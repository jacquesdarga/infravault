using InfraVault.Application.Interfaces;
using InfraVault.Infrastructure.Repositories;
using InfraVault.Tests.Shared;

namespace InfraVault.Infrastructure.Tests;

/// <summary>
/// L'index plein texte est ce qui rend l'application utilisable sur une grosse infrastructure.
/// Il ne doit jamais recevoir de secret, et doit retrouver une machine par un fragment
/// de hostname ou d'adresse IP — ce que ne permet pas une recherche par mot entier.
/// </summary>
public sealed class SearchIndexTests
{
    private static SearchDocument Document(
        Guid id, string title, string? subtitle = null, string? body = null, string? tags = null,
        string entityType = "Asset", string category = "Ressources")
        => new(entityType, id, category, title, subtitle, body, tags, "server", "#336699");

    [Fact]
    public async Task Un_document_indexe_est_retrouve_par_son_titre()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "SRV-WEB-01"));
        var hits = await index.QueryAsync("srv-web", 50);

        Assert.Contains(hits, h => h.EntityId == id);
    }

    [Fact]
    public async Task La_recherche_trouve_un_fragment_de_hostname()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "Serveur web", subtitle: "srv-web-01.example.com"));

        // Un administrateur tape rarement le nom complet.
        Assert.Contains(await index.QueryAsync("web-01", 50), h => h.EntityId == id);
        Assert.Contains(await index.QueryAsync("example.com", 50), h => h.EntityId == id);
    }

    [Fact]
    public async Task La_recherche_trouve_un_fragment_d_adresse_IP()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "Routeur", subtitle: "10.42.0.254"));

        Assert.Contains(await index.QueryAsync("10.42.0", 50), h => h.EntityId == id);
        Assert.Contains(await index.QueryAsync("0.254", 50), h => h.EntityId == id);
    }

    [Fact]
    public async Task La_recherche_ignore_la_casse()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "SRV-WEB-01"));

        Assert.Contains(await index.QueryAsync("srv-web-01", 50), h => h.EntityId == id);
        Assert.Contains(await index.QueryAsync("SRV-WEB-01", 50), h => h.EntityId == id);
    }

    [Fact]
    public async Task Tous_les_termes_doivent_correspondre()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var web = Guid.NewGuid();
        var base_ = Guid.NewGuid();

        await index.UpsertManyAsync([
            Document(web, "SRV-WEB-01", subtitle: "production"),
            Document(base_, "SRV-DB-01", subtitle: "production"),
        ]);

        var hits = await index.QueryAsync("production web", 50);

        Assert.Contains(hits, h => h.EntityId == web);
        Assert.DoesNotContain(hits, h => h.EntityId == base_);
    }

    [Fact]
    public async Task Reindexer_une_entite_ne_cree_pas_de_doublon()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "SRV-WEB-01"));
        await index.UpsertAsync(Document(id, "SRV-WEB-01"));
        await index.UpsertAsync(Document(id, "SRV-WEB-02"));

        Assert.Equal(1, await index.CountAsync());
        Assert.Single(await index.QueryAsync("srv-web", 50));
    }

    [Fact]
    public async Task Reindexer_remplace_vraiment_l_ancien_contenu()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "ancien-nom-machine"));
        await index.UpsertAsync(Document(id, "nouveau-nom-machine"));

        // Sans suppression de l'ancienne ligne, la machine resterait trouvable sous son ancien nom.
        Assert.Empty(await index.QueryAsync("ancien-nom", 50));
        Assert.Single(await index.QueryAsync("nouveau-nom", 50));
    }

    [Fact]
    public async Task Une_entite_retiree_n_est_plus_trouvee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();
        await index.UpsertAsync(Document(id, "SRV-WEB-01"));

        await index.RemoveAsync("Asset", id);

        Assert.Empty(await index.QueryAsync("srv-web", 50));
        Assert.Equal(0, await index.CountAsync());
    }

    [Fact]
    public async Task Clear_vide_tout_l_index()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        await index.UpsertManyAsync([
            Document(Guid.NewGuid(), "SRV-WEB-01"),
            Document(Guid.NewGuid(), "SRV-DB-01"),
        ]);

        await index.ClearAsync();

        Assert.Equal(0, await index.CountAsync());
        Assert.Empty(await index.QueryAsync("srv", 50));
    }

    [Fact]
    public async Task La_limite_de_resultats_est_respectee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        await index.UpsertManyAsync(Enumerable.Range(0, 30)
            .Select(i => Document(Guid.NewGuid(), $"SRV-WEB-{i:D2}")));

        Assert.Equal(5, (await index.QueryAsync("srv-web", 5)).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task Une_recherche_vide_ne_renvoie_rien(string query)
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        await index.UpsertAsync(Document(Guid.NewGuid(), "SRV-WEB-01"));

        Assert.Empty(await index.QueryAsync(query, 50));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("*")]
    [InlineData("((((")]
    [InlineData("NEAR/")]
    [InlineData("^$%&")]
    public async Task Une_requete_exotique_ne_fait_pas_tomber_la_recherche(string query)
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        await index.UpsertAsync(Document(Guid.NewGuid(), "SRV-WEB-01"));

        // Aucun résultat est acceptable ; une exception remonterait jusqu'à l'interface.
        var hits = await index.QueryAsync(query, 50);

        Assert.NotNull(hits);
    }

    [Fact]
    public async Task Les_metadonnees_d_affichage_sont_restituees()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "SRV-WEB-01", subtitle: "10.42.0.10"));
        var hit = Assert.Single(await index.QueryAsync("srv-web-01", 50));

        Assert.Equal("Asset", hit.EntityType);
        Assert.Equal(id, hit.EntityId);
        Assert.Equal("Ressources", hit.Category);
        Assert.Equal("SRV-WEB-01", hit.Title);
        Assert.Equal("10.42.0.10", hit.Subtitle);
        Assert.Equal("server", hit.Icon);
        Assert.Equal("#336699", hit.Color);
    }

    [Fact]
    public async Task Un_sous_titre_vide_revient_a_null_plutot_qu_a_une_chaine_vide()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();

        await index.UpsertAsync(Document(Guid.NewGuid(), "SRV-WEB-01"));
        var hit = Assert.Single(await index.QueryAsync("srv-web-01", 50));

        Assert.Null(hit.Subtitle);
    }

    [Fact]
    public async Task La_recherche_porte_aussi_sur_le_corps_et_les_tags()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();
        var id = Guid.NewGuid();

        await index.UpsertAsync(Document(id, "Procédure", body: "redémarrage du cluster proxmox", tags: "runbook, prod"));

        Assert.Contains(await index.QueryAsync("proxmox", 50), h => h.EntityId == id);
        Assert.Contains(await index.QueryAsync("runbook", 50), h => h.EntityId == id);
    }

    [Fact]
    public async Task Un_index_vide_ne_renvoie_rien_sans_echouer()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var index = harness.Get<SqliteSearchIndex>();

        Assert.Equal(0, await index.CountAsync());
        Assert.Empty(await index.QueryAsync("quoi que ce soit", 50));
    }
}
