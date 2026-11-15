using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.DTOs;
using InfraVault.Application.Security;
using InfraVault.Application.Services;
using InfraVault.Domain.ValueObjects;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.ViewModels;

public sealed record CredentialEditorArgs(Guid? CredentialId, Guid? LinkAssetId);

/// <summary>
/// Liste et fiche des identifiants. Les secrets restent masqués par défaut ; l'affichage est une action
/// explicite, limitée dans le temps, et tout ce qui a été déchiffré est effacé en quittant la page.
/// </summary>
public sealed partial class CredentialsViewModel(UiContext ui, CredentialService credentials, SelectArgs args) : PageViewModel(ui)
{
    private const string Mask = "••••••••••••";
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Guid? _pendingSelection = args.Id;
    private bool _loaded;

    public override string Title => "Identifiants";
    public override string NavKey => "credentials";

    public ObservableCollection<CredentialSummaryDto> Items { get; } = [];
    public IReadOnlyList<Option> TypeOptions { get; } =
        [new Option(null, "Tous les types"), .. CredentialTypes.All.Select(t => new Option(t.Key, t.Label))];

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private Option? _selectedType;
    [ObservableProperty] private CredentialSummaryDto? _selected;
    [ObservableProperty] private CredentialDetailDto? _detail;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _vaultHasNone;
    [ObservableProperty] private string _usernameText = Mask;
    [ObservableProperty] private bool _isUsernameVisible;
    [ObservableProperty] private string _secretText = Mask;
    [ObservableProperty] private bool _isSecretVisible;
    [ObservableProperty] private int _revealRemaining;
    [ObservableProperty] private int _revealTotal = 20;
    [ObservableProperty] private string? _notesText;
    [ObservableProperty] private IReadOnlyList<string> _tags = [];
    [ObservableProperty] private bool _isSecretMultiline;

    partial void OnFilterChanged(string value) { _debounce.Stop(); _debounce.Start(); }
    partial void OnSelectedTypeChanged(Option? value) { if (_loaded) _ = RunAsync(QueryAsync); }
    partial void OnSelectedChanged(CredentialSummaryDto? value) => _ = RunAsync(() => LoadDetailAsync(value?.Id));

    public override async Task LoadAsync()
    {
        _loaded = false;
        _debounce.Tick -= OnDebounce;
        _debounce.Tick += OnDebounce;
        _revealTimer.Tick -= OnRevealTick;
        _revealTimer.Tick += OnRevealTick;
        SelectedType ??= TypeOptions[0];
        _loaded = true;
        await QueryAsync();
    }

    private void OnDebounce(object? sender, EventArgs e) { _debounce.Stop(); _ = RunAsync(QueryAsync); }

    private async Task QueryAsync()
    {
        var keep = _pendingSelection ?? Selected?.Id;
        _pendingSelection = null;
        var items = await credentials.ListAsync(Filter, SelectedType?.Value);
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        IsEmpty = items.Count == 0;
        VaultHasNone = items.Count == 0 && Filter.Trim().Length == 0 && SelectedType?.Value is null;
        Selected = items.FirstOrDefault(i => i.Id == keep) ?? items.FirstOrDefault();
    }

    private async Task LoadDetailAsync(Guid? id)
    {
        HideSecrets();
        if (id is null) { Detail = null; return; }
        var detail = await credentials.GetAsync(id.Value);
        Detail = detail;
        Tags = TagList.Parse(detail?.Summary.Tags);
        IsSecretMultiline = detail is not null && CredentialTypes.IsMultiline(detail.Summary.Type);
    }

    private void HideSecrets()
    {
        _revealTimer.Stop();
        UsernameText = SecretText = Mask;
        IsUsernameVisible = IsSecretVisible = false;
        NotesText = null;
        RevealRemaining = 0;
    }

    private void OnRevealTick(object? sender, EventArgs e)
    {
        RevealRemaining--;
        if (RevealRemaining <= 0) HideSecret();
    }

    private void HideSecret()
    {
        _revealTimer.Stop();
        SecretText = Mask;
        IsSecretVisible = false;
        RevealRemaining = 0;
    }

    public override void OnLeave()
    {
        _debounce.Stop();
        HideSecrets();
    }

    // ───────────── Secret ─────────────

