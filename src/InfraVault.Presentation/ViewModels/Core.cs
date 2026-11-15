using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using InfraVault.Presentation.Services;
using InfraVault.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InfraVault.Presentation.ViewModels;

public interface INavigator
{
    void Dashboard();
    void Assets(string? typeKey = null);
    void Asset(Guid id);
    void Credentials(Guid? select = null);
    void Documents(Guid? select = null);
    void Search(string query);
    void Topology(Guid? scope = null);
    void Audit();
    void Backups();
    void Settings();
    void Back();
    void Open(string entityType, Guid id);
}

/// <summary>Services d'interface partagés par tous les ViewModels.</summary>
public sealed class UiContext(
    DialogService dialogs, ToastService toasts, IClipboardService clipboard, FileDialogService files,
    VaultService vault, IServiceProvider services, ILogger<UiContext> log)
{
    public DialogService Dialogs { get; } = dialogs;
    public ToastService Toasts { get; } = toasts;
    public IClipboardService Clipboard { get; } = clipboard;
    public FileDialogService Files { get; } = files;
    public VaultService Vault { get; } = vault;
    public ILogger Log { get; } = log;
    public INavigator Navigator { get; set; } = null!;

    public T Create<T>(params object[] args) => ActivatorUtilities.CreateInstance<T>(services, args);
    public T Get<T>() where T : notnull => services.GetRequiredService<T>();
}

public abstract partial class ViewModelBase(UiContext ui) : ObservableObject
{
    protected UiContext Ui { get; } = ui;

    [ObservableProperty] private bool _isBusy;

    /// <summary>Gestion centralisée des erreurs : aucune exception ne remonte jusqu'à l'interface.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? successMessage = null)
    {
        try
        {
            IsBusy = true;
            await action();
            if (successMessage is not null) Ui.Toasts.Success(successMessage);
            return true;
        }
        catch (VaultLockedException) { return false; }
        catch (OperationCanceledException) { return false; }
        catch (InfraVaultException ex)
        {
            Ui.Toasts.Error(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Ui.Log.LogError(ex, "Erreur inattendue dans {ViewModel}", GetType().Name);
            Ui.Toasts.Error("Une erreur inattendue s'est produite. Le détail est dans le journal de l'application.");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public abstract class PageViewModel(UiContext ui) : ViewModelBase(ui)
{
    public abstract string Title { get; }
    public virtual string NavKey => "";
    public virtual Task LoadAsync() => Task.CompletedTask;
    /// <summary>Appelé quand la page quitte l'écran : y effacer toute donnée déchiffrée.</summary>
    public virtual void OnLeave() { }
}

// ───────────────────────── Dialogues ─────────────────────────

public abstract partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<object?> _completion = new();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;

    public virtual double Width => 520;
    public Task<object?> Completion => _completion.Task;

    protected void Close(object? result) => _completion.TrySetResult(result);

    [RelayCommand]
    public void Cancel() => _completion.TrySetResult(null);

    /// <summary>Exécute une action ; une erreur métier s'affiche dans le dialogue sans le fermer.</summary>
    protected async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            Error = null;
            IsBusy = true;
            await action();
            return true;
        }
        catch (VaultLockedException) { Cancel(); return false; }
        catch (InfraVaultException ex) { Error = ex.Message; return false; }
        catch (Exception) { Error = "Une erreur inattendue s'est produite."; return false; }
        finally { IsBusy = false; }
    }
}

public sealed partial class ConfirmDialogViewModel : DialogViewModel
{
    public ConfirmDialogViewModel(string title, string message, string confirmLabel, bool danger)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        IsDanger = danger;
    }

    public string Message { get; }
    public string ConfirmLabel { get; }
    public bool IsDanger { get; }
    public override double Width => 460;

    [RelayCommand] private void Confirm() => Close(true);
}

public sealed partial class PromptDialogViewModel : DialogViewModel
{
    public PromptDialogViewModel(string title, string label, string? initial, bool isPassword, string confirmLabel, string? hint)
    {
        Title = title;
        Label = label;
        _value = initial ?? "";
        IsPassword = isPassword;
        ConfirmLabel = confirmLabel;
        Hint = hint;
    }

    public string Label { get; }
    public bool IsPassword { get; }
    public string ConfirmLabel { get; }
    public string? Hint { get; }
    public override double Width => 460;

    [ObservableProperty] private string _value;

    [RelayCommand] private void Confirm() => Close(Value);
}

public sealed record PickItem(Guid Id, string Title, string? Subtitle, string Icon, string? Color);

/// <summary>Sélecteur générique (ressource, identifiant, document) avec filtre.</summary>
public sealed partial class PickerDialogViewModel : DialogViewModel
{
    private readonly IReadOnlyList<PickItem> _all;

