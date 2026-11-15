using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Security;
using InfraVault.Application.Services;
using InfraVault.Domain.ValueObjects;
using InfraVault.Presentation.Services;
using InfraVault.Presentation.Topology;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.ViewModels;

// ───────────────────────── Cartographie ─────────────────────────

public sealed partial class TopologyViewModel(
    UiContext ui, TopologyService topology, IArchitectureReportWriter reportWriter, ThemeService theme, SelectArgs args) : PageViewModel(ui)
{
    private Guid? _scope = args.Id;
    private TopologyRender? _render;

    public override string Title => "Cartographie";
    public override string NavKey => "topology";

    [ObservableProperty] private TopologyModel? _model;
    [ObservableProperty] private ImageSource? _diagram;
    [ObservableProperty] private double _diagramWidth;
    [ObservableProperty] private double _diagramHeight;
    [ObservableProperty] private double _zoom = 1;
    [ObservableProperty] private bool _showPorts = true;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _scopeLabel = "Tout le coffre";
    [ObservableProperty] private bool _isScoped;
    [ObservableProperty] private string _summary = "";

    public string ZoomText => $"{Zoom * 100:0} %";

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomText));
    partial void OnShowPortsChanged(bool value) => Draw();

    public override async Task LoadAsync()
    {
        theme.ThemeChanged -= OnThemeChanged;
        theme.ThemeChanged += OnThemeChanged;
        var model = await topology.BuildAsync(_scope);
        Model = model;
        IsScoped = _scope is not null;
        ScopeLabel = _scope is null ? "Tout le coffre" : model.Title;
        IsEmpty = model.NodeCount == 0;
        _fitPending = true;
        Summary = $"{model.NodeCount} ressources · {model.Flows.Count} flux · {model.Exposures.Count} exposition(s) Internet · {model.Stacks.Count} stack(s)";
        Draw();
        Fit();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Draw();

    private void Draw()
    {
        if (Model is null) return;
        _render = TopologyRenderer.Render(Model, TopologyPalette.Current(), ShowPorts);
        var image = new DrawingImage(_render.Drawing);
        image.Freeze();
        Diagram = image;
        DiagramWidth = _render.Width;
        DiagramHeight = _render.Height;
    }

    public override void OnLeave() => theme.ThemeChanged -= OnThemeChanged;

    /// <summary>Clic sur le schéma (coordonnées du dessin) : ouvre la ressource la plus profonde sous le pointeur.</summary>
    public void OpenAt(Point point)
    {
        var hit = _render?.Hits.FirstOrDefault(h => h.Bounds.Contains(point));
        if (hit is { AssetId: var id } && id != Guid.Empty) Ui.Navigator.Asset(id);
    }

    private double _viewportWidth, _viewportHeight;
    private bool _fitPending = true;

    /// <summary>La vue signale la taille de la zone visible ; le premier affichage est ajusté automatiquement.</summary>
    public void SetViewport(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        if (_fitPending) Fit();
    }

    /// <summary>Ajuste le zoom pour que le schéma tienne dans la zone visible.</summary>
    [RelayCommand]
    private void Fit()
    {
        if (_render is null || _viewportWidth < 50 || _viewportHeight < 50) return;
        Zoom = Math.Clamp(Math.Min((_viewportWidth - 12) / _render.Width, (_viewportHeight - 12) / _render.Height), 0.2, 1.0);
        _fitPending = false;
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(3, Math.Round(Zoom * 1.2, 2));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.2, Math.Round(Zoom / 1.2, 2));
    [RelayCommand] private void ZoomReset() => Zoom = 1;

    [RelayCommand]
    private async Task PickScopeAsync()
    {
        var picked = await PickItems.PickAssetAsync(Ui, "Limiter la cartographie à une ressource et à son contenu");
        if (picked is null) return;
        _scope = picked.Id;
        await RunAsync(LoadAsync);
    }

    [RelayCommand]
    private async Task ClearScopeAsync()
    {
        _scope = null;
        await RunAsync(LoadAsync);
    }

    [RelayCommand] private void OpenFlowSource(TopologyFlow flow) => Ui.Navigator.Asset(flow.SourceId);
    [RelayCommand] private void OpenExposure(TopologyExposure exposure) => Ui.Navigator.Asset(exposure.AssetId);

    private string DefaultName(string extension)
        => $"InfraVault - {(IsScoped ? ScopeLabel : "vue globale")} - {DateTime.Now:yyyy-MM-dd}.{extension}";

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        if (Model is null) return;
        var path = Ui.Files.Save("Exporter le schéma en image", "Image PNG (*.png)|*.png", DefaultName("png"));
        if (path is null) return;
        await RunAsync(async () =>
        {
            var (png, _, _) = TopologyRenderer.RenderPng(Model);
            await topology.SaveExportAsync(path, png, "PNG");
        }, "Schéma exporté en PNG.");
    }

    [RelayCommand]
    private async Task ExportDocxAsync()
    {
        if (Model is null) return;
        var path = Ui.Files.Save("Exporter le dossier d'architecture", "Document Word (*.docx)|*.docx", DefaultName("docx"));
        if (path is null) return;
        await RunAsync(async () =>
        {
            var (png, width, height) = TopologyRenderer.RenderPng(Model);
            var model = Model;
            // Le PNG est rendu à l'échelle 2 : on indique à Word sa taille logique.
            var docx = await Task.Run(() => reportWriter.Write(model, png, width / 2, height / 2));
            await topology.SaveExportAsync(path, docx, "Word");
        }, "Dossier d'architecture exporté au format Word.");
    }
}

