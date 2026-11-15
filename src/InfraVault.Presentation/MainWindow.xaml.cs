using System.Windows;
using System.Windows.Input;
using InfraVault.Presentation.Services;
using InfraVault.Presentation.ViewModels;

namespace InfraVault.Presentation;

public partial class MainWindow : Window
{
    private readonly AppViewModel _app;
    private readonly ThemeService _theme;
    private readonly PreferencesStore _preferences;

    public MainWindow(AppViewModel app, ThemeService theme, PreferencesStore preferences)
    {
        _app = app;
        _theme = theme;
        _preferences = preferences;
        InitializeComponent();
        DataContext = app;

        var p = preferences.Current;
        Width = Math.Clamp(p.WindowWidth, MinWidth, SystemParameters.VirtualScreenWidth);
        Height = Math.Clamp(p.WindowHeight, MinHeight, SystemParameters.VirtualScreenHeight);
        if (p.Maximized) WindowState = WindowState.Maximized;

        SourceInitialized += (_, _) => _theme.ApplyTitleBar(this);
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, _) =>
        {
            _preferences.Current.Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _preferences.Current.WindowWidth = Width;
                _preferences.Current.WindowHeight = Height;
            }
            _preferences.Save();
        };
    }

    /// <summary>Raccourcis globaux : Ctrl+K recherche, Ctrl+L verrouille, Ctrl+N nouvelle ressource, Alt+Gauche retour.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_app.Main is not { } main || _app.Dialogs.Stack.Count > 0) return;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ctrl && key is Key.K or Key.F && ctrl)
        {
            if (FindSearchBox() is { } box) { box.Focus(); box.SelectAll(); }
            e.Handled = true;
        }
        else if (ctrl && key == Key.L) { main.LockCommand.Execute(null); e.Handled = true; }
        else if (ctrl && key == Key.N) { main.NewAssetCommand.Execute(null); e.Handled = true; }
        else if (ctrl && key == Key.D1) { main.Dashboard(); e.Handled = true; }
        else if (ctrl && key == Key.D2) { main.Assets(); e.Handled = true; }
        else if (ctrl && key == Key.D3) { main.Topology(); e.Handled = true; }
        else if (ctrl && key == Key.D4) { main.Credentials(); e.Handled = true; }
        else if (ctrl && key == Key.D5) { main.Documents(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Left) { main.BackCommand.Execute(null); e.Handled = true; }
    }

    private System.Windows.Controls.TextBox? FindSearchBox() => Find(this);

    private static System.Windows.Controls.TextBox? Find(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.TextBox { Name: "GlobalSearch" } box) return box;
            if (Find(child) is { } found) return found;
        }
        return null;
    }
}
