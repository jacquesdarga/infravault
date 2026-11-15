#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InfraVault.Application.Services;
using InfraVault.Presentation.Services;
using InfraVault.Presentation.Topology;
using InfraVault.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace InfraVault.Presentation.Diagnostics;

/// <summary>
/// Outil de développement, compilé UNIQUEMENT en configuration Debug (absent des livraisons Release) :
/// « InfraVault.exe --data-dir &lt;dossier jetable&gt; --snapshot &lt;dossier&gt; » crée un coffre de démonstration,
/// parcourt les écrans et enregistre une capture PNG de chacun, pour contrôler le rendu.
/// </summary>
internal static class SnapshotRunner
{
    private const string DemoPassword = "Demo-Capture!Phrase-2026";

    public static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        Directory.CreateDirectory(directory);
        var app = services.GetRequiredService<AppViewModel>();
        var theme = services.GetRequiredService<ThemeService>();
        var log = new List<string>();
        try
        {
            await Pause(700);
            if (app.Screen is CreateVaultViewModel create)
            {
                Capture(window, directory, "01-creation");
                create.Password = create.Confirmation = DemoPassword;
                create.CreateCommand.Execute(null);
                await Until(() => app.Dialogs.Stack.OfType<RecoveryKeyDialogViewModel>().Any(), 40);
                await Pause(500);
                Capture(window, directory, "02-cle-de-recuperation");
                var dialog = app.Dialogs.Stack.OfType<RecoveryKeyDialogViewModel>().First();
                dialog.Acknowledged = true;
                dialog.DoneCommand.Execute(null);
            }
            else if (app.Screen is UnlockViewModel unlock)
            {
                Capture(window, directory, "01-deverrouillage");
                unlock.Password = DemoPassword;
                unlock.UnlockCommand.Execute(null);
            }
            await Until(() => app.Main?.CurrentPage is DashboardViewModel { Data: not null }, 40);
            var main = app.Main ?? throw new InvalidOperationException("Écran principal non atteint.");
            await main.ReloadAsync();
            await Pause(800);
            Capture(window, directory, "03-tableau-de-bord");

            main.Assets();
            await Pause(900);
            Capture(window, directory, "04-infrastructure");

            var assets = await services.GetRequiredService<InfrastructureService>().ListAssetsAsync();
            var node = assets.FirstOrDefault(a => a.Name == "Firewall Périmètre") ?? assets.FirstOrDefault();
            if (node is not null)
            {
                main.Asset(node.Id);
                await Pause(900);
                Capture(window, directory, "05-ressource-vue-ensemble");
                if (main.CurrentPage is AssetDetailsViewModel details)
                {
                    details.SelectedTab = 1; await Pause(400); Capture(window, directory, "06-ressource-proprietes");
                    details.SelectedTab = 3; await Pause(400); Capture(window, directory, "07-ressource-relations");
                    details.SelectedTab = 5; await Pause(400); Capture(window, directory, "08-ressource-historique");
                }
                var editor = services.GetRequiredService<UiContext>().Create<AssetEditorDialogViewModel>(new AssetEditorArgs(node.Id, null, null));
                _ = app.Dialogs.ShowAsync<Guid?>(editor);
                await Pause(700);
                editor.ToggleIconPickerCommand.Execute(null);
                await Pause(400);
                Capture(window, directory, "09-editeur-ressource-icone");
                editor.Cancel();
            }

            main.Topology();
            await Pause(1200);
            Capture(window, directory, "10-cartographie");

            main.Credentials();
            await Pause(900);
            Capture(window, directory, "11-identifiants");
            if (main.CurrentPage is CredentialsViewModel credentials)
            {
                credentials.ToggleSecretCommand.Execute(null);
                await Pause(600);
                Capture(window, directory, "12-identifiant-secret-affiche");
                credentials.ToggleSecretCommand.Execute(null);
            }
            var credentialEditor = services.GetRequiredService<UiContext>().Create<CredentialEditorDialogViewModel>(new CredentialEditorArgs(null, null));
            _ = app.Dialogs.ShowAsync<Guid?>(credentialEditor);
            await Pause(500);
            credentialEditor.ToggleGeneratorCommand.Execute(null);
            credentialEditor.GenerateCommand.Execute(null);
            await Pause(400);
            Capture(window, directory, "13-editeur-identifiant-generateur");
            credentialEditor.Cancel();

            main.Documents();
            await Pause(1000);
            Capture(window, directory, "14-documentation");
            if (main.CurrentPage is DocumentsViewModel documents)
            {
                documents.EditCommand.Execute(null);
                await Pause(700);
                Capture(window, directory, "15-documentation-edition");
                documents.IsEditing = false;
            }

            main.Search("10.52.134");
            await Pause(900);
            Capture(window, directory, "16-recherche");

            main.Audit();
            await Pause(800);
            if (main.CurrentPage is AuditViewModel audit) { audit.VerifyCommand.Execute(null); await Pause(800); }
            Capture(window, directory, "17-audit");

            await services.GetRequiredService<BackupService>().CreateBackupAsync();
            main.Backups();
            await Pause(900);
            Capture(window, directory, "18-sauvegardes");

            main.Settings();
            await Pause(900);
            Capture(window, directory, "19-parametres");

            theme.Toggle();
            main.Dashboard();
            await Pause(1000);
            Capture(window, directory, "20-tableau-de-bord-clair");
            main.Topology();
            await Pause(1100);
            Capture(window, directory, "21-cartographie-clair");
            main.Credentials();
            await Pause(900);
            Capture(window, directory, "22-identifiants-clair");

            // Exports de la cartographie, tels que l'utilisateur les obtiendrait.
            var model = await services.GetRequiredService<TopologyService>().BuildAsync();
            var (png, width, height) = TopologyRenderer.RenderPng(model);
            await File.WriteAllBytesAsync(Path.Combine(directory, "export-cartographie.png"), png);
            var docx = services.GetRequiredService<IArchitectureReportWriter>().Write(model, png, width / 2, height / 2);
            await File.WriteAllBytesAsync(Path.Combine(directory, "export-dossier-architecture.docx"), docx);

            await FunctionalPassAsync(services, app, main, log);

            theme.Toggle();
            main.LockCommand.Execute(null);
            await Until(() => app.Screen is UnlockViewModel, 20);
            await Pause(600);
            Capture(window, directory, "23-coffre-verrouille");
            log.Add("OK");
        }
        catch (Exception ex)
        {
            log.Add("ERREUR : " + ex);
        }
        await File.WriteAllLinesAsync(Path.Combine(directory, "resultat.txt"), log);
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Exerce les éditeurs comme le ferait un utilisateur (ViewModels réels, sur le fil d'interface).</summary>
    private static async Task FunctionalPassAsync(IServiceProvider services, AppViewModel app, MainViewModel main, List<string> log)
    {
        var ui = services.GetRequiredService<UiContext>();
        var infrastructure = services.GetRequiredService<InfrastructureService>();
        var credentials = services.GetRequiredService<CredentialService>();

        void Check(string label, bool ok) => log.Add((ok ? "ok    " : "ÉCHEC ") + label);

        // Ressource : création par l'éditeur, avec parent et exposition Internet.
        var before = (await infrastructure.ListAssetsAsync()).Count;
        var parent = (await infrastructure.ListAssetsAsync()).First(a => a.Name == "Node 02");
        var editor = ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(null, parent.Id, "vm"));
        var editorTask = app.Dialogs.ShowAsync<Guid?>(editor);
        await Pause(600);
        editor.Name = "vm-essai";
        editor.Hostname = "vm-essai";
        editor.IpAddress = "10.52.134.77";
        editor.InternetExposed = true;
        editor.ExposedPorts = "8443, 22/tcp";
        editor.Tags = "Essai, capture";
        foreach (var f in editor.Fields.Where(f => f.Definition.Key == "os")) f.Value = "Debian";
        await editor.SaveCommand.ExecuteAsync(null);
        var createdId = await editorTask;
        Check("éditeur de ressource : création" + (editor.Error is null ? "" : " — " + editor.Error), createdId is not null);
        Check("ressource ajoutée à l'inventaire", (await infrastructure.ListAssetsAsync()).Count == before + 1);
        if (createdId is { } assetId)
        {
            var detail = await infrastructure.GetAssetAsync(assetId);
            Check("ports exposés normalisés", detail?.Summary.ExposedPorts == "8443/tcp, 22/tcp");
            Check("rattachée à son parent", detail?.Path.LastOrDefault()?.Name == "Node 02");
            Check("propriété typée enregistrée", detail?.Properties.Any(p => p.Key == "os" && p.Value == "Debian") == true);

            // Saisie invalide : l'éditeur doit rester ouvert avec un message.
            var bad = ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(assetId, null, null));
            var badTask = app.Dialogs.ShowAsync<Guid?>(bad);
            await Pause(500);
            bad.IpAddress = "999.1.2";
            await bad.SaveCommand.ExecuteAsync(null);
            Check("adresse IP invalide refusée avec message", bad.Error is not null && !badTask.IsCompleted);
            bad.Cancel();

            // Relation avec ports.
            var postgres = (await infrastructure.ListAssetsAsync()).First(a => a.Name == "PostgreSQL");
            var relation = new RelationshipDialogViewModel(ui, infrastructure, detail!.Summary)
            {
                Other = PickItems.From(postgres), Ports = "5432",
            };
            var relationTask = app.Dialogs.ShowAsync<bool>(relation);
            await relation.SaveCommand.ExecuteAsync(null);
            Check("relation créée" + (relation.Error is null ? "" : " — " + relation.Error), await relationTask);
            Check("ports de la relation enregistrés", (await infrastructure.GetRelationshipsAsync(assetId)).Any(r => r.Ports == "5432/tcp"));

            // Identifiant : création, rotation (nouvelle version), consultation.
            var credentialEditor = ui.Create<CredentialEditorDialogViewModel>(new CredentialEditorArgs(null, assetId));
            var credentialTask = app.Dialogs.ShowAsync<Guid?>(credentialEditor);
            await Pause(500);
            credentialEditor.Name = "vm-essai — root";
            credentialEditor.Username = "root";
            credentialEditor.GenerateCommand.Execute(null);
            var firstSecret = credentialEditor.Secret;
            credentialEditor.Port = "22";
            credentialEditor.SetExpiryCommand.Execute("90");
            await credentialEditor.SaveCommand.ExecuteAsync(null);
            var credentialId = await credentialTask;
            Check("éditeur d'identifiant : création" + (credentialEditor.Error is null ? "" : " — " + credentialEditor.Error), credentialId is not null);
            if (credentialId is { } cid)
            {
                Check("secret relu identique", await credentials.RevealSecretAsync(cid, SecretAccess.Internal) == firstSecret);
                var rotate = ui.Create<CredentialEditorDialogViewModel>(new CredentialEditorArgs(cid, null));
                var rotateTask = app.Dialogs.ShowAsync<Guid?>(rotate);
                await Pause(600);
                Check("édition : username rechargé", rotate.Username == "root");
                rotate.GenerateCommand.Execute(null);
                rotate.ChangeReason = "Rotation de contrôle";
                await rotate.SaveCommand.ExecuteAsync(null);
                await rotateTask;
                var after = await credentials.GetAsync(cid);
                Check("rotation : version 2 créée", after?.Summary.CurrentVersion == 2 && after.Versions.Count == 2);
                Check("identifiant lié à la ressource", after?.Assets.Any(a => a.Id == assetId) == true);

                main.Credentials(cid);
                await Pause(800);
                Check("fiche identifiant sélectionnée", (main.CurrentPage as CredentialsViewModel)?.Detail?.Summary.Id == cid);
            }

            // Recherche : IP, tag et username exact.
            var search = services.GetRequiredService<SearchService>();
            Check("recherche par IP", (await search.SearchAsync("10.52.134.77")).Total >= 1);
            Check("recherche par tag", (await search.SearchAsync("essai")).Total >= 1);
            Check("recherche par username exact", (await search.SearchAsync("root")).Groups.Any(g => g.Hits.Any(h => h.Title == "vm-essai — root")));

            // Suppression contrôlée.
            await infrastructure.DeleteAssetAsync(assetId, confirmDependencies: true);
            Check("suppression contrôlée", await infrastructure.GetAssetAsync(assetId) is null);
        }

