using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.DTOs;
using InfraVault.Application.Services;
using InfraVault.Domain.ValueObjects;
using InfraVault.Presentation.Controls;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.ViewModels;

// ───────────────────────── Tableau de bord ─────────────────────────

public sealed record StatTile(string Label, int Value, string Icon, string Color, string? NavKey, string? TypeKey);
public sealed record TypeBar(TypeCount Type, double Ratio);
public sealed record EnvBar(string Name, int Count, double Ratio);

public sealed partial class DashboardViewModel(UiContext ui, VaultService vault, CredentialService credentials, BackupService backup)
    : PageViewModel(ui)
{
    public override string Title => "Tableau de bord";
    public override string NavKey => "dashboard";

    [ObservableProperty] private DashboardDto? _data;
    [ObservableProperty] private IReadOnlyList<StatTile> _tiles = [];
    [ObservableProperty] private IReadOnlyList<TypeBar> _typeBars = [];
    [ObservableProperty] private IReadOnlyList<EnvBar> _envBars = [];
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _greeting = "";

    public override async Task LoadAsync()
    {
        var d = await vault.GetDashboardAsync(credentials);
        Data = d;
        IsEmpty = d.AssetCount == 0 && d.CredentialCount == 0;
        Tiles =
        [
            new("Ressources", d.AssetCount, "cube", "amber", "assets", null),
            new("Serveurs", d.ServerCount, "server", "sky", "assets", AssetTypeKeys.Server),
            new("VM", d.VmCount, "monitor", "teal", "assets", AssetTypeKeys.VirtualMachine),
            new("Applications", d.ApplicationCount, "app", "rose", "assets", AssetTypeKeys.Application),
            new("Bases", d.DatabaseCount, "database", "lime", "assets", AssetTypeKeys.Database),
            new("Identifiants", d.CredentialCount, "key", "violet", "credentials", null),
        ];
        var max = Math.Max(1, d.ByType.Count == 0 ? 1 : d.ByType.Max(t => t.Count));
        TypeBars = d.ByType.OrderByDescending(t => t.Count).Select(t => new TypeBar(t, (double)t.Count / max)).ToList();
        var envMax = Math.Max(1, d.ByEnvironment.Count == 0 ? 1 : d.ByEnvironment.Max(e => e.Count));
        EnvBars = d.ByEnvironment.Select(e => new EnvBar(e.Environment, e.Count, (double)e.Count / envMax)).ToList();
        var hour = DateTime.Now.Hour;
        Greeting = hour < 12 ? "Bonjour" : hour < 18 ? "Bon après-midi" : "Bonsoir";
    }

    [RelayCommand]
    private void OpenTile(StatTile tile)
    {
        if (tile.NavKey == "credentials") Ui.Navigator.Credentials();
        else Ui.Navigator.Assets(tile.TypeKey);
    }

    [RelayCommand] private void OpenType(TypeBar bar) => Ui.Navigator.Assets(bar.Type.TypeKey);
    [RelayCommand] private void OpenAsset(AssetSummaryDto asset) => Ui.Navigator.Asset(asset.Id);
    [RelayCommand] private void OpenCredential(CredentialSummaryDto credential) => Ui.Navigator.Credentials(credential.Id);
    [RelayCommand] private void OpenEvent(AuditEventDto e) { if (e.EntityId is { } id) Ui.Navigator.Open(e.EntityType, id); }
    [RelayCommand] private void GoAudit() => Ui.Navigator.Audit();
    [RelayCommand] private void GoBackups() => Ui.Navigator.Backups();
    [RelayCommand] private void GoTopology() => Ui.Navigator.Topology();

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        if (await RunAsync(async () => await backup.CreateBackupAsync(), "Sauvegarde chiffrée créée."))
            await RunAsync(LoadAsync);
    }

    [RelayCommand]
    private async Task NewAssetAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(null, null, null)));
        if (id is { } created) Ui.Navigator.Asset(created);
    }
}

// ───────────────────────── Liste des ressources ─────────────────────────

