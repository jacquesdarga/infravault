using System.Diagnostics;
using System.Text;
using InfraVault.Application.DTOs;
using InfraVault.Application.Services;
using InfraVault.Domain.Entities;
using InfraVault.Shared.Constants;
using InfraVault.Shared.Exceptions;
using InfraVault.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Integration.Tests;

/// <summary>
/// §37 — Critères d'acceptation de sécurité vérifiés sur le fichier réellement écrit sur le disque.
/// Le principe de ces tests : fabriquer un secret reconnaissable, puis chercher sa trace partout.
/// </summary>
public sealed class SecurityAcceptanceTests
{
    // Improbable par accident : si cette chaîne apparaît quelque part, elle vient bien de nous.
    private const string Secret = "SECRET-TEMOIN-9f3a7c21-NE-DOIT-PAS-FUIR";
    private const string Username = "UTILISATEUR-TEMOIN-5b1e";
    private const string Notes = "NOTE-TEMOIN-8c4d : accès via le bastion";
    private const string DocumentBody = "CONTENU-TEMOIN-2a6f du document interne";

    private static bool Contains(byte[] haystack, string needle)
    {
        var bytes = Encoding.UTF8.GetBytes(needle);
        for (var i = 0; i + bytes.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < bytes.Length && match; j++)
                if (haystack[i + j] != bytes[j]) match = false;
            if (match) return true;
        }
        return false;
    }

    /// <summary>Remplit le coffre avec des témoins dans chaque emplacement censé être chiffré.</summary>
    private static async Task<Guid> FillWithWitnessesAsync(VaultHarness harness)
    {
        var types = await harness.Get<VaultService>().GetAssetTypesAsync();
        var serverType = types.First(t => t.Key == AssetTypeKeys.Server).Id;

        var assetId = await harness.Get<InfrastructureService>().SaveAssetAsync(new AssetInput
        {
            AssetTypeId = serverType,
            Name = "SRV-WEB-01",
            Hostname = "srv-web-01.example.com",
            IpAddress = "10.42.0.10",
        });

        var credentialId = await harness.Get<CredentialService>().SaveAsync(new CredentialInput
        {
            Name = "root sur SRV-WEB-01",
            Type = CredentialTypes.Ssh,
            Username = Username,
            Secret = Secret,
            Notes = Notes,
            AssetIds = [assetId],
        });

        await harness.Get<DocumentationService>().SaveAsync(new DocumentInput
        {
            Title = "Procédure de redémarrage",
            Content = DocumentBody,
            DocumentType = "Procédure",
            AssetIds = [assetId],
        });

        return credentialId;
    }

    // ───────────────────────── Rien en clair sur le disque ─────────────────────────

    [Fact]
    public async Task Aucun_secret_en_clair_dans_la_base()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        var bytes = await harness.ReadDatabaseBytesAsync();

        Assert.False(Contains(bytes, Secret), "Le mot de passe se retrouve en clair dans le fichier SQLite.");
        Assert.False(Contains(bytes, Username), "Le nom d'utilisateur se retrouve en clair dans le fichier SQLite.");
        Assert.False(Contains(bytes, Notes), "Les notes se retrouvent en clair dans le fichier SQLite.");
        Assert.False(Contains(bytes, DocumentBody), "Le contenu du document se retrouve en clair dans le fichier SQLite.");
    }

    [Fact]
    public async Task Le_temoin_est_bien_detectable_quand_il_n_est_pas_chiffre()
    {
        // Contre-épreuve : sans elle, les tests précédents passeraient même si la recherche était cassée.
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        var bytes = await harness.ReadDatabaseBytesAsync();

        // Le nom de la ressource, lui, n'est pas un secret : il est stocké en clair et doit être trouvé.
        Assert.True(Contains(bytes, "SRV-WEB-01"));
        Assert.True(Contains(bytes, "srv-web-01.example.com"));
    }

    [Fact]
    public async Task Aucun_secret_dans_l_index_de_recherche()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        await using var connection = harness.OpenRawDatabase();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT title || ' ' || subtitle || ' ' || body || ' ' || tags FROM search_index;";
        var contenu = new StringBuilder();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                contenu.AppendLine(reader.GetString(0));

        var texte = contenu.ToString();
        Assert.DoesNotContain(Secret, texte);
        Assert.DoesNotContain(Username, texte);
        Assert.DoesNotContain(Notes, texte);
        // L'index doit malgré tout être utile : les éléments non sensibles y sont.
        Assert.Contains("SRV-WEB-01", texte);
    }

    [Fact]
    public async Task Aucun_secret_dans_le_journal_d_audit()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentialId = await FillWithWitnessesAsync(harness);
        await harness.Get<CredentialService>().RevealSecretAsync(credentialId, SecretAccess.View);

        var events = await harness.Get<AuditService>().QueryAsync(new AuditQuery(Take: 1000));

        Assert.NotEmpty(events);
        foreach (var e in events)
        {
            Assert.DoesNotContain(Secret, e.Label ?? "");
            Assert.DoesNotContain(Secret, e.Detail ?? "");
            Assert.DoesNotContain(Username, e.Label ?? "");
            Assert.DoesNotContain(Notes, e.Detail ?? "");
        }
    }

    [Fact]
    public async Task La_consultation_d_un_secret_est_journalisee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentialId = await FillWithWitnessesAsync(harness);

        await harness.Get<CredentialService>().RevealSecretAsync(credentialId, SecretAccess.View);

        var events = await harness.Get<AuditService>().QueryAsync(new AuditQuery(EntityId: credentialId));
        Assert.Contains(events, e => e.EventType == AuditEventTypes.SecretViewed);
    }

    [Fact]
    public async Task Aucun_secret_en_clair_dans_une_sauvegarde()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        var backup = await harness.Get<BackupService>().CreateBackupAsync(harness.DataDirectory);
        var bytes = await File.ReadAllBytesAsync(backup.Path);

        Assert.False(Contains(bytes, Secret), "La sauvegarde expose le secret en clair.");
        Assert.False(Contains(bytes, Username));
        Assert.False(Contains(bytes, Notes));
        Assert.False(Contains(bytes, VaultHarness.MasterPassword));
    }

    [Fact]
    public async Task Une_propriete_sensible_de_ressource_n_est_pas_stockee_en_clair()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        await using var db = harness.DbFactory.Create();
        var sensibles = await db.AssetProperties.AsNoTracking().Where(p => p.IsSensitive).ToListAsync();

        // Toute propriété marquée sensible doit vivre dans EncryptedValue, jamais en clair.
        Assert.All(sensibles, p => Assert.NotNull(p.EncryptedValue));
    }

    // ───────────────────────── Verrouillage ─────────────────────────

    [Fact]
    public async Task Le_verrouillage_rend_les_secrets_inaccessibles()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentialId = await FillWithWitnessesAsync(harness);
        var credentials = harness.Get<CredentialService>();

        await harness.Auth.LockAsync();

        await Assert.ThrowsAsync<VaultLockedException>(
            () => credentials.RevealSecretAsync(credentialId, SecretAccess.Internal));
    }

    [Fact]
    public async Task Apres_deverrouillage_les_secrets_redeviennent_lisibles()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentialId = await FillWithWitnessesAsync(harness);
        var credentials = harness.Get<CredentialService>();

        await harness.Auth.LockAsync();
        await harness.Auth.UnlockAsync(VaultHarness.MasterPassword);

        Assert.Equal(Secret, await credentials.RevealSecretAsync(credentialId, SecretAccess.Internal));
    }

    [Fact]
    public async Task Le_changement_de_master_password_conserve_l_acces_aux_secrets()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentialId = await FillWithWitnessesAsync(harness);
        var credentials = harness.Get<CredentialService>();

        await harness.Auth.ChangeMasterPasswordAsync(VaultHarness.MasterPassword, VaultHarness.NewMasterPassword);
        await harness.Auth.LockAsync();
        await harness.Auth.UnlockAsync(VaultHarness.NewMasterPassword);

        Assert.Equal(Secret, await credentials.RevealSecretAsync(credentialId, SecretAccess.Internal));
    }

    [Fact]
    public async Task La_recuperation_redonne_acces_aux_secrets()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var credentialId = await FillWithWitnessesAsync(harness);
        var credentials = harness.Get<CredentialService>();
        var recoveryKey = harness.RecoveryKey;

        await harness.Auth.LockAsync();
        await harness.Auth.RecoverAsync(recoveryKey, VaultHarness.NewMasterPassword);

        Assert.Equal(Secret, await credentials.RevealSecretAsync(credentialId, SecretAccess.Internal));
    }

    // ───────────────────────── Chaîne d'audit ─────────────────────────

    [Fact]
    public async Task La_chaine_d_audit_est_valide_sur_un_coffre_sain()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        var verification = await harness.Get<AuditService>().VerifyAsync();

        Assert.True(verification.IsValid, verification.Message);
        Assert.True(verification.EventCount > 0);
        Assert.Null(verification.BrokenAtSequence);
    }

    [Fact]
    public async Task Une_ligne_d_audit_modifiee_est_detectee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        await using (var db = harness.DbFactory.Create())
        {
            var evenement = await db.AuditEvents.OrderBy(e => e.Sequence).Skip(1).FirstAsync();
            evenement.EventType = AuditEventTypes.Export;
            await db.SaveChangesAsync();
        }

        var verification = await harness.Get<AuditService>().VerifyAsync();

        Assert.False(verification.IsValid);
        Assert.NotNull(verification.BrokenAtSequence);
    }

    [Fact]
    public async Task Une_ligne_d_audit_supprimee_est_detectee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        await using (var db = harness.DbFactory.Create())
        {
            var evenement = await db.AuditEvents.OrderBy(e => e.Sequence).Skip(1).FirstAsync();
            db.AuditEvents.Remove(evenement);
            await db.SaveChangesAsync();
        }

        var verification = await harness.Get<AuditService>().VerifyAsync();

        Assert.False(verification.IsValid);
    }

    [Fact]
    public async Task Chaque_evenement_engage_le_precedent()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await FillWithWitnessesAsync(harness);

        await using var db = harness.DbFactory.Create();
        var events = await db.AuditEvents.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync();

        Assert.True(events.Count > 1);
        for (var i = 1; i < events.Count; i++)
        {
            Assert.Equal(events[i - 1].Hash, events[i].PreviousHash);
            Assert.Equal(events[i].Hash, events[i].ComputeHash());
        }
    }

    // ───────────────────────── Performance ─────────────────────────

    [Fact]
    public async Task Recherche_sous_500_ms_sur_10000_ressources()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await SeedManyAssetsAsync(harness, 10_000);
        var search = harness.Get<SearchService>();

        // Première requête : laisse SQLite et le JIT se mettre en place.
        await search.SearchAsync("srv-00042");

        var chrono = Stopwatch.StartNew();
        var parHostname = await search.SearchAsync("srv-07531");
        var apresHostname = chrono.ElapsedMilliseconds;

        chrono.Restart();
        var parIp = await search.SearchAsync("10.99.29.91");
        var apresIp = chrono.ElapsedMilliseconds;

        Assert.NotEmpty(parHostname.Groups.SelectMany(g => g.Hits));
        Assert.NotEmpty(parIp.Groups.SelectMany(g => g.Hits));
        Assert.True(apresHostname < 500, $"Recherche par hostname : {apresHostname} ms (limite 500 ms).");
        Assert.True(apresIp < 500, $"Recherche par IP : {apresIp} ms (limite 500 ms).");
    }

    [Fact]
    public async Task L_index_suit_l_inventaire_sur_un_gros_volume()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await SeedManyAssetsAsync(harness, 10_000);

        Assert.Equal(10_000, await harness.Get<Infrastructure.Repositories.SqliteSearchIndex>().CountAsync());
    }

    /// <summary>
    /// Insertion en masse directement en base : on teste la recherche sur 10 000 ressources,
    /// pas la vitesse du chemin de création unitaire.
    /// </summary>
    private static async Task SeedManyAssetsAsync(VaultHarness harness, int count)
    {
        var types = await harness.Get<VaultService>().GetAssetTypesAsync();
        var serverType = types.First(t => t.Key == AssetTypeKeys.Server);
        var now = harness.Clock.UtcNow;

        var assets = new List<Asset>(count);
        var documents = new List<Application.Interfaces.SearchDocument>(count);
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            var hostname = $"srv-{i:D5}.example.com";
            var ip = $"10.99.{i / 256}.{i % 256}";
            assets.Add(new Asset
            {
                Id = id,
                VaultId = harness.VaultId,
                AssetTypeId = serverType.Id,
                Name = $"SRV-{i:D5}",
                Hostname = hostname,
                IpAddress = ip,
                Environment = "Production",
                Status = AssetStatuses.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });
            documents.Add(new Application.Interfaces.SearchDocument(
                EntityTypes.Asset, id, "Ressources", $"SRV-{i:D5}", $"{hostname} · {ip}", null, null,
                serverType.Icon, serverType.Color));
        }

        await using (var db = harness.DbFactory.Create())
        {
            db.Assets.AddRange(assets);
            await db.SaveChangesAsync();
        }
        await harness.Get<Infrastructure.Repositories.SqliteSearchIndex>().UpsertManyAsync(documents);
    }
}
