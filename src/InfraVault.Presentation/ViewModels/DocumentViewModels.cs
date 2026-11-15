using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.DTOs;
using InfraVault.Application.Security;
using InfraVault.Application.Services;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.ViewModels;

/// <summary>Documentation technique : lecture mise en forme, édition Markdown avec aperçu, versions et pièces jointes chiffrées.</summary>
public sealed partial class DocumentsViewModel(UiContext ui, DocumentationService documentation, SelectArgs args) : PageViewModel(ui)
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Guid? _pendingSelection = args.Id;
    private bool _loaded;
    private bool _isNewDraft;

    public override string Title => "Documentation";
    public override string NavKey => "documents";

    public ObservableCollection<DocumentSummaryDto> Items { get; } = [];
    public IReadOnlyList<Option> TypeOptions { get; } =
        [new Option(null, "Tous les types"), .. DocumentTypes.All.Select(t => new Option(t, t))];
    public IReadOnlyList<string> DocumentTypeNames => DocumentTypes.All;

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private Option? _selectedType;
    [ObservableProperty] private DocumentSummaryDto? _selected;
    [ObservableProperty] private DocumentDetailDto? _detail;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _vaultHasNone;
    [ObservableProperty] private IReadOnlyList<string> _tags = [];

    /// <summary>Contenu affiché par le lecteur (version courante ou ancienne version).</summary>
    [ObservableProperty] private string _viewContent = "";
    [ObservableProperty] private string? _viewingVersionLabel;

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editType = "Note";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private string _editContent = "";
    [ObservableProperty] private bool _leakWarning;

    partial void OnFilterChanged(string value) { _debounce.Stop(); _debounce.Start(); }
    partial void OnSelectedTypeChanged(Option? value) { if (_loaded) _ = RunAsync(QueryAsync); }
    partial void OnEditContentChanged(string value) => LeakWarning = SecretLeakDetector.LooksLikeSecret(value);

    partial void OnSelectedChanged(DocumentSummaryDto? value)
    {
        if (IsEditing) return;
        _ = RunAsync(() => LoadDetailAsync(value?.Id));
    }

    public override async Task LoadAsync()
    {
        _loaded = false;
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        SelectedType ??= TypeOptions[0];
        _loaded = true;
        if (!IsEditing) await QueryAsync();
    }

    private void OnDebounce(object? sender, EventArgs e) { _debounce.Stop(); _ = RunAsync(QueryAsync); }

    private async Task QueryAsync()
    {
        var keep = _pendingSelection ?? Selected?.Id;
        _pendingSelection = null;
        var items = await documentation.ListAsync(Filter, SelectedType?.Value);
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        IsEmpty = items.Count == 0;
        VaultHasNone = items.Count == 0 && Filter.Trim().Length == 0 && SelectedType?.Value is null;
        var target = items.FirstOrDefault(i => i.Id == keep) ?? items.FirstOrDefault();
        if (Equals(Selected, target)) await LoadDetailAsync(target?.Id);
        else Selected = target;
    }

    private async Task LoadDetailAsync(Guid? id)
    {
        ViewingVersionLabel = null;
        if (id is null) { Detail = null; ViewContent = ""; return; }
        var detail = await documentation.GetAsync(id.Value);
        Detail = detail;
        ViewContent = detail?.Content ?? "";
        Tags = TagList.Parse(detail?.Summary.Tags);
    }

    public override void OnLeave()
    {
        _debounce.Stop();
        if (!IsEditing) { ViewContent = ""; Detail = null; }
    }

    // ───────────── Édition ─────────────

    [RelayCommand]
    private void New()
    {
        _isNewDraft = true;
        EditTitle = "";
        EditType = "Procédure";
        EditTags = "";
        EditContent = "# Titre\n\nDécrivez la procédure, puis ajoutez les commandes :\n\n```bash\n\n```\n";
        IsEditing = true;
    }

    [RelayCommand]
    private void Edit()
    {
        if (Detail is null) return;
        _isNewDraft = false;
        EditTitle = Detail.Summary.Title;
        EditType = Detail.Summary.DocumentType;
        EditTags = Detail.Summary.Tags ?? "";
        EditContent = ViewingVersionLabel is null ? Detail.Content : ViewContent;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task CancelEditAsync()
    {
        var original = _isNewDraft ? null : Detail?.Content;
        var dirty = _isNewDraft ? EditTitle.Trim().Length > 0 : EditContent != original || EditTitle != Detail?.Summary.Title;
        if (dirty && !await Ui.Dialogs.ConfirmAsync("Abandonner les modifications ?", "Les changements non enregistrés seront perdus.", "Abandonner", danger: true))
            return;
        IsEditing = false;
        EditContent = "";
        if (Detail is null) await RunAsync(QueryAsync);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (LeakWarning && !await Ui.Dialogs.ConfirmAsync("Ce document semble contenir un secret",
                "Un mot de passe, un jeton ou une clé privée a été détecté dans le texte. Les secrets doivent être enregistrés dans les identifiants " +
                "(champs chiffrés, historisés et journalisés), puis simplement référencés ici.\n\nEnregistrer malgré tout ?", "Enregistrer quand même"))
            return;

        IReadOnlyList<Guid> assetIds = _isNewDraft || Detail is null ? [] : Detail.Assets.Select(a => a.Id).ToList();
        Guid? id = null;
        var ok = await RunAsync(async () => id = await documentation.SaveAsync(new DocumentInput
        {
            Id = _isNewDraft ? null : Detail?.Summary.Id,
            Title = EditTitle,
            DocumentType = EditType,
            Tags = EditTags,
            Content = EditContent,
            AssetIds = assetIds,
        }), "Document enregistré.");
        if (!ok) return;
        IsEditing = false;
        EditContent = "";
        _pendingSelection = id;
        await RunAsync(QueryAsync);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Detail is null) return;
        if (!await Ui.Dialogs.ConfirmAsync("Supprimer ce document ?",
                $"« {Detail.Summary.Title} », ses versions et ses pièces jointes seront supprimés définitivement.", "Supprimer", danger: true)) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await documentation.DeleteAsync(id); await QueryAsync(); }, "Document supprimé.");
    }

    // ───────────── Versions ─────────────

    [RelayCommand]
    private async Task ViewVersionAsync(DocumentVersionDto version)
    {
        if (Detail is null) return;
        if (version.IsCurrent)
        {
            ViewContent = Detail.Content;
            ViewingVersionLabel = null;
            return;
        }
        await RunAsync(async () =>
        {
            ViewContent = await documentation.GetVersionContentAsync(version.Id);
            ViewingVersionLabel = $"Version {version.VersionNumber} du {version.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} — lecture seule";
        });
    }

    [RelayCommand]
    private void BackToCurrent()
    {
        if (Detail is null) return;
        ViewContent = Detail.Content;
        ViewingVersionLabel = null;
    }

    // ───────────── Pièces jointes ─────────────

    [RelayCommand]
    private async Task AddAttachmentAsync()
    {
        if (Detail is null) return;
        var path = Ui.Files.Open("Joindre un fichier", "Tous les fichiers (*.*)|*.*");
        if (path is null) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () =>
        {
            var info = new FileInfo(path);
            if (info.Length > Limits.AttachmentMaxBytes)
                throw new Shared.Exceptions.InfraVaultException($"La pièce jointe dépasse {Limits.AttachmentMaxBytes / (1024 * 1024)} Mo.");
            await documentation.AddAttachmentAsync(id, info.Name, await File.ReadAllBytesAsync(path));
            await LoadDetailAsync(id);
        }, "Pièce jointe chiffrée et ajoutée.");
    }

    [RelayCommand]
    private async Task SaveAttachmentAsync(AttachmentDto attachment)
    {
        var path = Ui.Files.Save("Enregistrer la pièce jointe", "Tous les fichiers (*.*)|*.*", attachment.FileName);
        if (path is null) return;
        await RunAsync(async () =>
        {
            var (_, content) = await documentation.GetAttachmentAsync(attachment.Id);
            await File.WriteAllBytesAsync(path, content);
        }, "Pièce jointe déchiffrée et enregistrée.");
    }

    [RelayCommand]
    private async Task DeleteAttachmentAsync(AttachmentDto attachment)
    {
        if (Detail is null) return;
        if (!await Ui.Dialogs.ConfirmAsync("Supprimer la pièce jointe ?", $"« {attachment.FileName} » sera supprimée définitivement.", "Supprimer", danger: true)) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await documentation.DeleteAttachmentAsync(attachment.Id); await LoadDetailAsync(id); });
    }

    // ───────────── Ressources liées ─────────────

    [RelayCommand]
    private async Task LinkAssetAsync()
    {
        if (Detail is null) return;
        var picked = await PickItems.PickAssetAsync(Ui, "Associer une ressource", Detail.Assets.Select(a => a.Id));
        if (picked is null) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await documentation.LinkAssetAsync(id, picked.Id); await LoadDetailAsync(id); }, "Ressource associée.");
    }

    [RelayCommand]
    private async Task UnlinkAssetAsync(AssetSummaryDto asset)
    {
        if (Detail is null) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await documentation.UnlinkAssetAsync(id, asset.Id); await LoadDetailAsync(id); });
    }

    [RelayCommand] private void OpenAsset(AssetSummaryDto asset) => Ui.Navigator.Asset(asset.Id);
}