        // Paramètres : modification et relecture.
        main.Settings();
        await Pause(700);
        if (main.CurrentPage is SettingsViewModel settings)
        {
            settings.AutoLockMinutes = 7;
            Check("paramètres : modification détectée", settings.IsDirty);
            await settings.SaveCommand.ExecuteAsync(null);
            Check("paramètres : enregistrés", ui.Vault.Current.AutoLockMinutes == 7 && !settings.IsDirty);
            settings.AutoLockMinutes = 10;
            await settings.SaveCommand.ExecuteAsync(null);
        }

        // Documentation : création puis relecture du contenu déchiffré.
        main.Documents();
        await Pause(700);
        if (main.CurrentPage is DocumentsViewModel documents)
        {
            documents.NewCommand.Execute(null);
            documents.EditTitle = "Note de contrôle";
            documents.EditContent = "# Contrôle\n\nTexte de **contrôle**.";
            await documents.SaveCommand.ExecuteAsync(null);
            await Pause(500);
            Check("documentation : création et relecture", documents.Detail?.Summary.Title == "Note de contrôle" && documents.ViewContent.Contains("contrôle"));
            documents.EditContent = "password = hunter2hunter2";
            Check("documentation : détection d'un secret collé", documents.LeakWarning);
        }
        Check("journal d'audit intègre", (await services.GetRequiredService<AuditService>().VerifyAsync()).IsValid);
    }

    private static void Capture(Window window, string directory, string name)
    {
        if (window.Content is not FrameworkElement root) return;
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static Task Pause(int milliseconds)
    {
        var tcs = new TaskCompletionSource();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); tcs.TrySetResult(); };
        timer.Start();
        return tcs.Task;
    }

    private static async Task Until(Func<bool> condition, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) await Pause(150);
        if (!condition()) throw new TimeoutException("Condition non atteinte pendant la capture.");
    }
}
#endif