    [RelayCommand]
    private async Task ToggleSecretAsync()
    {
        if (Detail is null) return;
        if (IsSecretVisible) { HideSecret(); return; }
        await RunAsync(async () =>
        {
            SecretText = await credentials.RevealSecretAsync(Detail.Summary.Id, SecretAccess.View);
            IsSecretVisible = true;
            RevealTotal = RevealRemaining = Ui.Vault.Current.SecretRevealSeconds;
            _revealTimer.Start();
        });
    }

    [RelayCommand]
    private async Task CopySecretAsync()
    {
        if (Detail is null) return;
        await RunAsync(async () =>
        {
            var seconds = Ui.Vault.Current.ClipboardClearSeconds;
            Ui.Clipboard.CopySensitive(await credentials.RevealSecretAsync(Detail.Summary.Id, SecretAccess.Copy), TimeSpan.FromSeconds(seconds));
            Ui.Toasts.Show($"Secret copié. Le presse-papiers sera vidé dans {seconds} s.");
        });
    }

    [RelayCommand]
    private async Task ToggleUsernameAsync()
    {
        if (Detail is null) return;
        if (IsUsernameVisible) { UsernameText = Mask; IsUsernameVisible = false; return; }
        await RunAsync(async () =>
        {
            UsernameText = await credentials.RevealUsernameAsync(Detail.Summary.Id) ?? "";
            IsUsernameVisible = true;
        });
    }

    [RelayCommand]
    private async Task CopyUsernameAsync()
    {
        if (Detail is null) return;
        await RunAsync(async () =>
        {
            var seconds = Ui.Vault.Current.ClipboardClearSeconds;
            Ui.Clipboard.CopySensitive(await credentials.RevealUsernameAsync(Detail.Summary.Id) ?? "", TimeSpan.FromSeconds(seconds));
            Ui.Toasts.Show($"Username copié. Le presse-papiers sera vidé dans {seconds} s.");
        });
    }

    [RelayCommand]
    private void CopyUrl()
    {
        if (Detail?.Summary.Url is not { Length: > 0 } url) return;
        Ui.Clipboard.CopyPlain(url);
        Ui.Toasts.Show("Adresse copiée.");
    }

    [RelayCommand]
    private async Task ToggleNotesAsync()
    {
        if (Detail is null) return;
        if (NotesText is not null) { NotesText = null; return; }
        await RunAsync(async () => NotesText = await credentials.RevealNotesAsync(Detail.Summary.Id) ?? "");
    }

    // ───────────── Versions ─────────────

    [RelayCommand]
    private async Task ViewVersionAsync(CredentialVersionDto version)
    {
        string? secret = null;
        if (!await RunAsync(async () => secret = await credentials.RevealVersionAsync(version.Id)) || secret is null) return;
        await Ui.Dialogs.ShowAsync<bool>(new SecretViewDialogViewModel(Ui, $"Version {version.VersionNumber} du secret", secret,
            Ui.Vault.Current.SecretRevealSeconds));
    }

    [RelayCommand]
    private async Task RestoreVersionAsync(CredentialVersionDto version)
    {
        if (Detail is null) return;
        if (!await Ui.Dialogs.ConfirmAsync("Restaurer cette version ?",
                $"Le secret de la version {version.VersionNumber} redeviendra le secret courant de « {Detail.Summary.Name} ». " +
                "Une nouvelle version est créée : l'historique reste intact.", "Restaurer")) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () =>
        {
            await credentials.RestoreVersionAsync(id, version.Id);
            await QueryAsync();
        }, "Version restaurée.");
    }

    // ───────────── CRUD & liens ─────────────

    [RelayCommand]
    private async Task NewAsync()
    {
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<CredentialEditorDialogViewModel>(new CredentialEditorArgs(null, null)));
        if (id is null) return;
        _pendingSelection = id;
        await RunAsync(QueryAsync, "Identifiant créé.");
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Detail is null) return;
        var id = await Ui.Dialogs.ShowAsync<Guid?>(Ui.Create<CredentialEditorDialogViewModel>(new CredentialEditorArgs(Detail.Summary.Id, null)));
        if (id is null) return;
        _pendingSelection = id;
        await RunAsync(QueryAsync, "Identifiant enregistré.");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Detail is null) return;
        if (!await Ui.Dialogs.ConfirmAsync("Supprimer cet identifiant ?",
                $"« {Detail.Summary.Name} » et tout son historique de versions seront supprimés définitivement.", "Supprimer", danger: true)) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await credentials.DeleteAsync(id); await QueryAsync(); }, "Identifiant supprimé.");
    }

    [RelayCommand]
    private async Task LinkAssetAsync()
    {
        if (Detail is null) return;
        var picked = await PickItems.PickAssetAsync(Ui, "Associer une ressource", Detail.Assets.Select(a => a.Id));
        if (picked is null) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await credentials.LinkAssetAsync(id, picked.Id); await LoadDetailAsync(id); }, "Ressource associée.");
    }

    [RelayCommand]
    private async Task UnlinkAssetAsync(AssetSummaryDto asset)
    {
        if (Detail is null) return;
        var id = Detail.Summary.Id;
        await RunAsync(async () => { await credentials.UnlinkAssetAsync(id, asset.Id); await LoadDetailAsync(id); });
    }

    [RelayCommand] private void OpenAsset(AssetSummaryDto asset) => Ui.Navigator.Asset(asset.Id);
}