public sealed record Option(string? Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class AssetsViewModel(UiContext ui, InfrastructureService infrastructure, VaultService vault, AssetsArgs args)
    : PageViewModel(ui)
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _loaded;

    public override string Title => "Infrastructure";
    public override string NavKey => "assets";

    public ObservableCollection<AssetSummaryDto> Items { get; } = [];
    [ObservableProperty] private IReadOnlyList<Option> _types = [];
    [ObservableProperty] private IReadOnlyList<Option> _environments = [];
    public IReadOnlyList<Option> Statuses { get; } =
    [
        new(null, "Tous les statuts"), new(AssetStatuses.Active, "Actif"), new(AssetStatuses.Maintenance, "Maintenance"),
        new(AssetStatuses.Retired, "Retiré"), new(AssetStatuses.Unknown, "Inconnu"),
    ];

    [ObservableProperty] private Option? _selectedType;
    [ObservableProperty] private Option? _selectedEnvironment;
    [ObservableProperty] private Option? _selectedStatus;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasFilter;

    partial void OnSelectedTypeChanged(Option? value) => Refresh();
    partial void OnSelectedEnvironmentChanged(Option? value) => Refresh();
    partial void OnSelectedStatusChanged(Option? value) => Refresh();
    partial void OnFilterChanged(string value) { _debounce.Stop(); _debounce.Start(); }

    private void Refresh() { if (_loaded) _ = RunAsync(QueryAsync); }

    public override async Task LoadAsync()
    {
        _loaded = false;
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        var types = await vault.GetAssetTypesAsync();
        var previousType = SelectedType?.Value ?? args.TypeKey;
        Types = [new Option(null, "Toutes les catégories"), .. types.Select(t => new Option(t.Key, $"{t.Name} ({t.AssetCount})"))];
        Environments = [new Option(null, "Tous les environnements"), .. vault.Current.Environments.Select(e => new Option(e, e))];
        SelectedType = Types.FirstOrDefault(t => t.Value == previousType) ?? Types[0];
        SelectedEnvironment ??= Environments[0];
        SelectedStatus ??= Statuses[0];
        _loaded = true;
        await QueryAsync();
    }

    private void OnDebounce(object? sender, EventArgs e) { _debounce.Stop(); Refresh(); }

    private async Task QueryAsync()
    {
        var items = await infrastructure.ListAssetsAsync(new AssetFilter(SelectedType?.Value, SelectedEnvironment?.Value, SelectedStatus?.Value, Filter));
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        HasFilter = SelectedType?.Value is not null || SelectedEnvironment?.Value is not null || SelectedStatus?.Value is not null || Filter.Trim().Length > 0;
        IsEmpty = items.Count == 0;
        CountText = items.Count switch { 0 => "Aucune ressource", 1 => "1 ressource", _ => $"{items.Count} ressources" };
    }

    public override void OnLeave() => _debounce.Stop();

    [RelayCommand] private void Open(AssetSummaryDto? asset) { if (asset is not null) Ui.Navigator.Asset(asset.Id); }

    [RelayCommand]
    private void ClearFilters()
    {
        Filter = "";
        SelectedType = Types[0];
        SelectedEnvironment = Environments[0];
        SelectedStatus = Statuses[0];
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(null, null, SelectedType?.Value)));
        if (id is { } created) Ui.Navigator.Asset(created);
    }
}

// ───────────────────────── Éditeur de ressource (avec sélecteur d'icône) ─────────────────────────

public sealed partial class FieldViewModel(FieldDefinition definition, string? value, bool hasStoredSecret) : ObservableObject
{
    public FieldDefinition Definition { get; } = definition;
    public string Label => Definition.Label;
    public bool IsChoice => Definition.Kind == FieldKind.Choice;
    public bool IsMultiline => Definition.Kind == FieldKind.Multiline;
    public bool IsText => !IsChoice && !IsMultiline;
    public bool IsSensitive => Definition.IsSensitive;
    public IReadOnlyList<string> Options => Definition.Options ?? [];
    public string Placeholder => HasStoredSecret && !ClearStored
        ? "Valeur chiffrée enregistrée — saisir pour remplacer"
        : Definition.Placeholder ?? (Definition.Kind == FieldKind.Date ? "AAAA-MM-JJ" : "");