// ───────────────────────── Audit ─────────────────────────

public sealed partial class AuditViewModel(UiContext ui, AuditService audit) : PageViewModel(ui)
{
    private const int PageSize = 150;
    private bool _loaded;

    public override string Title => "Audit";
    public override string NavKey => "audit";

    public ObservableCollection<AuditEventDto> Events { get; } = [];
    public IReadOnlyList<Option> TypeOptions { get; } =
        [new Option(null, "Tous les événements"), .. AuditLabels.All.OrderBy(l => l.Value).Select(l => new Option(l.Key, l.Value))];

    [ObservableProperty] private Option? _selectedType;
    [ObservableProperty] private bool _securityOnly;
    [ObservableProperty] private bool _failuresOnly;
    [ObservableProperty] private bool _canLoadMore;
    [ObservableProperty] private AuditVerification? _verification;
    [ObservableProperty] private string _countText = "";

    partial void OnSelectedTypeChanged(Option? value) => Refresh();
    partial void OnSecurityOnlyChanged(bool value) => Refresh();
    partial void OnFailuresOnlyChanged(bool value) => Refresh();
    private void Refresh() { if (_loaded) _ = RunAsync(() => QueryAsync(false)); }

    public override async Task LoadAsync()
    {
        _loaded = false;
        SelectedType ??= TypeOptions[0];
        _loaded = true;
        await QueryAsync(false);
        CountText = $"{await audit.CountAsync()} événements au journal";
    }

    private async Task QueryAsync(bool append)
    {
        if (!append) Events.Clear();
        var page = await audit.QueryAsync(new AuditQuery(EventType: SelectedType?.Value, SecurityOnly: SecurityOnly,
            FailuresOnly: FailuresOnly, Take: PageSize, Skip: Events.Count));
        foreach (var e in page) Events.Add(e);
        CanLoadMore = page.Count == PageSize;
    }

    [RelayCommand] private Task LoadMoreAsync() => RunAsync(() => QueryAsync(true));

    [RelayCommand]
    private async Task VerifyAsync()
    {
        await RunAsync(async () =>
        {
            Verification = await audit.VerifyAsync();
            await audit.RecordAsync(AuditEventTypes.AuditVerified, EntityTypes.Vault, success: Verification.IsValid,
                detail: Verification.IsValid ? "Chaîne intègre" : "Altération détectée");
            await QueryAsync(false);
        });
    }

