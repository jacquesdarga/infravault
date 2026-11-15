using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.DTOs;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using InfraVault.Presentation.Services;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.ViewModels;

public sealed partial class NavEntry(string key, string label, string icon) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Icon { get; } = icon;
    [ObservableProperty] private bool _isActive;
}

public sealed partial class TreeNodeViewModel : ObservableObject
{
    public TreeNodeViewModel(TreeNodeDto node, int depth, ISet<Guid> expanded)
    {
        Node = node;
        _isExpanded = depth < 2 || expanded.Contains(node.AssetId);
        Children = node.Children.Select(c => new TreeNodeViewModel(c, depth + 1, expanded)).ToList();
    }

    public TreeNodeDto Node { get; }
    public IReadOnlyList<TreeNodeViewModel> Children { get; }
    [ObservableProperty] private bool _isExpanded;

    public IEnumerable<TreeNodeViewModel> SelfAndDescendants() => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);
}

/// <summary>Coque de l'application : barre supérieure, navigation, explorateur d'infrastructure, barre d'état.</summary>
public sealed partial class MainViewModel : ViewModelBase, INavigator, IDisposable
{
    private readonly IVaultSession _session;
    private readonly InfrastructureService _infrastructure;
    private readonly CredentialService _credentials;
    private readonly DocumentationService _documentation;
    private readonly BackupService _backup;
    private readonly AuditService _audit;
    private readonly SessionGuard _guard;
    private readonly Stack<PageViewModel> _history = new();
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private bool _disposed;

