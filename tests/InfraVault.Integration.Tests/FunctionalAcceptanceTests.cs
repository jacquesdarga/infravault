using InfraVault.Application.DTOs;
using InfraVault.Application.Services;
using InfraVault.Shared.Constants;
using InfraVault.Tests.Shared;

namespace InfraVault.Integration.Tests;

/// <summary>
/// §38 — Critères d'acceptation fonctionnels, exercés de bout en bout sur un vrai coffre :
/// chaque test correspond à une ligne du cahier des charges.
/// </summary>
public sealed class FunctionalAcceptanceTests
{
    private static async Task<Guid> TypeIdAsync(VaultHarness harness, string key)
    {
        var types = await harness.Get<VaultService>().GetAssetTypesAsync();
        return types.First(t => t.Key == key).Id;
    }

    private static async Task<Guid> CreateAssetAsync(
        VaultHarness harness, string name, string typeKey = AssetTypeKeys.Server,
        string? hostname = null, string? ip = null, Guid? parent = null, string? parentRelation = null)
        => await harness.Get<InfrastructureService>().SaveAssetAsync(new AssetInput
        {
            AssetTypeId = await TypeIdAsync(harness, typeKey),
            Name = name,
            Hostname = hostname,
            IpAddress = ip,
            ParentId = parent,
            ParentRelationType = parentRelation,
        });

    // ───────────────────────── Ressources ─────────────────────────

    [Fact]
    public async Task Creation_d_une_ressource()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        var id = await CreateAssetAsync(harness, "SRV-WEB-01", hostname: "srv-web-01.example.com", ip: "10.42.0.10");