    [RelayCommand] private void Open(AuditEventDto e) { if (e.EntityId is { } id) Ui.Navigator.Open(e.EntityType, id); }
}

// ───────────────────────── Sauvegardes ─────────────────────────

public sealed partial class BackupsViewModel(UiContext ui, BackupService backup, VaultService vault) : PageViewModel(ui)
{
    public override string Title => "Sauvegardes";
    public override string NavKey => "backups";

    public ObservableCollection<BackupInfo> Backups { get; } = [];
    [ObservableProperty] private string _directory = "";
    [ObservableProperty] private DateTime? _lastBackup;
    [ObservableProperty] private bool _autoEnabled;
    [ObservableProperty] private string _autoText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _busyText = "";

    public override async Task LoadAsync()
    {
        var settings = await vault.GetSettingsAsync();
        Directory = await backup.GetBackupDirectoryAsync();
        LastBackup = await vault.GetLastBackupAsync();
        AutoEnabled = settings.AutoBackupEnabled;
        AutoText = settings.AutoBackupEnabled
            ? $"Automatique toutes les {settings.AutoBackupIntervalHours} h · {settings.BackupRetention} versions conservées"
            : "Sauvegarde automatique désactivée";
        var list = await backup.ListBackupsAsync();
        Backups.Clear();
        foreach (var b in list) Backups.Add(b);
        IsEmpty = list.Count == 0;
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        BusyText = "Chiffrement de la sauvegarde…";
        await RunAsync(async () => { await backup.CreateBackupAsync(); await LoadAsync(); }, "Sauvegarde chiffrée créée.");
    }

    [RelayCommand]
    private async Task VerifyAsync(BackupInfo info)
    {
        BusyText = "Vérification de l'intégrité…";
        await RunAsync(async () =>
        {
            var result = await backup.VerifyAsync(info.Path);
            Ui.Toasts.Show(result.Message, result.IsValid ? ToastKind.Success : ToastKind.Error);
        });
    }

    [RelayCommand] private Task RestoreAsync(BackupInfo info) => RestoreFromAsync(info.Path);

    [RelayCommand]
    private async Task RestoreFileAsync()
    {
        var path = Ui.Files.Open("Choisir une sauvegarde InfraVault", "Sauvegarde InfraVault (*.ivbak)|*.ivbak", Directory);
        if (path is not null) await RestoreFromAsync(path);
    }

    private async Task RestoreFromAsync(string path)
    {
        var secret = await Ui.Dialogs.ShowAsync<RestoreSecret>(new RestoreDialogViewModel(System.IO.Path.GetFileName(path)));
        if (secret is null) return;
        if (!await Ui.Dialogs.ConfirmAsync("Remplacer le coffre actuel ?",
                "Tout le contenu actuel sera remplacé par celui de la sauvegarde, puis le coffre sera verrouillé. " +
                "L'ancien fichier est conservé à côté, par précaution.", "Restaurer", danger: true)) return;
        BusyText = "Restauration en cours…";
        await RunAsync(async () =>
        {
            await backup.RestoreAsync(path, secret.Secret, secret.IsRecoveryKey);
            Ui.Toasts.Success("Sauvegarde restaurée. Déverrouillez le coffre avec son mot de passe maître.");
        });
    }

    [RelayCommand] private void OpenFolder() { System.IO.Directory.CreateDirectory(Directory); Ui.Files.RevealInExplorer(Directory); }
    [RelayCommand] private void Reveal(BackupInfo info) => Ui.Files.RevealInExplorer(info.Path);

    [RelayCommand]
    private async Task ChangeDirectoryAsync()
    {
        var folder = Ui.Files.PickFolder("Dossier des sauvegardes", Directory);
        if (folder is null) return;
        await RunAsync(async () =>
        {
            await vault.SaveSettingsAsync(vault.Current with { BackupDirectory = folder });
            await LoadAsync();
        }, "Dossier de sauvegarde mis à jour.");
    }

    // ───────────── Exports / imports ─────────────