    [ObservableProperty] private string _value = value ?? "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Placeholder))] private bool _hasStoredSecret = hasStoredSecret;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Placeholder))] private bool _clearStored;

    [RelayCommand] private void Clear() { Value = ""; ClearStored = true; }
}

public sealed record IconCategory(string Name, string Icon, IReadOnlyList<string> Icons);

public sealed partial class AssetEditorDialogViewModel : DialogViewModel
{
    private readonly UiContext _ui;
    private readonly InfrastructureService _infrastructure;
    private readonly VaultService _vault;
    private readonly AssetEditorArgs _args;
    private IReadOnlyList<AssetTypeDto> _typeDtos = [];
    private Dictionary<string, string?> _storedValues = [];
    private HashSet<string> _storedSecrets = [];
    private bool _iconCustomized;

    public AssetEditorDialogViewModel(UiContext ui, InfrastructureService infrastructure, VaultService vault, AssetEditorArgs args)
    {
        _ui = ui;
        _infrastructure = infrastructure;
        _vault = vault;
        _args = args;
        IsNew = args.AssetId is null;
        Title = IsNew ? "Nouvelle ressource" : "Modifier la ressource";
        IconCategories = Icons.PickerCategories.Select(c => new IconCategory(c.Category, c.Icon, c.Icons)).ToList();
        _selectedCategory = IconCategories[0];
        _ = TryAsync(LoadAsync);
    }

    public override double Width => 760;
    public bool IsNew { get; }
    public IReadOnlyList<IconCategory> IconCategories { get; }
    public IReadOnlyList<string> Colors => Icons.PickerColors;
    public IReadOnlyList<Option> StatusOptions { get; } =
    [
        new(AssetStatuses.Active, "Actif"), new(AssetStatuses.Maintenance, "Maintenance"),
        new(AssetStatuses.Retired, "Retiré"), new(AssetStatuses.Unknown, "Inconnu"),
    ];
    public IReadOnlyList<Option> RelationOptions { get; } =
        RelationshipTypes.All.Where(r => r.IsHierarchical).Select(r => new Option(r.Key, $"Le parent {r.Forward} cette ressource")).ToList();

    public ObservableCollection<FieldViewModel> Fields { get; } = [];
    [ObservableProperty] private IReadOnlyList<AssetTypeDto> _types = [];
    [ObservableProperty] private IReadOnlyList<string> _environments = [];
    [ObservableProperty] private AssetTypeDto? _selectedType;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _hostname = "";
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _environment = "Production";
    [ObservableProperty] private Option? _status;
    [ObservableProperty] private string _tags = "";
    [ObservableProperty] private bool _internetExposed;
    [ObservableProperty] private string _publicAddress = "";
    [ObservableProperty] private string _exposedPorts = "";
    [ObservableProperty] private PickItem? _parent;
    [ObservableProperty] private Option? _parentRelation;
    [ObservableProperty] private string _icon = "cube";
    [ObservableProperty] private string _color = "slate";
    [ObservableProperty] private IconCategory _selectedCategory;
    [ObservableProperty] private bool _isIconPickerOpen;
    [ObservableProperty] private bool _isIconCustom;

