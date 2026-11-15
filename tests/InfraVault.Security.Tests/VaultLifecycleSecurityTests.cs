using System.Text;
using InfraVault.Application.Security;
using InfraVault.Shared.Exceptions;
using InfraVault.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace InfraVault.Security.Tests;

/// <summary>
/// Les critères de sécurité du §37 vérifiés sur un vrai coffre : mauvais mot de passe refusé,
/// rien en clair dans le fichier, altération détectée, rotation et récupération fonctionnelles.
/// </summary>
public sealed class VaultLifecycleSecurityTests
{
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

    // ───────────────────────── Création et déverrouillage ─────────────────────────

    [Fact]
    public async Task La_creation_ouvre_la_session_et_rend_une_cle_de_recuperation()
    {
        await using var harness = await VaultHarness.StartAsync();

        var result = await harness.CreateVaultAsync();

        Assert.True(harness.Session.IsUnlocked);
        Assert.NotEqual(Guid.Empty, result.VaultId);
        Assert.NotEmpty(result.RecoveryKey);
    }

    [Fact]
    public async Task Un_deuxieme_coffre_ne_peut_pas_ecraser_le_premier()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await Assert.ThrowsAsync<InfraVaultException>(() => harness.CreateVaultAsync());
    }

    [Fact]
    public async Task Un_mauvais_mot_de_passe_laisse_le_coffre_ferme()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync("Zz9!pQ4wNm2#bX"));

        Assert.False(harness.Session.IsUnlocked);
    }

    [Fact]
    public async Task Le_bon_mot_de_passe_rouvre_le_coffre()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await harness.Auth.UnlockAsync(VaultHarness.MasterPassword);

        Assert.True(harness.Session.IsUnlocked);
    }

    [Fact]
    public async Task VerifyMasterPassword_ne_modifie_pas_l_etat_de_la_session()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        Assert.True(await harness.Auth.VerifyMasterPasswordAsync(VaultHarness.MasterPassword));
        Assert.False(harness.Session.IsUnlocked);

        Assert.False(await harness.Auth.VerifyMasterPasswordAsync("Zz9!pQ4wNm2#bX"));
        Assert.False(harness.Session.IsUnlocked);
    }

    // ───────────────────────── Rien en clair sur le disque ─────────────────────────

    [Fact]
    public async Task Le_master_password_n_apparait_nulle_part_dans_le_fichier()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        var bytes = await harness.ReadDatabaseBytesAsync();

        Assert.False(Contains(bytes, VaultHarness.MasterPassword),
            "Le Master Password ne doit jamais toucher le disque, sous aucune forme.");
    }

    [Fact]
    public async Task La_cle_de_recuperation_n_apparait_pas_dans_le_fichier()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        var bytes = await harness.ReadDatabaseBytesAsync();

        Assert.False(Contains(bytes, harness.RecoveryKey));
        // Même privée de ses tirets, elle ne doit pas s'y trouver.
        Assert.False(Contains(bytes, harness.RecoveryKey.Replace("-", "")));
    }

    [Fact]
    public async Task Le_coffre_ne_stocke_que_des_enveloppes()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await using var db = harness.DbFactory.Create();
        var vault = await db.Vaults.AsNoTracking().FirstAsync();

        Assert.NotEmpty(vault.Salt);
        Assert.NotEmpty(vault.EncryptedDek);
        Assert.Equal(12, vault.DekNonce.Length);
        Assert.Equal(16, vault.DekTag.Length);
        Assert.True(vault.HasRecoveryKey);
    }

    // ───────────────────────── Altération détectée ─────────────────────────

    [Fact]
    public async Task Une_enveloppe_alteree_rend_le_coffre_inouvrable()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await using (var db = harness.DbFactory.Create())
        {
            var vault = await db.Vaults.FirstAsync();
            var tampered = (byte[])vault.EncryptedDek.Clone();
            tampered[0] ^= 0xFF;
            vault.EncryptedDek = tampered;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync(VaultHarness.MasterPassword));
    }

    [Fact]
    public async Task Un_sel_altere_rend_le_coffre_inouvrable()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await using (var db = harness.DbFactory.Create())
        {
            var vault = await db.Vaults.FirstAsync();
            var tampered = (byte[])vault.Salt.Clone();
            tampered[0] ^= 0xFF;
            vault.Salt = tampered;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync(VaultHarness.MasterPassword));
    }

    [Fact]
    public async Task Un_tag_altere_rend_le_coffre_inouvrable()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await using (var db = harness.DbFactory.Create())
        {
            var vault = await db.Vaults.FirstAsync();
            var tampered = (byte[])vault.DekTag.Clone();
            tampered[0] ^= 0xFF;
            vault.DekTag = tampered;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync(VaultHarness.MasterPassword));
    }

    [Fact]
    public async Task Des_parametres_KDF_illisibles_sont_traites_comme_une_atteinte_a_l_integrite()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await using (var db = harness.DbFactory.Create())
        {
            var vault = await db.Vaults.FirstAsync();
            vault.KdfParameters = "ceci n'est pas du json";
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync(VaultHarness.MasterPassword));
    }

    // ───────────────────────── Rotation et récupération ─────────────────────────

    [Fact]
    public async Task La_rotation_du_master_password_conserve_l_acces_aux_donnees()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        const string aad = "credential:test:secret";
        var box = harness.Session.SealString("mot de passe du routeur", aad);

        await harness.Auth.ChangeMasterPasswordAsync(VaultHarness.MasterPassword, VaultHarness.NewMasterPassword);
        await harness.Auth.LockAsync();
        await harness.Auth.UnlockAsync(VaultHarness.NewMasterPassword);

        // Seule l'enveloppe change : la DEK, donc les données, restent les mêmes.
        Assert.Equal("mot de passe du routeur", harness.Session.OpenString(box, aad));
    }

    [Fact]
    public async Task L_ancien_master_password_ne_fonctionne_plus_apres_rotation()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await harness.Auth.ChangeMasterPasswordAsync(VaultHarness.MasterPassword, VaultHarness.NewMasterPassword);
        await harness.Auth.LockAsync();

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.UnlockAsync(VaultHarness.MasterPassword));
    }

    [Fact]
    public async Task La_rotation_exige_le_mot_de_passe_actuel()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await Assert.ThrowsAsync<InvalidMasterPasswordException>(
            () => harness.Auth.ChangeMasterPasswordAsync("Zz9!pQ4wNm2#bX", VaultHarness.NewMasterPassword));
    }

    [Fact]
    public async Task La_rotation_refuse_un_nouveau_mot_de_passe_faible()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Auth.ChangeMasterPasswordAsync(VaultHarness.MasterPassword, "password1234"));
    }

    [Fact]
    public async Task La_recuperation_rouvre_le_coffre_et_impose_un_nouveau_mot_de_passe()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        const string aad = "credential:test:secret";
        var box = harness.Session.SealString("mot de passe du routeur", aad);
        var recoveryKey = harness.RecoveryKey;
        await harness.Auth.LockAsync();

        var nouvelleCle = await harness.Auth.RecoverAsync(recoveryKey, VaultHarness.NewMasterPassword);

        Assert.True(harness.Session.IsUnlocked);
        Assert.NotEmpty(nouvelleCle);
        Assert.NotEqual(recoveryKey, nouvelleCle);
        Assert.Equal("mot de passe du routeur", harness.Session.OpenString(box, aad));
    }

    [Fact]
    public async Task Une_cle_de_recuperation_deja_utilisee_ne_resservira_pas()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var ancienneCle = harness.RecoveryKey;
        await harness.Auth.LockAsync();
        await harness.Auth.RecoverAsync(ancienneCle, VaultHarness.NewMasterPassword);
        await harness.Auth.LockAsync();

        await Assert.ThrowsAsync<InvalidRecoveryKeyException>(
            () => harness.Auth.RecoverAsync(ancienneCle, "Rr5!tY8uIo1#pL"));
    }

    [Fact]
    public async Task Regenerer_la_cle_de_recuperation_invalide_la_precedente()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var ancienneCle = harness.RecoveryKey;

        var nouvelleCle = await harness.Auth.RegenerateRecoveryKeyAsync();
        await harness.Auth.LockAsync();

        Assert.NotEqual(ancienneCle, nouvelleCle);
        await Assert.ThrowsAsync<InvalidRecoveryKeyException>(
            () => harness.Auth.RecoverAsync(ancienneCle, VaultHarness.NewMasterPassword));
    }

    [Fact]
    public async Task Une_cle_de_recuperation_inventee_est_refusee()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();

        await Assert.ThrowsAsync<InvalidRecoveryKeyException>(
            () => harness.Auth.RecoverAsync("AAAA-BBBB-CCCC-DDDD-EEEE-FFFF-GGGG-HHHH", VaultHarness.NewMasterPassword));
    }

    [Fact]
    public async Task La_recuperation_refuse_un_nouveau_mot_de_passe_faible()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        var recoveryKey = harness.RecoveryKey;
        await harness.Auth.LockAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Auth.RecoverAsync(recoveryKey, "password1234"));
    }

    // ───────────────────────── Verrouillage ─────────────────────────

    [Fact]
    public async Task Le_verrouillage_rend_les_secrets_inaccessibles()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        const string aad = "credential:test:secret";
        var box = harness.Session.SealString("mot de passe du routeur", aad);

        await harness.Auth.LockAsync();

        Assert.False(harness.Session.IsUnlocked);
        Assert.Throws<VaultLockedException>(() => harness.Session.OpenString(box, aad));
        Assert.Throws<VaultLockedException>(() => harness.Session.SealString("autre", aad));
        Assert.Throws<VaultLockedException>(() => harness.Session.EnsureUnlocked());
    }

    [Fact]
    public async Task Verrouiller_un_coffre_deja_verrouille_ne_leve_rien()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();

        await harness.Auth.LockAsync();
        await harness.Auth.LockAsync();

        Assert.False(harness.Session.IsUnlocked);
    }

    // ───────────────────────── Anti force brute ─────────────────────────

    [Fact]
    public async Task Le_quatrieme_essai_consecutif_est_temporise()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();
        const string mauvais = "Zz9!pQ4wNm2#bX";

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidMasterPasswordException>(() => harness.Auth.UnlockAsync(mauvais));

        var ex = await Assert.ThrowsAsync<AuthenticationThrottledException>(
            () => harness.Auth.UnlockAsync(mauvais));

        Assert.True(ex.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task La_temporisation_se_libere_avec_le_temps()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();
        const string mauvais = "Zz9!pQ4wNm2#bX";

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidMasterPasswordException>(() => harness.Auth.UnlockAsync(mauvais));
        await Assert.ThrowsAsync<AuthenticationThrottledException>(() => harness.Auth.UnlockAsync(mauvais));

        harness.Clock.Advance(TimeSpan.FromSeconds(5));

        // Le délai écoulé, l'essai est de nouveau évalué (et toujours faux).
        await Assert.ThrowsAsync<InvalidMasterPasswordException>(() => harness.Auth.UnlockAsync(mauvais));
    }

    [Fact]
    public async Task Une_ouverture_reussie_remet_le_compteur_a_zero()
    {
        await using var harness = await VaultHarness.StartUnlockedAsync();
        await harness.Auth.LockAsync();
        const string mauvais = "Zz9!pQ4wNm2#bX";

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidMasterPasswordException>(() => harness.Auth.UnlockAsync(mauvais));
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        await harness.Auth.UnlockAsync(VaultHarness.MasterPassword);
        await harness.Auth.LockAsync();

        // Le compteur ayant été effacé, le premier échec suivant n'est pas temporisé.
        await Assert.ThrowsAsync<InvalidMasterPasswordException>(() => harness.Auth.UnlockAsync(mauvais));
    }

    [Fact]
    public async Task La_politique_de_mot_de_passe_s_applique_a_la_creation()
    {
        await using var harness = await VaultHarness.StartAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.CreateVaultAsync(password: "password1234"));
        await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.CreateVaultAsync(password: "court"));
    }

    [Fact]
    public async Task Le_nom_du_coffre_est_exige()
    {
        await using var harness = await VaultHarness.StartAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => harness.CreateVaultAsync(name: "   "));
    }

    [Fact]
    public void La_politique_refuse_les_mots_de_passe_maitres_previsibles()
    {
        Assert.NotEmpty(MasterPasswordPolicy.Validate("motdepasse12"));
        Assert.Empty(MasterPasswordPolicy.Validate(VaultHarness.MasterPassword));
        Assert.Empty(MasterPasswordPolicy.Validate(VaultHarness.NewMasterPassword));
    }
}