    [RelayCommand]
    private async Task ExportAssetsAsync()
    {
        var path = Ui.Files.Save("Exporter les ressources", "Export InfraVault (*.json)|*.json", $"InfraVault - ressources - {DateTime.Now:yyyy-MM-dd}.json");
        if (path is null) return;
        var count = 0;
        await RunAsync(async () => count = await backup.ExportAssetsAsync(path));
        if (count > 0) Ui.Toasts.Success($"{count} ressources exportées (sans secret ni propriété sensible).");
    }

    [RelayCommand]
    private async Task ImportAssetsAsync()
    {
        var path = Ui.Files.Open("Importer des ressources", "Export InfraVault (*.json)|*.json");
        if (path is null) return;
        await RunAsync(async () =>
        {
            var result = await backup.ImportAssetsAsync(path);
            Ui.Toasts.Show($"{result.Created} ressources importées, {result.Skipped} ignorées." +
                           (result.Warnings.Count > 0 ? " " + result.Warnings[0] : ""), result.Warnings.Count > 0 ? ToastKind.Warning : ToastKind.Success);
            await Ui.Vault.EnsureSearchIndexAsync();
            if (Ui.Get<AppViewModel>().Main is { } main) await main.ReloadAsync();
        });
    }

    [RelayCommand]
    private async Task ExportSecretsAsync()
    {
        var passphrase = await Ui.Dialogs.PromptAsync("Exporter les identifiants (chiffré)",
            "Phrase de passe de l'export", isPassword: true, confirmLabel: "Continuer",
            hint: "Les identifiants ne sont jamais exportés en clair. Le fichier sera chiffré (Argon2id + AES-256-GCM) avec cette phrase de passe : " +
                  "12 caractères minimum, différente de votre mot de passe maître. Sans elle, le fichier est inutilisable.");
        if (string.IsNullOrEmpty(passphrase)) return;
        var confirmation = await Ui.Dialogs.PromptAsync("Confirmer la phrase de passe", "Saisissez-la à nouveau", isPassword: true, confirmLabel: "Exporter");
        if (confirmation is null) return;
        if (confirmation != passphrase) { Ui.Toasts.Error("Les deux phrases de passe ne correspondent pas."); return; }
        var path = Ui.Files.Save("Exporter les identifiants", "Export chiffré InfraVault (*.ivsec)|*.ivsec", $"InfraVault - identifiants - {DateTime.Now:yyyy-MM-dd}.ivsec");
        if (path is null) return;
        var count = -1;
        BusyText = "Chiffrement de l'export…";
        await RunAsync(async () => count = await backup.ExportSecretsAsync(path, passphrase));
        if (count >= 0) Ui.Toasts.Success($"{count} identifiants exportés dans un fichier chiffré.");
    }

    [RelayCommand]
    private async Task ImportSecretsAsync()
    {
        var path = Ui.Files.Open("Importer des identifiants", "Export chiffré InfraVault (*.ivsec)|*.ivsec");
        if (path is null) return;
        var passphrase = await Ui.Dialogs.PromptAsync("Importer des identifiants", "Phrase de passe de l'export", isPassword: true, confirmLabel: "Importer");
        if (string.IsNullOrEmpty(passphrase)) return;
        BusyText = "Déchiffrement de l'export…";
        await RunAsync(async () =>
        {
            var result = await backup.ImportSecretsAsync(path, passphrase);
            Ui.Toasts.Show($"{result.Created} identifiants importés, {result.Skipped} ignorés (déjà présents).", ToastKind.Success);
        });
    }
}

// ───────────────────────── Paramètres ─────────────────────────

public sealed partial class ChangePasswordDialogViewModel : DialogViewModel
{
    private readonly AuthenticationService _authentication;

    public ChangePasswordDialogViewModel(AuthenticationService authentication)
    {
        _authentication = authentication;
        Title = "Changer le mot de passe maître";
    }

    [ObservableProperty] private string _current = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmation = "";
    [ObservableProperty] private PasswordStrength _strength = PasswordStrengthEstimator.Estimate("");

    public double StrengthRatio => Strength.Score / 4.0;
    public string StrengthBrushKey => Strength.Score switch { 0 or 1 => "Danger", 2 => "Warning", _ => "Success" };