    private async Task LoadAsync()
    {
        _typeDtos = await _vault.GetAssetTypesAsync();
        Types = _typeDtos;
        Environments = _vault.Current.Environments;
        Status = StatusOptions[0];

        if (_args.AssetId is { } id)
        {
            var detail = await _infrastructure.GetAssetAsync(id) ?? throw new Shared.Exceptions.NotFoundException("Ressource");
            var s = detail.Summary;
            Name = s.Name;
            Hostname = s.Hostname ?? "";
            IpAddress = s.IpAddress ?? "";
            Description = detail.Description ?? "";
            Environment = s.Environment;
            if (!Environments.Contains(s.Environment)) Environments = [.. Environments, s.Environment];
            Status = StatusOptions.FirstOrDefault(o => o.Value == s.Status) ?? StatusOptions[0];
            Tags = s.Tags ?? "";
            InternetExposed = s.InternetExposed;
            PublicAddress = s.PublicAddress ?? "";
            ExposedPorts = s.ExposedPorts ?? "";
            _storedValues = detail.Properties.Where(p => !p.IsSensitive).ToDictionary(p => p.Key, p => p.Value);
            _storedSecrets = detail.Properties.Where(p => p.IsSensitive && p.HasValue).Select(p => p.Key).ToHashSet();
            _iconCustomized = detail.HasCustomIcon;
            IsIconCustom = detail.HasCustomIcon;
            SelectedType = _typeDtos.First(t => t.Id == detail.TypeId);
            Icon = s.Icon;
            Color = s.Color;
        }
        else
        {
            Environment = Environments.FirstOrDefault() ?? "Production";
            SelectedType = _typeDtos.FirstOrDefault(t => t.Key == _args.TypeKey) ?? _typeDtos.FirstOrDefault(t => t.Key == AssetTypeKeys.Server) ?? _typeDtos[0];
            if (_args.ParentId is { } parentId)
            {
                var parent = await _infrastructure.GetAssetAsync(parentId);
                if (parent is not null)
                {
                    Parent = PickItems.From(parent.Summary);
                    var relation = RelationshipTypes.DefaultFor(parent.Summary.TypeKey);
                    ParentRelation = RelationOptions.FirstOrDefault(o => o.Value == relation);
                    Environment = parent.Summary.Environment;
                }
            }
        }
    }

    partial void OnSelectedTypeChanged(AssetTypeDto? value)
    {
        if (value is null) return;
        var current = Fields.ToDictionary(f => f.Definition.Key, f => f.Value);
        Fields.Clear();
        foreach (var field in value.Fields)
        {
            var text = current.GetValueOrDefault(field.Key) is { Length: > 0 } typed ? typed : _storedValues.GetValueOrDefault(field.Key);
            Fields.Add(new FieldViewModel(field, field.IsSensitive ? null : text, field.IsSensitive && _storedSecrets.Contains(field.Key)));
        }
        if (!_iconCustomized)
        {
            Icon = value.Icon;
            Color = value.Color;
        }
    }

    [RelayCommand] private void ToggleIconPicker() => IsIconPickerOpen = !IsIconPickerOpen;
    [RelayCommand] private void SelectCategory(IconCategory category) => SelectedCategory = category;

    [RelayCommand]
    private void SelectIcon(string icon)
    {
        Icon = icon;
        _iconCustomized = IsIconCustom = true;
    }

    [RelayCommand]
    private void SelectColor(string color)
    {
        Color = color;
        _iconCustomized = IsIconCustom = true;
    }

    [RelayCommand]
    private void ResetIcon()
    {
        _iconCustomized = IsIconCustom = false;
        if (SelectedType is { } type) { Icon = type.Icon; Color = type.Color; }
    }

    [RelayCommand]
    private async Task PickParentAsync()
    {
        var picked = await PickItems.PickAssetAsync(_ui, "Choisir la ressource parente");
        if (picked is null) return;
        Parent = picked;
        ParentRelation ??= RelationOptions[0];
    }

    [RelayCommand] private void ClearParent() => Parent = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedType is null) return;
        var properties = new Dictionary<string, string?>();
        foreach (var f in Fields)
        {
            // Champ sensible laissé vide : la valeur chiffrée existante est conservée, sauf effacement explicite.
            if (f.IsSensitive && string.IsNullOrWhiteSpace(f.Value) && f.HasStoredSecret && !f.ClearStored) continue;
            properties[f.Definition.Key] = f.Value;
        }

        Guid? saved = null;
        var ok = await TryAsync(async () => saved = await _infrastructure.SaveAssetAsync(new AssetInput
        {
            Id = _args.AssetId,
            AssetTypeId = SelectedType.Id,
            Name = Name,
            Hostname = Hostname,
            IpAddress = IpAddress,
            Description = Description,
            Environment = Environment,
            Status = Status?.Value ?? AssetStatuses.Active,
            Tags = Tags,
            InternetExposed = InternetExposed,
            PublicAddress = PublicAddress,
            ExposedPorts = ExposedPorts,
            Icon = IsIconCustom ? Icon : null,
            IconColor = IsIconCustom ? Color : null,
            Properties = properties,
            ParentId = IsNew ? Parent?.Id : null,
            ParentRelationType = ParentRelation?.Value,
        }));
        if (ok) Close(saved);
    }
}