        var asset = await harness.Get<InfrastructureService>().GetAssetAsync(id);
        Assert.NotNull(asset);
        Assert.Equal("SRV-WEB-01", asset.Summary.Name);
        Assert.Equal("srv-web-01.example.com", asset.Summary.Hostname);
        Assert.Equal("10.42.0.10", asset.Summary.IpAddress);
    }

    [Fact]
    public async Task Modification_d_une_ressource()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var service = harness.Get<InfrastructureService>();
        var id = await CreateAssetAsync(harness, "SRV-WEB-01", ip: "10.42.0.10");

        await service.SaveAssetAsync(new AssetInput
        {
            Id = id,
            AssetTypeId = await TypeIdAsync(harness, AssetTypeKeys.Server),
            Name = "SRV-WEB-01 (rénové)",
            IpAddress = "10.42.0.11",
            Status = AssetStatuses.Maintenance,
        });

        var asset = await service.GetAssetAsync(id);
        Assert.NotNull(asset);
        Assert.Equal("SRV-WEB-01 (rénové)", asset.Summary.Name);
        Assert.Equal("10.42.0.11", asset.Summary.IpAddress);
        Assert.Equal(AssetStatuses.Maintenance, asset.Summary.Status);
    }

    [Fact]
    public async Task Les_entrees_invalides_sont_refusees_avant_enregistrement()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await Assert.ThrowsAsync<Shared.Exceptions.ValidationFailedException>(
            () => CreateAssetAsync(harness, "", ip: "10.42.0.10"));
        await Assert.ThrowsAsync<Shared.Exceptions.ValidationFailedException>(
            () => CreateAssetAsync(harness, "SRV-01", ip: "10.1"));
    }

    // ───────────────────────── Identifiants ─────────────────────────

    [Fact]
    public async Task Association_d_un_identifiant_a_une_ressource()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var assetId = await CreateAssetAsync(harness, "SRV-WEB-01");
        var credentials = harness.Get<CredentialService>();

        var credentialId = await credentials.SaveAsync(new CredentialInput
        {
            Name = "root sur SRV-WEB-01",
            Type = CredentialTypes.Ssh,
            Username = "root",
            Secret = "Xq7!mB2vLp9#aZ",
            AssetIds = [assetId],
        });

        var detail = await credentials.GetAsync(credentialId);
        Assert.NotNull(detail);
        Assert.Contains(detail.Assets, a => a.Id == assetId);
    }

    [Fact]
    public async Task Association_de_plusieurs_ressources_a_un_identifiant()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var premier = await CreateAssetAsync(harness, "SRV-WEB-01");
        var second = await CreateAssetAsync(harness, "SRV-WEB-02");
        var troisieme = await CreateAssetAsync(harness, "SRV-WEB-03");
        var credentials = harness.Get<CredentialService>();

        var credentialId = await credentials.SaveAsync(new CredentialInput
        {
            Name = "Compte de service partagé",
            Username = "svc-deploy",
            Secret = "Xq7!mB2vLp9#aZ",
            AssetIds = [premier, second],
        });
        await credentials.LinkAssetAsync(credentialId, troisieme);

        var detail = await credentials.GetAsync(credentialId);
        Assert.NotNull(detail);
        Assert.Equal(3, detail.Assets.Count);
    }

    [Fact]
    public async Task Un_identifiant_peut_etre_detache_d_une_ressource()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var assetId = await CreateAssetAsync(harness, "SRV-WEB-01");
        var credentials = harness.Get<CredentialService>();
        var credentialId = await credentials.SaveAsync(new CredentialInput
        {
            Name = "root", Username = "root", Secret = "Xq7!mB2vLp9#aZ", AssetIds = [assetId],
        });

        await credentials.UnlinkAssetAsync(credentialId, assetId);

        var detail = await credentials.GetAsync(credentialId);
        Assert.NotNull(detail);
        Assert.Empty(detail.Assets);
    }

    [Fact]
    public async Task Le_secret_est_restitue_tel_qu_il_a_ete_saisi()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentials = harness.Get<CredentialService>();
        const string secret = "Xq7!mB2vLp9#aZ";
        var id = await credentials.SaveAsync(new CredentialInput { Name = "root", Secret = secret });

        Assert.Equal(secret, await credentials.RevealSecretAsync(id, SecretAccess.Internal));
    }

    [Fact]
    public async Task Modifier_un_secret_cree_une_nouvelle_version_consultable()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentials = harness.Get<CredentialService>();
        var id = await credentials.SaveAsync(new CredentialInput { Name = "root", Secret = "Ancien!Secret1" });

        await credentials.SaveAsync(new CredentialInput
        {
            Id = id, Name = "root", Secret = "Nouveau!Secret1", ChangeReason = "Rotation trimestrielle",
        });

        var detail = await credentials.GetAsync(id);
        Assert.NotNull(detail);
        Assert.True(detail.Versions.Count >= 2);
        Assert.Equal("Nouveau!Secret1", await credentials.RevealSecretAsync(id, SecretAccess.Internal));
    }

    // ───────────────────────── Arborescence ─────────────────────────

    [Fact]
    public async Task Navigation_dans_l_arborescence()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var dc = await CreateAssetAsync(harness, "Datacenter Principal", AssetTypeKeys.Datacenter);
        var hote = await CreateAssetAsync(harness, "Hyperviseur 01", AssetTypeKeys.Hypervisor,
            parent: dc, parentRelation: RelationshipTypes.Contains);
        var vm = await CreateAssetAsync(harness, "VM-APP-01", AssetTypeKeys.VirtualMachine,
            parent: hote, parentRelation: RelationshipTypes.Runs);

        var tree = await harness.Get<InfrastructureService>().GetTreeAsync();

        var racine = Assert.Single(tree, n => n.AssetId == dc);
        var enfant = Assert.Single(racine.Children, n => n.AssetId == hote);
        Assert.Single(enfant.Children, n => n.AssetId == vm);
    }

    [Fact]
    public async Task Les_relations_sont_consultables_dans_les_deux_sens()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var service = harness.Get<InfrastructureService>();
        var source = await CreateAssetAsync(harness, "APP-01", AssetTypeKeys.Application);
        var cible = await CreateAssetAsync(harness, "DB-01", AssetTypeKeys.Database);
        await service.AddRelationshipAsync(source, cible, RelationshipTypes.DependsOn, ports: "5432/tcp");

        var depuisSource = await service.GetRelationshipsAsync(source);
        var depuisCible = await service.GetRelationshipsAsync(cible);

        Assert.Contains(depuisSource, r => r.Other.Id == cible && r.IsOutgoing);
        Assert.Contains(depuisCible, r => r.Other.Id == source && !r.IsOutgoing);
        Assert.Contains(depuisSource, r => r.Ports == "5432/tcp");
    }

    // ───────────────────────── Recherche ─────────────────────────

    [Fact]
    public async Task Recherche_par_adresse_IP()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var id = await CreateAssetAsync(harness, "SRV-WEB-01", hostname: "srv-web-01.example.com", ip: "10.42.0.10");
        await CreateAssetAsync(harness, "SRV-DB-01", hostname: "srv-db-01.example.com", ip: "10.42.1.20");

        var results = await harness.Get<SearchService>().SearchAsync("10.42.0.10");

        Assert.Contains(results.Groups.SelectMany(g => g.Hits), h => h.EntityId == id);
    }

    [Fact]
    public async Task Recherche_par_fragment_d_adresse_IP()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var id = await CreateAssetAsync(harness, "SRV-WEB-01", ip: "10.42.0.10");

        // On cherche souvent un sous-réseau, pas une adresse exacte.
        var results = await harness.Get<SearchService>().SearchAsync("10.42.0");

        Assert.Contains(results.Groups.SelectMany(g => g.Hits), h => h.EntityId == id);
    }

    [Fact]
    public async Task Recherche_par_hostname()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var id = await CreateAssetAsync(harness, "SRV-WEB-01", hostname: "srv-web-01.example.com", ip: "10.42.0.10");

        var results = await harness.Get<SearchService>().SearchAsync("srv-web-01");

        Assert.Contains(results.Groups.SelectMany(g => g.Hits), h => h.EntityId == id);
    }

    [Fact]
    public async Task Une_ressource_renommee_n_est_plus_trouvee_sous_son_ancien_nom()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var service = harness.Get<InfrastructureService>();
        var search = harness.Get<SearchService>();
        var id = await CreateAssetAsync(harness, "ANCIEN-NOM-01");

        await service.SaveAssetAsync(new AssetInput
        {
            Id = id,
            AssetTypeId = await TypeIdAsync(harness, AssetTypeKeys.Server),
            Name = "NOUVEAU-NOM-01",
        });

        var ancien = await search.SearchAsync("ancien-nom");
        var nouveau = await search.SearchAsync("nouveau-nom");

        Assert.DoesNotContain(ancien.Groups.SelectMany(g => g.Hits), h => h.EntityId == id);
        Assert.Contains(nouveau.Groups.SelectMany(g => g.Hits), h => h.EntityId == id);
    }

    [Fact]
    public async Task Une_ressource_supprimee_disparait_de_la_recherche()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var id = await CreateAssetAsync(harness, "SRV-TEMPORAIRE-01");

        await harness.Get<InfrastructureService>().DeleteAssetAsync(id, confirmDependencies: true);

        var results = await harness.Get<SearchService>().SearchAsync("srv-temporaire");
        Assert.DoesNotContain(results.Groups.SelectMany(g => g.Hits), h => h.EntityId == id);
    }

    // ───────────────────────── Historique ─────────────────────────

    [Fact]
    public async Task Consultation_de_l_historique()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var assetId = await CreateAssetAsync(harness, "SRV-WEB-01");

        var events = await harness.Get<AuditService>().QueryAsync(new AuditQuery(EntityId: assetId));

        Assert.Contains(events, e => e.EventType == AuditEventTypes.AssetCreated);
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.EventLabel)));
    }

    [Fact]
    public async Task L_historique_retient_la_creation_du_coffre_et_le_verrouillage()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();
        await harness.Auth.UnlockAsync(VaultHarness.MasterPassword);

        var events = await harness.Get<AuditService>().QueryAsync(new AuditQuery(SecurityOnly: true));

        Assert.Contains(events, e => e.EventType == AuditEventTypes.VaultCreated);
        Assert.Contains(events, e => e.EventType == AuditEventTypes.Lock);
        Assert.Contains(events, e => e.EventType == AuditEventTypes.Unlock);
    }

    [Fact]
    public async Task L_historique_retient_les_echecs_d_authentification()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();
        await Assert.ThrowsAsync<Shared.Exceptions.InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync("Zz9!pQ4wNm2#bX"));
        await harness.Auth.UnlockAsync(VaultHarness.MasterPassword);

        var echecs = await harness.Get<AuditService>().QueryAsync(new AuditQuery(FailuresOnly: true));

        Assert.Contains(echecs, e => e.EventType == AuditEventTypes.AuthenticationFailed);
        Assert.All(echecs, e => Assert.False(e.Success));
    }

    // ───────────────────────── Sauvegarde ─────────────────────────

    [Fact]
    public async Task Export_et_restauration_d_une_sauvegarde()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentials = harness.Get<CredentialService>();
        const string secret = "Xq7!mB2vLp9#aZ";
        await CreateAssetAsync(harness, "SRV-WEB-01", ip: "10.42.0.10");
        var credentialId = await credentials.SaveAsync(new CredentialInput { Name = "root", Secret = secret });

        var backup = await harness.Get<BackupService>().CreateBackupAsync(harness.DataDirectory);
        Assert.True(File.Exists(backup.Path));
        Assert.Equal(harness.VaultId, backup.VaultId);

        // La fiche disparaît, puis la sauvegarde la ramène.
        await credentials.DeleteAsync(credentialId);
        Assert.Null(await credentials.GetAsync(credentialId));

        await harness.Get<BackupService>().RestoreAsync(backup.Path, VaultHarness.MasterPassword);
        await harness.Auth.LockAsync();
        await harness.Auth.UnlockAsync(VaultHarness.MasterPassword);

        Assert.NotNull(await credentials.GetAsync(credentialId));
        Assert.Equal(secret, await credentials.RevealSecretAsync(credentialId, SecretAccess.Internal));
    }

    [Fact]
    public async Task Une_sauvegarde_est_verifiable_avant_restauration()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await CreateAssetAsync(harness, "SRV-WEB-01");
        var backups = harness.Get<BackupService>();

        var backup = await backups.CreateBackupAsync(harness.DataDirectory);
        var verification = await backups.VerifyAsync(backup.Path);

        Assert.True(verification.IsValid, verification.Message);
    }

    [Fact]
    public async Task Une_sauvegarde_apparait_dans_la_liste()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var backups = harness.Get<BackupService>();
        var backup = await backups.CreateBackupAsync(harness.DataDirectory);

        var liste = await backups.ListBackupsAsync(harness.DataDirectory);

        Assert.Contains(liste, b => b.Path == backup.Path);
    }

    [Fact]
    public async Task Une_sauvegarde_ne_se_restaure_pas_avec_un_mauvais_mot_de_passe()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await CreateAssetAsync(harness, "SRV-WEB-01");
        var backups = harness.Get<BackupService>();
        var backup = await backups.CreateBackupAsync(harness.DataDirectory);

        await Assert.ThrowsAnyAsync<Shared.Exceptions.InfraVaultException>(
            () => backups.RestoreAsync(backup.Path, "Zz9!pQ4wNm2#bX"));
    }
}