    partial void OnNewPasswordChanged(string value)
    {
        Strength = PasswordStrengthEstimator.Estimate(value);
        OnPropertyChanged(nameof(StrengthRatio));
        OnPropertyChanged(nameof(StrengthBrushKey));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (NewPassword != Confirmation) { Error = "La confirmation ne correspond pas au nouveau mot de passe."; return; }
        if (await TryAsync(() => _authentication.ChangeMasterPasswordAsync(Current, NewPassword)))
        {
            Current = NewPassword = Confirmation = "";
            Close(true);
        }
    }
}

public sealed partial class FieldRowViewModel : ObservableObject
{
    public string Key { get; init; } = "";
    public bool IsLocked { get; init; }
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _group = "Général";
    [ObservableProperty] private Option _kind = KindOptions[0];
    [ObservableProperty] private bool _isSensitive;
    [ObservableProperty] private string _options = "";

    public static IReadOnlyList<Option> KindOptions { get; } =
    [
        new(nameof(FieldKind.Text), "Texte"), new(nameof(FieldKind.Multiline), "Texte long"), new(nameof(FieldKind.Number), "Nombre"),
        new(nameof(FieldKind.Choice), "Liste de choix"), new(nameof(FieldKind.Date), "Date"), new(nameof(FieldKind.Url), "Lien"),
    ];
    public IReadOnlyList<Option> Kinds => KindOptions;
}

/// <summary>Éditeur de catégorie : nom, icône, couleur et champs spécifiques.</summary>
public sealed partial class AssetTypeDialogViewModel : DialogViewModel
{
    private readonly VaultService _vault;
    private readonly AssetTypeDto? _existing;

    public AssetTypeDialogViewModel(VaultService vault, AssetTypeDto? existing)
    {
        _vault = vault;
        _existing = existing;
        Title = existing is null ? "Nouvelle catégorie" : $"Catégorie « {existing.Name} »";
        IsSystem = existing?.IsSystem ?? false;
        _name = existing?.Name ?? "";
        _description = existing?.Description ?? "";
        _icon = existing?.Icon ?? "cube";
        _color = existing?.Color ?? "slate";
        var systemKeys = existing is { IsSystem: true }
            ? SystemAssetTypes.Find(existing.Key)?.Fields.Select(f => f.Key).ToHashSet() ?? []
            : new HashSet<string>();
        foreach (var f in existing?.Fields ?? [])
        {
            Fields.Add(new FieldRowViewModel
            {
                Key = f.Key, IsLocked = systemKeys.Contains(f.Key), Label = f.Label, Group = f.Group ?? "Général",
                Kind = FieldRowViewModel.KindOptions.First(k => k.Value == f.Kind.ToString()), IsSensitive = f.IsSensitive,
                Options = string.Join(", ", f.Options ?? []),
            });
        }
    }

    public override double Width => 820;
    public bool IsSystem { get; }
    public IReadOnlyList<string> AllIcons { get; } = Controls.Icons.PickerCategories.SelectMany(c => c.Icons).Distinct().ToList();
    public IReadOnlyList<string> Colors => Controls.Icons.PickerColors;
    public ObservableCollection<FieldRowViewModel> Fields { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private string _color;

    [RelayCommand] private void SelectIcon(string icon) => Icon = icon;
    [RelayCommand] private void SelectColor(string color) => Color = color;
    [RelayCommand] private void AddField() => Fields.Add(new FieldRowViewModel());
    [RelayCommand] private void RemoveField(FieldRowViewModel row) { if (!row.IsLocked) Fields.Remove(row); }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var fields = Fields.Where(f => !string.IsNullOrWhiteSpace(f.Label)).Select(f =>
        {
            var kind = Enum.Parse<FieldKind>(f.Kind.Value!);
            var options = kind == FieldKind.Choice
                ? f.Options.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : null;
            return new FieldDefinition(f.Key, f.Label, kind, f.IsSensitive, f.Group, options is { Count: > 0 } ? options : null);
        }).ToList();
        if (await TryAsync(() => _vault.SaveAssetTypeAsync(new AssetTypeInput(_existing?.Id, Name, Description, Icon, Color, fields))))
            Close(true);
    }
}