// ───────────────────────── Recherche ─────────────────────────

public sealed partial class SearchViewModel(UiContext ui, SearchService search, SearchArgs args) : PageViewModel(ui)
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private CancellationTokenSource? _cts;

    public override string Title => "Recherche";
    public override string NavKey => "";

    [ObservableProperty] private string _query = args.Query;
    [ObservableProperty] private bool _includeContent;
    [ObservableProperty] private IReadOnlyList<SearchGroup> _groups = [];
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _noResult;

    partial void OnQueryChanged(string value) { _debounce.Stop(); _debounce.Start(); }
    partial void OnIncludeContentChanged(bool value) => _ = RunAsync(ExecuteAsync);

    public override Task LoadAsync()
    {
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        return ExecuteAsync();
    }

    private void OnDebounce(object? sender, EventArgs e) { _debounce.Stop(); _ = RunAsync(ExecuteAsync); }

    private async Task ExecuteAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var results = await search.SearchAsync(Query, IncludeContent, ct: cts.Token);
        if (cts.IsCancellationRequested) return;
        Groups = results.Groups;
        NoResult = results.Total == 0 && Query.Trim().Length > 0;
        Summary = Query.Trim().Length == 0
            ? "Saisissez un hostname, une adresse IP, un nom, un username ou un tag."
            : $"{results.Total} résultat{(results.Total > 1 ? "s" : "")} · {results.Elapsed.TotalMilliseconds:0} ms";
    }

    public override void OnLeave() { _debounce.Stop(); _cts?.Cancel(); }

    [RelayCommand] private void Open(SearchHit hit) => Ui.Navigator.Open(hit.EntityType, hit.EntityId);
}
