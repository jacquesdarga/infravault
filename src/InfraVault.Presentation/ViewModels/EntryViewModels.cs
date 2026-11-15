using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Security;
using InfraVault.Application.Services;
using InfraVault.Application.UseCases;
using InfraVault.Presentation.Services;
using InfraVault.Shared.Exceptions;
using Microsoft.Extensions.Logging;

namespace InfraVault.Presentation.ViewModels;

/// <summary>Racine de l'interface : choisit l'écran (création, déverrouillage, application) selon l'état du coffre.</summary>
public sealed partial class AppViewModel : ObservableObject
{
    private readonly UiContext _ui;
    private readonly IVaultSession _session;
    private readonly IVaultDbFactory _dbFactory;
    private readonly AuthenticationService _authentication;
    private readonly ILogger<AppViewModel> _logger;

    public AppViewModel(UiContext ui, IVaultSession session, IVaultDbFactory dbFactory, AuthenticationService authentication,
        ThemeService theme, ILogger<AppViewModel> logger)
    {
        _ui = ui;
        _session = session;
        _dbFactory = dbFactory;
        _authentication = authentication;
        _logger = logger;
        Theme = theme;
        session.StateChanged += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(OnSessionChanged);
    }

    public DialogService Dialogs => _ui.Dialogs;
    public ToastService Toasts => _ui.Toasts;
    public ThemeService Theme { get; }

    [ObservableProperty] private object? _screen;

    public MainViewModel? Main => Screen as MainViewModel;

    [RelayCommand] private void DismissToast(Toast toast) => _ui.Toasts.Dismiss(toast);

    public async Task StartAsync()
    {
        string? startupError = null;
        try
        {
            await _dbFactory.InitializeAsync();
        }
        catch (InfraVaultException ex)
        {
            _logger.LogError(ex, "Initialisation de la base impossible");
            startupError = ex.Message;
        }
        await ShowEntryAsync(startupError);
    }

    public async Task ShowEntryAsync(string? message = null)
    {
        var exists = false;
        try { exists = message is null && await _authentication.VaultExistsAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Lecture du coffre impossible"); message ??= "La base locale est illisible. Restaurez une sauvegarde."; }

        if (exists)
        {
            var unlock = _ui.Create<UnlockViewModel>(this);
            await unlock.LoadAsync();
            Screen = unlock;
        }
        else
        {
            var create = _ui.Create<CreateVaultViewModel>(this);
            create.Error = message;
            Screen = create;
        }
    }

    private async void OnSessionChanged()
    {
        if (_session.IsUnlocked)
        {
            if (Screen is MainViewModel) return;
            var main = _ui.Create<MainViewModel>();
            Screen = main;
            await main.StartAsync();
        }
        else
        {
            _ui.Dialogs.CloseAll();
            _ui.Clipboard.ClearIfOwned();
            (Screen as MainViewModel)?.Dispose();
            await ShowEntryAsync();
        }
    }
}

public sealed partial class CreateVaultViewModel(
    UiContext ui, AppViewModel app, AuthenticationService authentication, DemoDataSeeder seeder, BackupService backup,
    IVaultDbFactory dbFactory) : ViewModelBase(ui)
{
    [ObservableProperty] private string _vaultName = "Mon infrastructure";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmation = "";
    [ObservableProperty] private bool _seedDemo = true;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private PasswordStrength _strength = PasswordStrengthEstimator.Estimate("");
    [ObservableProperty] private string _busyText = "";

    public double StrengthRatio => Strength.Score / 4.0;
    public string StrengthBrushKey => Strength.Score switch { 0 or 1 => "Danger", 2 => "Warning", _ => "Success" };

    partial void OnPasswordChanged(string value)
    {
        Strength = PasswordStrengthEstimator.Estimate(value);
        OnPropertyChanged(nameof(StrengthRatio));
        OnPropertyChanged(nameof(StrengthBrushKey));
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        Error = null;
        if (Password != Confirmation) { Error = "La confirmation ne correspond pas au mot de passe maître."; return; }
        try
        {
            IsBusy = true;
            BusyText = "Dérivation de la clé (Argon2id) et création du coffre…";
            var result = await authentication.CreateVaultAsync(VaultName, Password);
            Password = Confirmation = "";
            if (SeedDemo)
            {
                BusyText = "Chargement de l'infrastructure d'exemple…";
                await seeder.SeedAsync();
                if (app.Main is { } main) await main.ReloadAsync();
            }
            await Ui.Dialogs.ShowAsync<bool>(new RecoveryKeyDialogViewModel(Ui, result.RecoveryKey, VaultName));
        }
        catch (InfraVaultException ex) { Error = ex.Message; }
        catch (Exception ex)
        {
            Ui.Log.LogError(ex, "Création du coffre impossible");
            Error = "La création du coffre a échoué. Le détail est dans le journal de l'application.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var path = Ui.Files.Open("Choisir une sauvegarde InfraVault", "Sauvegarde InfraVault (*.ivbak)|*.ivbak");
        if (path is null) return;
        var dialog = new RestoreDialogViewModel(System.IO.Path.GetFileName(path));
        var secret = await Ui.Dialogs.ShowAsync<RestoreSecret>(dialog);
        if (secret is null) return;
        Error = null;
        await RunAsync(async () =>
        {
            BusyText = "Déchiffrement et restauration de la sauvegarde…";
            await dbFactory.InitializeAsync();
            await backup.RestoreAsync(path, secret.Secret, secret.IsRecoveryKey);
            Ui.Toasts.Success("Sauvegarde restaurée. Déverrouillez le coffre avec son mot de passe maître.");
            await app.ShowEntryAsync();
        });
    }
}

public sealed record RestoreSecret(string Secret, bool IsRecoveryKey);

public sealed partial class RestoreDialogViewModel : DialogViewModel
{
    public RestoreDialogViewModel(string fileName)
    {
        Title = "Restaurer une sauvegarde";
        FileName = fileName;
    }

    public string FileName { get; }
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private bool _useRecoveryKey;

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(Secret)) { Error = "Saisissez le mot de passe maître de la sauvegarde."; return; }
        Close(new RestoreSecret(Secret, UseRecoveryKey));
    }
}