public sealed partial class SettingsViewModel(
    UiContext ui, VaultService vault, AuthenticationService authentication, ThemeService theme, IAppPaths paths) : PageViewModel(ui)
{
    public override string Title => "Paramètres";
    public override string NavKey => "settings";

    public ThemeService Theme => theme;
    public string DataDirectory => paths.DataDirectory;
    public string Version => AppInfo.Version;

    [ObservableProperty] private VaultInfo? _info;
    [ObservableProperty] private string _kdfText = "";
    [ObservableProperty] private string _vaultName = "";

    // Verrouillage et sécurité
    [ObservableProperty] private int _autoLockMinutes;
    [ObservableProperty] private bool _lockOnMinimize;
    [ObservableProperty] private bool _lockOnSuspend;
    [ObservableProperty] private bool _lockOnSessionLock;
    [ObservableProperty] private int _clipboardClearSeconds;
    [ObservableProperty] private int _secretRevealSeconds;

    // Sauvegarde
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private int _autoBackupIntervalHours;
    [ObservableProperty] private int _backupRetention;
    [ObservableProperty] private string _backupDirectory = "";

    [ObservableProperty] private string _environments = "";
    [ObservableProperty] private IReadOnlyList<AssetTypeDto> _types = [];
    [ObservableProperty] private IReadOnlyList<TagUsage> _tags = [];
    [ObservableProperty] private bool _isDirty;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_tracking && e.PropertyName is nameof(AutoLockMinutes) or nameof(LockOnMinimize) or nameof(LockOnSuspend) or nameof(LockOnSessionLock)
                or nameof(ClipboardClearSeconds) or nameof(SecretRevealSeconds) or nameof(AutoBackupEnabled) or nameof(AutoBackupIntervalHours)
                or nameof(BackupRetention) or nameof(BackupDirectory) or nameof(Environments))
            IsDirty = true;
    }

    private bool _tracking;

    public override async Task LoadAsync()
    {
        _tracking = false;
        var s = await vault.GetSettingsAsync();
        Info = await authentication.GetVaultInfoAsync();
        VaultName = Info?.Name ?? "";
        KdfText = Info is null ? "" : $"{Info.KdfAlgorithm} · {Info.Kdf.MemoryKiB / 1024} Mio · {Info.Kdf.Iterations} passes · {Info.Kdf.Parallelism} voies — AES-256-GCM";
        AutoLockMinutes = s.AutoLockMinutes;
        LockOnMinimize = s.LockOnMinimize;
        LockOnSuspend = s.LockOnSuspend;
        LockOnSessionLock = s.LockOnSessionLock;
        ClipboardClearSeconds = s.ClipboardClearSeconds;
        SecretRevealSeconds = s.SecretRevealSeconds;
        AutoBackupEnabled = s.AutoBackupEnabled;
        AutoBackupIntervalHours = s.AutoBackupIntervalHours;
        BackupRetention = s.BackupRetention;
        BackupDirectory = s.BackupDirectory ?? "";
        Environments = string.Join(", ", s.Environments);
        Types = await vault.GetAssetTypesAsync();
        Tags = await vault.GetTagsAsync();
        IsDirty = false;
        _tracking = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            await vault.SaveSettingsAsync(vault.Current with
            {
                AutoLockMinutes = AutoLockMinutes,
                LockOnMinimize = LockOnMinimize,
                LockOnSuspend = LockOnSuspend,
                LockOnSessionLock = LockOnSessionLock,
                ClipboardClearSeconds = ClipboardClearSeconds,
                SecretRevealSeconds = SecretRevealSeconds,
                AutoBackupEnabled = AutoBackupEnabled,
                AutoBackupIntervalHours = AutoBackupIntervalHours,
                BackupRetention = BackupRetention,
                BackupDirectory = BackupDirectory,
                Environments = Environments.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            });
            await LoadAsync();
        }, "Paramètres enregistrés.");
    }

    [RelayCommand] private Task ResetAsync() => RunAsync(LoadAsync);

    [RelayCommand]
    private void PickBackupDirectory()
    {
        var folder = Ui.Files.PickFolder("Dossier des sauvegardes", BackupDirectory);
        if (folder is not null) BackupDirectory = folder;
    }

    [RelayCommand]
    private async Task RenameVaultAsync()
    {
        var name = await Ui.Dialogs.PromptAsync("Renommer le coffre", "Nom du coffre", VaultName);
        if (string.IsNullOrWhiteSpace(name) || name == VaultName) return;
        await RunAsync(async () => { await vault.RenameVaultAsync(name); await LoadAsync(); }, "Coffre renommé.");
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (await Ui.Dialogs.ShowAsync<bool>(new ChangePasswordDialogViewModel(authentication)))
        {
            Ui.Toasts.Success("Mot de passe maître modifié. Les secrets n'ont pas eu besoin d'être rechiffrés.");
            await RunAsync(LoadAsync);
        }
    }

    [RelayCommand]
    private async Task RegenerateRecoveryKeyAsync()
    {
        if (!await Ui.Dialogs.ConfirmAsync("Générer une nouvelle clé de récupération ?",
                "L'ancienne clé cessera immédiatement de fonctionner. Vous devrez conserver la nouvelle hors de ce poste.", "Générer")) return;
        string? key = null;
        if (!await RunAsync(async () => key = await authentication.RegenerateRecoveryKeyAsync()) || key is null) return;
        await Ui.Dialogs.ShowAsync<bool>(new RecoveryKeyDialogViewModel(Ui, key, VaultName) { Title = "Nouvelle clé de récupération" });
        await RunAsync(LoadAsync);
    }

    [RelayCommand]
    private async Task EditTypeAsync(AssetTypeDto? type)
    {
        if (await Ui.Dialogs.ShowAsync<bool>(new AssetTypeDialogViewModel(vault, type)))
            await RunAsync(LoadAsync, "Catégorie enregistrée.");
    }

    [RelayCommand]
    private async Task DeleteTypeAsync(AssetTypeDto type)
    {
        if (!await Ui.Dialogs.ConfirmAsync("Supprimer cette catégorie ?", $"La catégorie « {type.Name} » sera supprimée.", "Supprimer", danger: true)) return;
        await RunAsync(async () => { await vault.DeleteAssetTypeAsync(type.Id); await LoadAsync(); }, "Catégorie supprimée.");
    }

    [RelayCommand]
    private async Task RenameTagAsync(TagUsage tag)
    {
        var name = await Ui.Dialogs.PromptAsync("Renommer le tag", $"Nouveau nom pour « {tag.Tag} »", tag.Tag);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().ToLowerInvariant() == tag.Tag) return;
        await RunAsync(async () => { await vault.RenameTagAsync(tag.Tag, name); await vault.RebuildSearchIndexAsync(); await LoadAsync(); },
            "Tag renommé partout.");
    }

    [RelayCommand]
    private async Task DeleteTagAsync(TagUsage tag)
    {
        if (!await Ui.Dialogs.ConfirmAsync("Retirer ce tag ?", $"« {tag.Tag} » sera retiré de {tag.Total} élément(s). Les éléments eux-mêmes sont conservés.",
                "Retirer", danger: true)) return;
        await RunAsync(async () => { await vault.RenameTagAsync(tag.Tag, null); await vault.RebuildSearchIndexAsync(); await LoadAsync(); }, "Tag retiré.");
    }

    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        var count = 0;
        await RunAsync(async () => count = await vault.RebuildSearchIndexAsync());
        if (count > 0) Ui.Toasts.Success($"Index de recherche reconstruit ({count} entrées).");
    }

    [RelayCommand] private void ToggleTheme() => theme.Toggle();
    [RelayCommand] private void OpenDataDirectory() => Ui.Files.RevealInExplorer(paths.DataDirectory);
}
