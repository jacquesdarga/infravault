using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InfraVault.Application.Interfaces;
using InfraVault.Presentation.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace InfraVault.Presentation.Services;

// ───────────────────────── Préférences d'affichage (hors coffre, lisibles avant déverrouillage) ─────────────────────────

public sealed class Preferences
{
    public string Theme { get; set; } = "dark";
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 860;
    public bool Maximized { get; set; }
}

public sealed class PreferencesStore(IAppPaths paths, ILogger<PreferencesStore> logger)
{
    private string FilePath => Path.Combine(paths.DataDirectory, "preferences.json");
    public Preferences Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new Preferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Préférences illisibles, valeurs par défaut utilisées");
            Current = new Preferences();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Enregistrement des préférences impossible");
        }
    }
}

// ───────────────────────── Thème ─────────────────────────

public sealed partial class ThemeService(PreferencesStore preferences) : ObservableObject
{
    [ObservableProperty] private bool _isDark = true;

    public event EventHandler? ThemeChanged;

    public void Initialize() => Apply(preferences.Current.Theme != "light", persist: false);

    public void Toggle() => Apply(!IsDark, persist: true);

    public void Apply(bool dark, bool persist)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var source = new Uri($"pack://application:,,,/InfraVault;component/Resources/Themes/{(dark ? "Dark" : "Light")}.xaml");
        dictionaries[0] = new ResourceDictionary { Source = source };
        IsDark = dark;
        foreach (Window window in System.Windows.Application.Current.Windows) ApplyTitleBar(window);
        if (persist)
        {
            preferences.Current.Theme = dark ? "dark" : "light";
            preferences.Save();
        }
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Accorde la barre de titre Windows au thème (mode sombre et couleur de légende sous Windows 11).</summary>
    public void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        try
        {
            var dark = IsDark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var c = (Color)System.Windows.Application.Current.FindResource("C.Caption");
            var colorRef = c.R | (c.G << 8) | (c.B << 16);
            _ = DwmSetWindowAttribute(handle, 35, ref colorRef, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Versions de Windows sans ces attributs : barre de titre standard.
        }
    }
}

// ───────────────────────── Notifications ─────────────────────────

public enum ToastKind { Info, Success, Warning, Error }

public sealed record Toast(string Message, ToastKind Kind)
{
    public string Icon => Kind switch { ToastKind.Success => "check", ToastKind.Warning => "alert", ToastKind.Error => "alert", _ => "info" };
    public string BrushKey => Kind switch { ToastKind.Success => "Success", ToastKind.Warning => "Warning", ToastKind.Error => "Danger", _ => "Teal" };
}

public sealed class ToastService
{
    public ObservableCollection<Toast> Items { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        dispatcher.BeginInvoke(() =>
        {
            var toast = new Toast(message, kind);
            Items.Add(toast);
            while (Items.Count > 4) Items.RemoveAt(0);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 7 : 3.8) };
            timer.Tick += (_, _) => { timer.Stop(); Items.Remove(toast); };
            timer.Start();
        });
    }

    public void Success(string message) => Show(message, ToastKind.Success);
    public void Error(string message) => Show(message, ToastKind.Error);
    public void Warning(string message) => Show(message, ToastKind.Warning);
    public void Dismiss(Toast toast) => Items.Remove(toast);
}

// ───────────────────────── Dialogues (superposés dans la fenêtre) ─────────────────────────

public sealed class DialogService
{
    public ObservableCollection<DialogViewModel> Stack { get; } = [];

    public async Task<T?> ShowAsync<T>(DialogViewModel dialog)
    {
        Stack.Add(dialog);
        try
        {
            var result = await dialog.Completion;
            return result is T typed ? typed : default;
        }
        finally
        {
            Stack.Remove(dialog);
        }
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirmer", bool danger = false)
        => await ShowAsync<bool>(new ConfirmDialogViewModel(title, message, confirmLabel, danger));

    public Task<string?> PromptAsync(string title, string label, string? initial = null, bool isPassword = false,
        string confirmLabel = "Valider", string? hint = null)
        => ShowAsync<string>(new PromptDialogViewModel(title, label, initial, isPassword, confirmLabel, hint));

    /// <summary>Ferme tous les dialogues (au verrouillage : plus rien de sensible ne doit rester affiché).</summary>
    public void CloseAll()
    {
        foreach (var dialog in Stack.ToList()) dialog.Cancel();
        Stack.Clear();
    }
}

// ───────────────────────── Presse-papiers ─────────────────────────

/// <summary>
/// Copie un secret en demandant à Windows de l'exclure de l'historique du presse-papiers et de la
/// synchronisation cloud, puis l'efface après le délai configuré s'il s'y trouve encore.
/// </summary>
public sealed class ClipboardService(ILogger<ClipboardService> logger) : IClipboardService
{
    private byte[]? _ownedHash;
    private DispatcherTimer? _timer;

    public void CopySensitive(string text, TimeSpan clearAfter)
    {
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
            Clipboard.SetDataObject(data, copy: false);
            _ownedHash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = clearAfter };
            _timer.Tick += (_, _) => { _timer?.Stop(); ClearIfOwned(); };
            _timer.Start();
        }
        catch (COMException ex)
        {
            logger.LogWarning(ex, "Presse-papiers indisponible");
            throw new Shared.Exceptions.InfraVaultException("Le presse-papiers est momentanément indisponible. Réessayez.");
        }
    }

    public void CopyPlain(string text)
    {
        try { Clipboard.SetDataObject(text, copy: true); }
        catch (COMException ex) { logger.LogWarning(ex, "Presse-papiers indisponible"); }
    }

    public void ClearIfOwned()
    {
        _timer?.Stop();
        if (_ownedHash is null) return;
        try
        {
            if (Clipboard.ContainsText()
                && SHA256.HashData(Encoding.UTF8.GetBytes(Clipboard.GetText())).AsSpan().SequenceEqual(_ownedHash))
            {
                Clipboard.Clear();
            }
        }
        catch (COMException ex)
        {
            logger.LogWarning(ex, "Effacement du presse-papiers impossible");
        }
        _ownedHash = null;
    }
}

// ───────────────────────── Fichiers ─────────────────────────

public sealed class FileDialogService
{
    public string? Save(string title, string filter, string defaultName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? Open(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public void RevealInExplorer(string path)
    {
        try
        {
            var target = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // L'explorateur n'a pas pu s'ouvrir : sans gravité.
        }
    }
}