public sealed partial class UnlockViewModel(
    UiContext ui, AppViewModel app, AuthenticationService authentication, BackupService backup) : ViewModelBase(ui)
{
    [ObservableProperty] private string _vaultName = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isRecoveryMode;
    [ObservableProperty] private string _recoveryKey = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmation = "";
    [ObservableProperty] private bool _hasRecoveryKey;

    public async Task LoadAsync()
    {
        var info = await authentication.GetVaultInfoAsync();
        VaultName = info?.Name ?? "Coffre";
        HasRecoveryKey = info?.HasRecoveryKey ?? false;
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (IsBusy) return;
        Error = null;
        if (string.IsNullOrEmpty(Password)) { Error = "Saisissez votre mot de passe maître."; return; }
        try
        {
            IsBusy = true;
            await authentication.UnlockAsync(Password);
            Password = "";
        }
        catch (InfraVaultException ex)
        {
            Error = ex.Message;
            Password = "";
        }
        catch (Exception ex)
        {
            Ui.Log.LogError(ex, "Déverrouillage impossible");
            Error = "Le déverrouillage a échoué. Le détail est dans le journal de l'application.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ToggleRecovery()
    {
        IsRecoveryMode = !IsRecoveryMode;
        Error = null;
        Password = RecoveryKey = NewPassword = Confirmation = "";
    }

    [RelayCommand]
    private async Task RecoverAsync()
    {
        if (IsBusy) return;
        Error = null;
        if (NewPassword != Confirmation) { Error = "La confirmation ne correspond pas au nouveau mot de passe."; return; }
        try
        {
            IsBusy = true;
            var newKey = await authentication.RecoverAsync(RecoveryKey, NewPassword);
            var name = VaultName;
            RecoveryKey = NewPassword = Confirmation = "";
            Ui.Toasts.Success("Coffre récupéré. Un nouveau mot de passe maître est en vigueur.");
            await Ui.Dialogs.ShowAsync<bool>(new RecoveryKeyDialogViewModel(Ui, newKey, name)
            {
                Title = "Nouvelle clé de récupération",
            });
        }
        catch (InfraVaultException ex) { Error = ex.Message; }
        catch (Exception ex)
        {
            Ui.Log.LogError(ex, "Récupération impossible");
            Error = "La récupération a échoué. Le détail est dans le journal de l'application.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var directory = await backup.GetBackupDirectoryAsync();
        var path = Ui.Files.Open("Choisir une sauvegarde InfraVault", "Sauvegarde InfraVault (*.ivbak)|*.ivbak", directory);
        if (path is null) return;
        var secret = await Ui.Dialogs.ShowAsync<RestoreSecret>(new RestoreDialogViewModel(System.IO.Path.GetFileName(path)));
        if (secret is null) return;
        if (!await Ui.Dialogs.ConfirmAsync("Remplacer le coffre actuel ?",
                "Le coffre présent sur ce poste sera remplacé par le contenu de la sauvegarde. L'ancien fichier est conservé à côté, par précaution.",
                "Restaurer", danger: true)) return;
        Error = null;
        await RunAsync(async () =>
        {
            await backup.RestoreAsync(path, secret.Secret, secret.IsRecoveryKey);
            Ui.Toasts.Success("Sauvegarde restaurée. Déverrouillez le coffre avec son mot de passe maître.");
            await app.ShowEntryAsync();
        });
    }
}