// ───────────────────────── Dialogue de relation ─────────────────────────

public sealed partial class RelationshipDialogViewModel : DialogViewModel
{
    private readonly UiContext _ui;
    private readonly InfrastructureService _infrastructure;
    private readonly AssetSummaryDto _asset;

    public RelationshipDialogViewModel(UiContext ui, InfrastructureService infrastructure, AssetSummaryDto asset)
    {
        _ui = ui;
        _infrastructure = infrastructure;
        _asset = asset;
        Title = "Nouvelle relation";
        AssetName = asset.Name;
        Options = RelationshipTypes.All.SelectMany(r => r.Forward == r.Inverse
            ? new[] { new RelationChoice(r.Key, true, r.Forward, r.IsHierarchical) }
            : new[] { new RelationChoice(r.Key, true, r.Forward, r.IsHierarchical), new RelationChoice(r.Key, false, r.Inverse, r.IsHierarchical) }).ToList();
        _selected = Options.First(o => o.Type == RelationshipTypes.Uses && o.Outgoing);
    }

    public sealed record RelationChoice(string Type, bool Outgoing, string Label, bool Hierarchical)
    {
        public override string ToString() => Label;
    }

    public string AssetName { get; }
    public IReadOnlyList<RelationChoice> Options { get; }
    public override double Width => 560;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PortsHint))] private RelationChoice _selected;
    [ObservableProperty] private PickItem? _other;
    [ObservableProperty] private string _ports = "";
    [ObservableProperty] private string _description = "";

    public string PortsHint => Selected.Outgoing
        ? "Ports de la cible utilisés par ce flux, ex. 5432/tcp, 443"
        : "Ports de cette ressource utilisés par ce flux, ex. 5432/tcp, 443";

    [RelayCommand]
    private async Task PickOtherAsync()
    {
        var picked = await PickItems.PickAssetAsync(_ui, "Choisir la ressource à relier", [_asset.Id]);
        if (picked is not null) Other = picked;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Other is null) { Error = "Choisissez la ressource à relier."; return; }
        var (source, target) = Selected.Outgoing ? (_asset.Id, Other.Id) : (Other.Id, _asset.Id);
        if (await TryAsync(() => _infrastructure.AddRelationshipAsync(source, target, Selected.Type, Description, Ports))) Close(true);
    }
}

// ───────────────────────── Détail d'une ressource ─────────────────────────

public sealed partial class PropertyViewModel(AssetPropertyDto property) : ObservableObject
{
    public AssetPropertyDto Property { get; } = property;
    public string Label => Property.Label;
    public bool IsSensitive => Property.IsSensitive;
    public bool IsUrl => Property.Kind == FieldKind.Url;
    public bool IsMultiline => Property.Kind == FieldKind.Multiline;
    [ObservableProperty] private string _display = property.IsSensitive ? "••••••••••" : property.Value ?? "";
    [ObservableProperty] private bool _isRevealed;
}

public sealed record PropertyGroup(string Name, IReadOnlyList<PropertyViewModel> Items);
public sealed record Fact(string Label, string Value, bool Mono = false);