    public PickerDialogViewModel(string title, IEnumerable<PickItem> items, string emptyText = "Aucun élément disponible.")
    {
        Title = title;
        _all = items.ToList();
        EmptyText = emptyText;
        Items = new ObservableCollection<PickItem>(_all.Take(300));
    }

    public string EmptyText { get; }
    public ObservableCollection<PickItem> Items { get; }
    public bool IsEmpty => _all.Count == 0;

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private PickItem? _selected;

    partial void OnFilterChanged(string value)
    {
        Items.Clear();
        var text = value.Trim();
        foreach (var item in _all.Where(i => text.Length == 0
                     || i.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || (i.Subtitle?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)).Take(300))
            Items.Add(item);
    }

    [RelayCommand] private void Pick(PickItem? item) { if (item is not null) Close(item); }
    [RelayCommand] private void Confirm() { if (Selected is not null) Close(Selected); }
}

/// <summary>Affiche la clé de récupération une seule fois et exige une confirmation de conservation.</summary>
public sealed partial class RecoveryKeyDialogViewModel : DialogViewModel
{
    private readonly UiContext _ui;

    public RecoveryKeyDialogViewModel(UiContext ui, string recoveryKey, string vaultName)
    {
        _ui = ui;
        RecoveryKey = recoveryKey;
        VaultName = vaultName;
        Title = "Votre clé de récupération";
    }

    public string RecoveryKey { get; }
    public string VaultName { get; }
    public override double Width => 600;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(DoneCommand))] private bool _acknowledged;

    [RelayCommand]
    private void Copy()
    {
        _ui.Clipboard.CopySensitive(RecoveryKey, TimeSpan.FromSeconds(60));
        _ui.Toasts.Show("Clé copiée. Le presse-papiers sera vidé dans 60 secondes.");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var path = _ui.Files.Save("Enregistrer la clé de récupération", "Fichier texte (*.txt)|*.txt", "InfraVault - clé de récupération.txt");
        if (path is null) return;
        var text = $"InfraVault — clé de récupération\r\nCoffre : {VaultName}\r\nGénérée le : {DateTime.Now:dd/MM/yyyy HH:mm}\r\n\r\n{RecoveryKey}\r\n\r\n" +
                   "Conservez ce fichier hors de ce poste (clé USB chiffrée, coffre physique, impression).\r\n" +
                   "Cette clé permet de définir un nouveau mot de passe maître. Quiconque la détient peut ouvrir le coffre.\r\n";
        await System.IO.File.WriteAllTextAsync(path, text);
        _ui.Toasts.Success("Clé enregistrée. Déplacez ce fichier hors de ce poste.");
    }

    [RelayCommand(CanExecute = nameof(Acknowledged))]
    private void Done() => Close(true);
}

/// <summary>Affichage temporaire d'un secret (ancienne version, propriété sensible) avec fermeture automatique.</summary>
public sealed partial class SecretViewDialogViewModel : DialogViewModel
{
    private readonly UiContext _ui;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public SecretViewDialogViewModel(UiContext ui, string title, string secret, int seconds)
    {
        _ui = ui;
        Title = title;
        Secret = secret;
        _remaining = seconds;
        Total = seconds;
        _timer.Tick += (_, _) =>
        {
            Remaining--;
            if (Remaining <= 0) Cancel();
        };
        _timer.Start();
        Completion.ContinueWith(_ => _timer.Stop(), TaskScheduler.FromCurrentSynchronizationContext());
    }

    public string Secret { get; }
    public int Total { get; }
    [ObservableProperty] private int _remaining;

    [RelayCommand]
    private void Copy()
    {
        _ui.Clipboard.CopySensitive(Secret, TimeSpan.FromSeconds(_ui.Vault.Current.ClipboardClearSeconds));
        _ui.Toasts.Show($"Copié. Le presse-papiers sera vidé dans {_ui.Vault.Current.ClipboardClearSeconds} s.");
    }
}

public static class PickItems
{
    public static PickItem From(Application.DTOs.AssetSummaryDto a)
        => new(a.Id, a.Name, string.Join(" · ", new[] { a.TypeName, a.Hostname, a.IpAddress }.Where(s => !string.IsNullOrEmpty(s))), a.Icon, a.Color);

    public static PickItem From(Application.DTOs.CredentialSummaryDto c) => new(c.Id, c.Name, c.TypeLabel, c.Icon, "amber");
    public static PickItem From(Application.DTOs.DocumentSummaryDto d) => new(d.Id, d.Title, d.DocumentType, "file", "teal");

    public static async Task<PickItem?> PickAssetAsync(UiContext ui, string title, IEnumerable<Guid>? exclude = null)
    {
        var excluded = exclude?.ToHashSet() ?? [];
        var assets = await ui.Get<InfrastructureService>().ListAssetsAsync();
        return await ui.Dialogs.ShowAsync<PickItem>(new PickerDialogViewModel(title,
            assets.Where(a => !excluded.Contains(a.Id)).Select(From), "Aucune ressource disponible."));
    }
}