/// <summary>Éditeur d'identifiant avec générateur de mots de passe intégré.</summary>
public sealed partial class CredentialEditorDialogViewModel : DialogViewModel
{
    private readonly UiContext _ui;
    private readonly CredentialService _credentials;
    private readonly InfrastructureService _infrastructure;
    private readonly CredentialEditorArgs _args;
    private string? _originalSecret;

    public CredentialEditorDialogViewModel(UiContext ui, CredentialService credentials, InfrastructureService infrastructure, CredentialEditorArgs args)
    {
        _ui = ui;
        _credentials = credentials;
        _infrastructure = infrastructure;
        _args = args;
        IsNew = args.CredentialId is null;
        Title = IsNew ? "Nouvel identifiant" : "Modifier l'identifiant";
        TypeOptions = CredentialTypes.All.Select(t => new Option(t.Key, t.Label)).ToList();
        _selectedType = TypeOptions[0];
        var g = ui.Vault.Current.Generator;
        _length = g.Length;
        _useLower = g.Lowercase;
        _useUpper = g.Uppercase;
        _useDigits = g.Digits;
        _useSymbols = g.Symbols;
        _excludeAmbiguous = g.ExcludeAmbiguous;
        _ = TryAsync(LoadAsync);
    }

    public override double Width => 760;
    public bool IsNew { get; }
    public IReadOnlyList<Option> TypeOptions { get; }
    public ObservableCollection<PickItem> Assets { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsMultilineSecret)), NotifyPropertyChangedFor(nameof(SecretLabel))]
    private Option _selectedType;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SecretChanged))] private string _secret = "";
    [ObservableProperty] private bool _showSecret;
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _port = "";
    [ObservableProperty] private string _tags = "";
    [ObservableProperty] private string _expiresOn = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _changeReason = "";
    [ObservableProperty] private PasswordStrength _strength = PasswordStrengthEstimator.Estimate("");
    [ObservableProperty] private bool _isGeneratorOpen;

    // Générateur
    [ObservableProperty] private int _length;
    [ObservableProperty] private bool _useLower;
    [ObservableProperty] private bool _useUpper;
    [ObservableProperty] private bool _useDigits;
    [ObservableProperty] private bool _useSymbols;
    [ObservableProperty] private bool _excludeAmbiguous;
    [ObservableProperty] private string _entropyText = "";

    public bool IsMultilineSecret => CredentialTypes.IsMultiline(SelectedType.Value!);
    public string SecretLabel => SelectedType.Value switch
    {
        CredentialTypes.SshKey => "Clé privée",
        CredentialTypes.Certificate => "Certificat / clé (PEM)",
        CredentialTypes.ApiToken => "Jeton",
        _ => "Mot de passe",
    };
    public bool SecretChanged => !IsNew && _originalSecret is not null && Secret != _originalSecret;
    public double StrengthRatio => Strength.Score / 4.0;
    public string StrengthBrushKey => Strength.Score switch { 0 or 1 => "Danger", 2 => "Warning", _ => "Success" };

    partial void OnSecretChanged(string value)
    {
        Strength = PasswordStrengthEstimator.Estimate(value);
        OnPropertyChanged(nameof(StrengthRatio));
        OnPropertyChanged(nameof(StrengthBrushKey));
    }

    partial void OnLengthChanged(int value) => UpdateEntropy();
    partial void OnUseLowerChanged(bool value) => UpdateEntropy();
    partial void OnUseUpperChanged(bool value) => UpdateEntropy();
    partial void OnUseDigitsChanged(bool value) => UpdateEntropy();
    partial void OnUseSymbolsChanged(bool value) => UpdateEntropy();
    partial void OnExcludeAmbiguousChanged(bool value) => UpdateEntropy();

    private PasswordGeneratorOptions GeneratorOptions => new()
    {
        Length = Length, Lowercase = UseLower, Uppercase = UseUpper, Digits = UseDigits, Symbols = UseSymbols, ExcludeAmbiguous = ExcludeAmbiguous,
    };

    private void UpdateEntropy() => EntropyText = $"{Length} caractères · ≈ {PasswordGenerator.EntropyBits(GeneratorOptions):0} bits d'entropie";

    private async Task LoadAsync()
    {
        UpdateEntropy();
        if (_args.CredentialId is { } id)
        {
            var detail = await _credentials.GetAsync(id) ?? throw new Shared.Exceptions.NotFoundException("Identifiant");
            var s = detail.Summary;
            Name = s.Name;
            SelectedType = TypeOptions.FirstOrDefault(t => t.Value == s.Type) ?? TypeOptions[0];
            Url = s.Url ?? "";
            Port = s.Port?.ToString(CultureInfo.InvariantCulture) ?? "";
            Tags = s.Tags ?? "";
            ExpiresOn = s.ExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
            var edit = await _credentials.GetEditDataAsync(id);
            Username = edit.Username ?? "";
            Notes = edit.Notes ?? "";
            _originalSecret = await _credentials.RevealSecretAsync(id, SecretAccess.Internal);
            Secret = _originalSecret;
            foreach (var a in detail.Assets) Assets.Add(PickItems.From(a));
        }
        else if (_args.LinkAssetId is { } assetId)
        {
            var asset = await _infrastructure.GetAssetAsync(assetId);
            if (asset is not null)
            {
                Assets.Add(PickItems.From(asset.Summary));
                Url = asset.Summary.Hostname ?? asset.Summary.IpAddress ?? "";
            }
        }
    }

    [RelayCommand] private void ToggleGenerator() => IsGeneratorOpen = !IsGeneratorOpen;
    [RelayCommand] private void ToggleShow() => ShowSecret = !ShowSecret;

    [RelayCommand]
    private void Generate()
    {
        try
        {
            Error = null;
            Secret = PasswordGenerator.Generate(GeneratorOptions);
            ShowSecret = true;
        }
        catch (Shared.Exceptions.InfraVaultException ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void SetExpiry(string days)
        => ExpiresOn = days == "0" ? "" : DateTime.Today.AddDays(int.Parse(days, CultureInfo.InvariantCulture)).ToString("yyyy-MM-dd");

    [RelayCommand]
    private async Task AddAssetAsync()
    {
        var picked = await PickItems.PickAssetAsync(_ui, "Associer une ressource", Assets.Select(a => a.Id));
        if (picked is not null) Assets.Add(picked);
    }

    [RelayCommand] private void RemoveAsset(PickItem item) => Assets.Remove(item);

    [RelayCommand]
    private async Task SaveAsync()
    {
        int? port = null;
        if (!string.IsNullOrWhiteSpace(Port))
        {
            if (!int.TryParse(Port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var p)) { Error = "Le port doit être un nombre."; return; }
            port = p;
        }
        DateTime? expires = null;
        if (!string.IsNullOrWhiteSpace(ExpiresOn))
        {
            if (!DateTime.TryParseExact(ExpiresOn.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            {
                Error = "Date d'expiration invalide. Format attendu : AAAA-MM-JJ.";
                return;
            }
            expires = d.Date.AddHours(23).AddMinutes(59).ToUniversalTime();
        }

        Guid? saved = null;
        var ok = await TryAsync(async () => saved = await _credentials.SaveAsync(new CredentialInput
        {
            Id = _args.CredentialId,
            Name = Name,
            Type = SelectedType.Value!,
            Username = Username,
            Secret = IsNew || SecretChanged ? Secret : null,
            Notes = Notes,
            Url = Url,
            Port = port,
            Tags = Tags,
            ExpiresAt = expires,
            ChangeReason = ChangeReason,
            AssetIds = Assets.Select(a => a.Id).ToList(),
        }));
        if (!ok) return;
        Secret = "";
        _originalSecret = null;
        Close(saved);
    }
}