public sealed partial class AssetDetailsViewModel(
    UiContext ui, InfrastructureService infrastructure, CredentialService credentials, DocumentationService documentation,
    AuditService audit, Guid assetId) : PageViewModel(ui)
{
    public override string Title => Detail?.Summary.Name ?? "Ressource";
    public override string NavKey => "assets";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private AssetDetailDto? _detail;
    [ObservableProperty] private IReadOnlyList<Fact> _facts = [];
    [ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups = [];
    [ObservableProperty] private IReadOnlyList<CredentialSummaryDto> _credentialItems = [];
    [ObservableProperty] private IReadOnlyList<RelationshipDto> _relationships = [];
    [ObservableProperty] private IReadOnlyList<RelationshipDto> _children = [];
    [ObservableProperty] private IReadOnlyList<DocumentSummaryDto> _documents = [];
    [ObservableProperty] private IReadOnlyList<AuditEventDto> _history = [];
    [ObservableProperty] private IReadOnlyList<string> _tags = [];
    [ObservableProperty] private IReadOnlyList<string> _exposedPorts = [];
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private bool _notFound;

    public override async Task LoadAsync()
    {
        var detail = await infrastructure.GetAssetAsync(assetId);
        if (detail is null) { NotFound = true; Detail = null; return; }
        Detail = detail;
        var s = detail.Summary;

        var facts = new List<Fact> { new("Catégorie", s.TypeName), new("Environnement", s.Environment) };
        if (!string.IsNullOrEmpty(s.Hostname)) facts.Add(new("Hostname", s.Hostname, true));
        if (!string.IsNullOrEmpty(s.IpAddress)) facts.Add(new("Adresse IP", s.IpAddress, true));
        foreach (var p in detail.Properties.Where(p => p is { HasValue: true, IsSensitive: false, Kind: not FieldKind.Multiline }).Take(6))
            facts.Add(new(p.Label, p.Value ?? "", p.Kind is FieldKind.Number or FieldKind.Url));
        Facts = facts;

        PropertyGroups = detail.Properties.Where(p => p.HasValue)
            .GroupBy(p => p.Group ?? "Général")
            .Select(g => new PropertyGroup(g.Key, g.Select(p => new PropertyViewModel(p)).ToList())).ToList();
        Tags = TagList.Parse(s.Tags);
        ExposedPorts = PortList.Parse(s.ExposedPorts);

        CredentialItems = await credentials.ListAsync(assetId: assetId);
        var relations = await infrastructure.GetRelationshipsAsync(assetId);
        Children = relations.Where(r => r is { IsHierarchical: true, IsOutgoing: true }).ToList();
        Relationships = relations.Where(r => !(r is { IsHierarchical: true, IsOutgoing: true })).ToList();
        Documents = await documentation.ListAsync(assetId: assetId);
        History = await audit.QueryAsync(new AuditQuery(EntityId: assetId, Take: 100));
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(assetId, null, null)));
        if (id is not null) await RunAsync(LoadAsync, "Ressource enregistrée.");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Detail is null) return;
        DeleteImpact? impact = null;
        if (!await RunAsync(async () => impact = await infrastructure.GetDeleteImpactAsync(assetId)) || impact is null) return;

        var lines = new List<string>();
        if (impact.ChildCount > 0) lines.Add($"• {impact.ChildCount} ressource(s) hébergée(s) ou contenue(s) seront détachées (elles ne sont pas supprimées)");
        if (impact.RelationshipCount > 0) lines.Add($"• {impact.RelationshipCount} relation(s) seront supprimées");
        if (impact.CredentialLinkCount > 0) lines.Add($"• {impact.CredentialLinkCount} identifiant(s) seront détachés (ils restent dans le coffre)");
        if (impact.DocumentationLinkCount > 0) lines.Add($"• {impact.DocumentationLinkCount} document(s) seront détachés");
        var message = impact.HasDependencies
            ? $"« {impact.AssetName} » est encore référencée :\n\n{string.Join("\n", lines)}\n\nCette action est définitive."
            : $"« {impact.AssetName} » sera supprimée définitivement.";
        if (!await Ui.Dialogs.ConfirmAsync("Supprimer cette ressource ?", message, "Supprimer", danger: true)) return;
        if (await RunAsync(() => infrastructure.DeleteAssetAsync(assetId, confirmDependencies: true), "Ressource supprimée."))
            Ui.Navigator.Back();
    }

    [RelayCommand]
    private async Task AddChildAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<AssetEditorDialogViewModel>(new AssetEditorArgs(null, assetId, null)));
        if (id is not null) await RunAsync(LoadAsync, "Ressource ajoutée.");
    }

    [RelayCommand]
    private async Task AddRelationshipAsync()
    {
        if (Detail is null) return;
        if (await Ui.Dialogs.ShowAsync<bool>(new RelationshipDialogViewModel(Ui, infrastructure, Detail.Summary)))
            await RunAsync(LoadAsync, "Relation créée.");
    }

    [RelayCommand]
    private async Task RemoveRelationshipAsync(RelationshipDto relation)
    {
        if (!await Ui.Dialogs.ConfirmAsync("Supprimer cette relation ?",
                $"Le lien « {relation.Label} {relation.Other.Name} » sera supprimé. Les deux ressources sont conservées.", "Supprimer", danger: true)) return;
        await RunAsync(async () => { await infrastructure.RemoveRelationshipAsync(relation.Id); await LoadAsync(); });
    }

    [RelayCommand] private void OpenAsset(AssetSummaryDto asset) => Ui.Navigator.Asset(asset.Id);
    [RelayCommand] private void OpenRelated(RelationshipDto relation) => Ui.Navigator.Asset(relation.Other.Id);
    [RelayCommand] private void OpenCredential(CredentialSummaryDto credential) => Ui.Navigator.Credentials(credential.Id);
    [RelayCommand] private void OpenDocument(DocumentSummaryDto document) => Ui.Navigator.Documents(document.Id);
    [RelayCommand] private void ShowTopology() => Ui.Navigator.Topology(assetId);

    [RelayCommand]
    private async Task RevealPropertyAsync(PropertyViewModel property)
    {
        if (property.IsRevealed)
        {
            property.Display = "••••••••••";
            property.IsRevealed = false;
            return;
        }
        await RunAsync(async () =>
        {
            property.Display = await infrastructure.RevealPropertyAsync(assetId, property.Property.Key) ?? "";
            property.IsRevealed = true;
        });
    }

    [RelayCommand]
    private void CopyValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Ui.Clipboard.CopyPlain(value);
        Ui.Toasts.Show("Copié dans le presse-papiers.");
    }

    [RelayCommand]
    private async Task LinkCredentialAsync()
    {
        var all = await credentials.ListAsync();
        var linked = CredentialItems.Select(c => c.Id).ToHashSet();
        var picked = await Ui.Dialogs.ShowAsync<PickItem>(new PickerDialogViewModel("Associer un identifiant",
            all.Where(c => !linked.Contains(c.Id)).Select(PickItems.From), "Aucun autre identifiant dans le coffre."));
        if (picked is null) return;
        await RunAsync(async () => { await credentials.LinkAssetAsync(picked.Id, assetId); await LoadAsync(); }, "Identifiant associé.");
    }

    [RelayCommand]
    private async Task NewCredentialAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<CredentialEditorDialogViewModel>(new CredentialEditorArgs(null, assetId)));
        if (id is not null) await RunAsync(LoadAsync, "Identifiant créé et associé.");
    }

    [RelayCommand]
    private async Task UnlinkCredentialAsync(CredentialSummaryDto credential)
        => await RunAsync(async () => { await credentials.UnlinkAssetAsync(credential.Id, assetId); await LoadAsync(); }, "Identifiant détaché.");

    [RelayCommand]
    private async Task LinkDocumentAsync()
    {
        var all = await documentation.ListAsync();
        var linked = Documents.Select(d => d.Id).ToHashSet();
        var picked = await Ui.Dialogs.ShowAsync<PickItem>(new PickerDialogViewModel("Associer un document",
            all.Where(d => !linked.Contains(d.Id)).Select(PickItems.From), "Aucun autre document dans le coffre."));
        if (picked is null) return;
        await RunAsync(async () => { await documentation.LinkAssetAsync(picked.Id, assetId); await LoadAsync(); }, "Document associé.");
    }

    [RelayCommand]
    private async Task NewDocumentAsync()
    {
        if (Detail is null) return;
        Guid? id = null;
        if (await RunAsync(async () => id = await documentation.SaveAsync(new DocumentInput
            {
                Title = $"Notes — {Detail.Summary.Name}", DocumentType = "Note", Content = $"# {Detail.Summary.Name}\n\n", AssetIds = [assetId],
            })) && id is { } created)
            Ui.Navigator.Documents(created);
    }

    [RelayCommand]
    private async Task UnlinkDocumentAsync(DocumentSummaryDto document)
        => await RunAsync(async () => { await documentation.UnlinkAssetAsync(document.Id, assetId); await LoadAsync(); }, "Document détaché.");

    public override void OnLeave()
    {
        foreach (var p in PropertyGroups.SelectMany(g => g.Items).Where(p => p.IsRevealed))
        {
            p.Display = "••••••••••";
            p.IsRevealed = false;
        }
    }
}