    public MainViewModel(UiContext ui, IVaultSession session, InfrastructureService infrastructure, CredentialService credentials,
        DocumentationService documentation, BackupService backup, AuditService audit, SessionGuard guard, ThemeService theme) : base(ui)
    {
        _session = session;
        _infrastructure = infrastructure;
        _credentials = credentials;
        _documentation = documentation;
        _backup = backup;
        _audit = audit;
        _guard = guard;
        Theme = theme;
        ui.Navigator = this;
        VaultName = session.VaultName;
        UserName = session.UserName;

        _infrastructure.Changed += OnInfrastructureChanged;
        _backup.BackupCompleted += OnBackupCompleted;
        _guard.Tick += OnGuardTick;
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            if (CurrentPage is SearchViewModel search) search.Query = SearchText;
            else if (SearchText.Trim().Length >= 2) Search(SearchText);
        };
    }

    public ThemeService Theme { get; }

    public IReadOnlyList<NavEntry> NavEntries { get; } =
    [
        new("dashboard", "Tableau de bord", "dashboard"),
        new("assets", "Infrastructure", "server"),
        new("topology", "Cartographie", "map"),
        new("credentials", "Identifiants", "key"),
        new("documents", "Documentation", "file"),
        new("audit", "Audit", "history"),
        new("backups", "Sauvegardes", "archive"),
        new("settings", "Paramètres", "settings"),
    ];

    public ObservableCollection<TreeNodeViewModel> Tree { get; } = [];

    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private string _navKey = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _vaultName;
    [ObservableProperty] private string _userName;
    [ObservableProperty] private string _lockCountdown = "";
    [ObservableProperty] private string _backupStatus = "";
    [ObservableProperty] private bool _backupOverdue;
    [ObservableProperty] private string _inventoryStatus = "";
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _treeIsEmpty;

    partial void OnNavKeyChanged(string value)
    {
        foreach (var entry in NavEntries) entry.IsActive = entry.Key == value;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    public async Task StartAsync()
    {
        await RunAsync(async () =>
        {
            await Ui.Vault.GetSettingsAsync();
            await Ui.Vault.EnsureSearchIndexAsync();
        });
        Dashboard();
        await RefreshShellAsync();
    }

    /// <summary>Recharge la page courante, l'arborescence et la barre d'état.</summary>
    public async Task ReloadAsync()
    {
        if (CurrentPage is { } page) await RunAsync(page.LoadAsync);
        await RefreshShellAsync();
    }

    private async Task RefreshShellAsync()
    {
        await RunAsync(async () =>
        {
            var expanded = Tree.SelectMany(t => t.SelfAndDescendants()).Where(n => n.IsExpanded).Select(n => n.Node.AssetId).ToHashSet();
            var roots = await _infrastructure.GetTreeAsync();
            Tree.Clear();
            foreach (var root in roots) Tree.Add(new TreeNodeViewModel(root, 0, expanded));
            TreeIsEmpty = Tree.Count == 0;
            var count = Tree.SelectMany(t => t.SelfAndDescendants()).Select(n => n.Node.AssetId).Distinct().Count();
            InventoryStatus = count switch { 0 => "Inventaire vide", 1 => "1 ressource", _ => $"{count} ressources" };
            await RefreshBackupStatusAsync();
        });
    }

    private async Task RefreshBackupStatusAsync()
    {
        var last = await Ui.Vault.GetLastBackupAsync();
        var settings = Ui.Vault.Current;
        BackupOverdue = settings.AutoBackupEnabled && (last is null || DateTime.UtcNow - last > TimeSpan.FromHours(settings.AutoBackupIntervalHours * 2));
        BackupStatus = last is null
            ? "Aucune sauvegarde"
            : "Dernière sauvegarde " + Controls.DateConverter.Relative(last.Value.ToLocalTime());
    }

    private void OnInfrastructureChanged(object? sender, EventArgs e)
        => System.Windows.Application.Current.Dispatcher.BeginInvoke(async () => { if (!_disposed) await RefreshShellAsync(); });

    private void OnBackupCompleted(object? sender, EventArgs e)
        => System.Windows.Application.Current.Dispatcher.BeginInvoke(async () => { if (!_disposed && _session.IsUnlocked) await RunAsync(RefreshBackupStatusAsync); });

    private void OnGuardTick(object? sender, EventArgs e)
    {
        LockCountdown = _guard.Remaining is { } r
            ? $"Verrouillage auto dans {(int)r.TotalMinutes}:{r.Seconds:00}"
            : "Verrouillage auto désactivé";
    }

    // ───────────────────────── Navigation ─────────────────────────

    private async void Show(PageViewModel page, bool remember = true)
    {
        if (_disposed) return;
        CurrentPage?.OnLeave();
        if (remember && CurrentPage is not null)
        {
            _history.Push(CurrentPage);
            while (_history.Count > 40) TrimHistory();
        }
        CurrentPage = page;
        NavKey = page.NavKey;
        CanGoBack = _history.Count > 0;
        await RunAsync(page.LoadAsync);
    }

    private void TrimHistory()
    {
        var kept = _history.Reverse().Skip(1).ToList();
        _history.Clear();
        foreach (var p in kept) _history.Push(p);
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        switch (key)
        {
            case "dashboard": Dashboard(); break;
            case "assets": Assets(); break;
            case "topology": Topology(); break;
            case "credentials": Credentials(); break;
            case "documents": Documents(); break;
            case "audit": Audit(); break;
            case "backups": Backups(); break;
            case "settings": Settings(); break;
        }
    }

    public void Dashboard() => Show(Ui.Create<DashboardViewModel>());
    public void Assets(string? typeKey = null) => Show(Ui.Create<AssetsViewModel>(new AssetsArgs(typeKey)));
    public void Asset(Guid id) => Show(Ui.Create<AssetDetailsViewModel>(id));
    public void Credentials(Guid? select = null) => Show(Ui.Create<CredentialsViewModel>(new SelectArgs(select)));
    public void Documents(Guid? select = null) => Show(Ui.Create<DocumentsViewModel>(new SelectArgs(select)));
    public void Topology(Guid? scope = null) => Show(Ui.Create<TopologyViewModel>(new SelectArgs(scope)));
    public void Audit() => Show(Ui.Create<AuditViewModel>());
    public void Backups() => Show(Ui.Create<BackupsViewModel>());
    public void Settings() => Show(Ui.Create<SettingsViewModel>());

    public void Search(string query)
    {
        if (CurrentPage is SearchViewModel current) { current.Query = query; return; }
        Show(Ui.Create<SearchViewModel>(new SearchArgs(query)));
    }

    public void Open(string entityType, Guid id)
    {
        switch (entityType)
        {
            case EntityTypes.Asset: Asset(id); break;
            case EntityTypes.Credential: Credentials(id); break;
            case EntityTypes.Documentation: Documents(id); break;
        }
    }

    [RelayCommand]
    public void Back()
    {
        if (_history.Count == 0) return;
        CurrentPage?.OnLeave();
        var page = _history.Pop();
        CurrentPage = page;
        NavKey = page.NavKey;
        CanGoBack = _history.Count > 0;
        _ = RunAsync(page.LoadAsync);
    }

    [RelayCommand] private void OpenNode(TreeNodeViewModel? node) { if (node is not null) Asset(node.Node.AssetId); }

    [RelayCommand] private void SubmitSearch() { _searchDebounce.Stop(); if (SearchText.Trim().Length > 0) Search(SearchText); }

    [RelayCommand] private Task LockAsync() => _guard.LockAsync("Manuel");

    [RelayCommand] private void ToggleTheme() => Theme.Toggle();

    [RelayCommand]
    private async Task NewAssetAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(null, null, null)));
        if (id is { } created) Asset(created);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _searchDebounce.Stop();
        _infrastructure.Changed -= OnInfrastructureChanged;
        _backup.BackupCompleted -= OnBackupCompleted;
        _guard.Tick -= OnGuardTick;
        CurrentPage?.OnLeave();
        foreach (var page in _history) page.OnLeave();
        _history.Clear();
        CurrentPage = null;
    }
}

// Arguments typés pour la création des pages (ActivatorUtilities ne distingue pas deux Guid? ou string?).
public sealed record AssetsArgs(string? TypeKey);
public sealed record SelectArgs(Guid? Id);
public sealed record SearchArgs(string Query);
public sealed record AssetEditorArgs(Guid? AssetId, Guid? ParentId, string? TypeKey);
