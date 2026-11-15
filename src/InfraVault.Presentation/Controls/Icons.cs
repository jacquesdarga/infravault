using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InfraVault.Presentation.Controls;

/// <summary>
/// Jeu d'icônes vectorielles de l'application (grille 24 × 24, tracé au trait).
/// Dessinées pour InfraVault : aucune police ni ressource externe, donc rendu identique hors ligne.
/// </summary>
public static class Icons
{
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Rectangle à coins arrondis.</summary>
    private static string RR(double x, double y, double w, double h, double r)
        => $"M{N(x + r)} {N(y)}h{N(w - 2 * r)}a{N(r)} {N(r)} 0 0 1 {N(r)} {N(r)}v{N(h - 2 * r)}a{N(r)} {N(r)} 0 0 1 {N(-r)} {N(r)}h{N(-(w - 2 * r))}a{N(r)} {N(r)} 0 0 1 {N(-r)} {N(-r)}v{N(-(h - 2 * r))}a{N(r)} {N(r)} 0 0 1 {N(r)} {N(-r)}z ";

    private static string C(double cx, double cy, double r)
        => $"M{N(cx - r)} {N(cy)}a{N(r)} {N(r)} 0 1 0 {N(2 * r)} 0a{N(r)} {N(r)} 0 1 0 {N(-2 * r)} 0z ";

    private static string Dot(double x, double y) => $"M{N(x)} {N(y)}h.01 ";

    private static readonly Dictionary<string, string> Paths = new()
    {
        // Infrastructure
        ["server"] = RR(3, 3, 18, 7, 2) + RR(3, 14, 18, 7, 2) + Dot(7, 6.5) + Dot(7, 17.5),
        ["layers"] = "M12 3 21 8 12 13 3 8z M3 12.5 12 17.5 21 12.5 M3 17 12 22 21 17",
        ["monitor"] = RR(2, 4, 20, 13, 2) + "M8 21h8 M12 17v4",
        ["boxes"] = RR(3, 13, 8, 8, 1.5) + RR(13, 13, 8, 8, 1.5) + RR(8, 3, 8, 8, 1.5),
        ["box"] = "M21 8 12 3 3 8v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8",
        ["stack"] = RR(4, 3.5, 16, 4.5, 1.2) + RR(4, 9.75, 16, 4.5, 1.2) + RR(4, 16, 16, 4.5, 1.2),
        ["network"] = C(12, 5, 2.5) + C(5, 19, 2.5) + C(19, 19, 2.5) + "M12 7.5v4.5 M5 16.5V14a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v2.5",
        ["drive"] = "M3 13 5.6 5.4A2 2 0 0 1 7.5 4h9a2 2 0 0 1 1.9 1.4L21 13 " + RR(3, 13, 18, 7, 2) + Dot(7, 16.5) + Dot(11, 16.5),
        ["database"] = "M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3z M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6 M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3",
        ["app"] = RR(3, 4, 18, 16, 2) + "M3 9h18 " + Dot(6.5, 6.5) + Dot(9.5, 6.5),
        ["globe"] = C(12, 12, 9) + "M3 12h18 M12 3c2.5 2.6 3.8 5.7 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.7-3.8-9s1.3-6.4 3.8-9z",
        ["cloud"] = "M7 19a4.5 4.5 0 0 1-.7-8.95A6 6 0 0 1 18 10.6 4.2 4.2 0 0 1 17.5 19z",
        ["cube"] = "M12 3 20 7.5v9L12 21l-8-4.5v-9z M12 12l8-4.5 M12 12v9 M12 12 4 7.5",
        ["building"] = RR(4, 3, 10, 18, 1) + "M14 9h5a1 1 0 0 1 1 1v11h-6 M8 7h2 M8 11h2 M8 15h2 M2 21h20",
        ["firewall"] = RR(3, 4, 18, 16, 1.5) + "M3 9.5h18 M3 14.5h18 M9 4v5.5 M15 4v5.5 M6 9.5v5 M12 9.5v5 M18 9.5v5 M9 14.5V20 M15 14.5V20",
        ["router"] = RR(3, 13, 18, 7, 2) + Dot(7, 16.5) + Dot(11, 16.5) + "M17 13V9 M14.2 7a4 4 0 0 1 5.6 0 M12 4.6a7.2 7.2 0 0 1 10 0",
        ["switch"] = RR(2, 8, 20, 8, 2) + Dot(6, 12) + Dot(10, 12) + Dot(14, 12) + Dot(18, 12),
        ["cpu"] = RR(6, 6, 12, 12, 2) + RR(9.5, 9.5, 5, 5, .6) + "M9 3v3 M15 3v3 M9 18v3 M15 18v3 M3 9h3 M3 15h3 M18 9h3 M18 15h3",
        ["wifi"] = "M3 9a13 13 0 0 1 18 0 M6 12.5a8.5 8.5 0 0 1 12 0 M9 16a4.2 4.2 0 0 1 6 0 " + Dot(12, 19.5),
        ["git"] = C(6, 5.5, 2.5) + C(6, 18.5, 2.5) + C(18, 8, 2.5) + "M6 8v8 M18 10.5c0 3.5-3.5 4.5-6.5 4.8-2.6.3-5.5.2-5.5.7",
        ["zap"] = "M13 2 4 14h7l-1 8 9-12h-7z",
        ["mail"] = RR(3, 5, 18, 14, 2) + "M3.5 7.5l8.5 6 8.5-6",
        ["code"] = "M8 7l-5 5 5 5 M16 7l5 5-5 5 M13.5 5l-3 14",
        ["terminal"] = RR(3, 4, 18, 16, 2) + "M7 9l3 3-3 3 M12.5 15H17",

        // Sécurité
        ["shield"] = "M12 3l8 3v6c0 4.5-3.2 8-8 9-4.8-1-8-4.5-8-9V6z",
        ["shield-check"] = "M12 3l8 3v6c0 4.5-3.2 8-8 9-4.8-1-8-4.5-8-9V6z M8.5 12l2.5 2.5 4.5-5",
        ["key"] = C(8, 15, 4) + "M11 12l9-9 M16.5 6.5l3 3 M13.5 9.5l2 2",
        ["lock"] = RR(5, 11, 14, 10, 2) + "M8 11V7.5a4 4 0 0 1 8 0V11",
        ["unlock"] = RR(5, 11, 14, 10, 2) + "M8 11V7.5a4 4 0 0 1 7.6-1.7",
        ["fingerprint"] = "M12 11v4a6 6 0 0 0 1 3.3 M8.5 17A9 9 0 0 1 8 13.5V11a4 4 0 0 1 8 0v2.5c0 1.8.4 3.4 1.2 4.7 M5 13.5V11a7 7 0 0 1 14 0v1.5",
        ["certificate"] = RR(3, 4, 18, 12, 2) + "M7 8.5h7 M7 12h4 " + C(16.5, 15.5, 2.5) + "M15 17.6V22l1.5-1 1.5 1v-4.4",
        ["token"] = "M12 3l7.8 4.5v9L12 21l-7.8-4.5v-9z " + C(12, 12, 2.6),
        ["crown"] = "M3 8l4.5 4.5L12 5l4.5 7.5L21 8l-2 10H5z",
        ["user"] = C(12, 8, 4) + "M4 21a8 8 0 0 1 16 0",

        // Interface
        ["dashboard"] = RR(3, 3, 8, 8, 1.5) + RR(13, 3, 8, 5, 1.5) + RR(13, 11, 8, 10, 1.5) + RR(3, 14, 8, 7, 1.5),
        ["map"] = "M9 4 3 6.5V20l6-2.5 6 2.5 6-2.5V4l-6 2.5z M9 4v13.5 M15 6.5V20",
        ["file"] = "M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5 M9 13h6 M9 17h6",
        ["folder"] = "M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        ["search"] = C(11, 11, 7) + "M20 20l-4-4",
        ["plus"] = "M12 5v14 M5 12h14",
        ["x"] = "M6 6l12 12 M18 6 6 18",
        ["check"] = "M5 12.5l4.5 4.5L19 7.5",
        ["chevron-right"] = "M9 6l6 6-6 6",
        ["chevron-down"] = "M6 9l6 6 6-6",
        ["chevron-left"] = "M15 6l-6 6 6 6",
        ["arrow-right"] = "M5 12h14 M13 6l6 6-6 6",
        ["edit"] = "M4 20h4L19 9l-4-4L4 16z M13.5 6.5l4 4",
        ["trash"] = "M4 7h16 M9 7V4h6v3 M6 7l1 13h10l1-13 M10 11v5 M14 11v5",
        ["copy"] = RR(9, 9, 11, 11, 2) + "M15 5.5V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h.5",
        ["eye"] = "M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z " + C(12, 12, 3),
        ["eye-off"] = "M3 3l18 18 M10.6 5.1A9.7 9.7 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3 3.9 M6.5 6.6A16.6 16.6 0 0 0 2 12s3.6 7 10 7a9.6 9.6 0 0 0 4.6-1.2 M9.9 9.9a3 3 0 0 0 4.2 4.2",
        ["refresh"] = "M20 11a8 8 0 0 0-14.3-4.3L4 8.5 M4 4v4.5h4.5 M4 13a8 8 0 0 0 14.3 4.3L20 15.5 M20 20v-4.5h-4.5",
        ["history"] = C(12, 12, 9) + "M12 7v5l3.5 2",
        ["settings"] = "M4 6h9 M17 6h3 M4 12h3 M11 12h9 M4 18h11 M19 18h1 " + C(15, 6, 2) + C(9, 12, 2) + C(17, 18, 2),
        ["archive"] = RR(3, 4, 18, 5, 1) + "M5 9v9a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V9 M10 13h4",
        ["link"] = "M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1 1 M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1-1",
        ["download"] = "M12 4v11 M7 10.5l5 5 5-5 M5 20h14",
        ["upload"] = "M12 16V5 M7 9.5l5-5 5 5 M5 20h14",
        ["sun"] = C(12, 12, 4) + "M12 2v2.5 M12 19.5V22 M2 12h2.5 M19.5 12H22 M5 5l1.8 1.8 M17.2 17.2 19 19 M5 19l1.8-1.8 M17.2 6.8 19 5",
        ["moon"] = "M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z",
        ["alert"] = "M12 3 2.5 20h19z M12 10v4.5 " + Dot(12, 17.3),
        ["info"] = C(12, 12, 9) + "M12 11v6 " + Dot(12, 7.6),
        ["tag"] = "M3 12V4h8l10 10-8 8z " + Dot(7.5, 8.5),
        ["wand"] = "M5 19 16 8 M15 3.5v3 M13.5 5h3 M19.5 10v3 M18 11.5h3 M8 4v2 M7 5h2",
        ["paperclip"] = "M20 11.5l-8.5 8.5a5 5 0 0 1-7-7L13 4.5a3.4 3.4 0 0 1 4.8 4.8l-8.3 8.3a1.7 1.7 0 0 1-2.4-2.4L15 7.5",
        ["external"] = "M14 4h6v6 M20 4l-9 9 M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4",
        ["image"] = RR(3, 4, 18, 16, 2) + C(8.5, 9.5, 1.5) + "M21 16l-5-5-9 9",
        ["more"] = Dot(5, 12) + Dot(12, 12) + Dot(19, 12),
        ["activity"] = "M3 12h4l3-8 4 16 3-8h4",
        ["home"] = "M4 11l8-7 8 7v9h-5v-6h-6v6H4z",
    };

    private static readonly Dictionary<string, Geometry> Cache = [];

    public static IReadOnlyCollection<string> Names => Paths.Keys;

    /// <summary>Catégories proposées dans le sélecteur d'icônes.</summary>
    public static IReadOnlyList<(string Category, string Icon, string[] Icons)> PickerCategories { get; } =
    [
        ("Serveurs", "server", ["server", "layers", "cpu", "building", "drive", "stack"]),
        ("Bases", "database", ["database", "drive", "archive", "stack"]),
        ("Réseau", "network", ["network", "router", "switch", "firewall", "wifi", "link"]),
        ("Sécurité", "shield", ["shield", "shield-check", "lock", "key", "certificate", "fingerprint", "token"]),
        ("Systèmes", "monitor", ["monitor", "terminal", "cpu", "app", "code"]),
        ("Conteneurs", "box", ["box", "boxes", "cube", "stack", "layers"]),
        ("Cloud", "cloud", ["cloud", "globe", "zap", "upload"]),
        ("Web", "globe", ["globe", "app", "mail", "git", "code", "external"]),
    ];

    public static IReadOnlyList<string> PickerColors { get; } = ["sky", "teal", "coral", "violet", "amber", "rose", "lime", "slate"];

    public static Geometry Get(string? name)
    {
        name = string.IsNullOrEmpty(name) || !Paths.ContainsKey(name) ? "cube" : name;
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var g)) return g;
            g = Geometry.Parse(Paths[name]);
            g.Freeze();
            return Cache[name] = g;
        }
    }
}

/// <summary>Icône au trait. <see cref="Tint"/> lie la couleur à une couleur nommée du thème (« teal », « coral »…).</summary>
public class IconView : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(IconView),
        new PropertyMetadata(null, (d, e) => d.SetValue(GeometryProperty, Icons.Get(e.NewValue as string))));

    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry), typeof(Geometry), typeof(IconView), new PropertyMetadata(Icons.Get("cube")));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(IconView), new PropertyMetadata(1.8));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(string), typeof(IconView), new PropertyMetadata(null, OnTintChanged));

    public string? Kind { get => (string?)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Geometry Geometry { get => (Geometry)GetValue(GeometryProperty); set => SetValue(GeometryProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public string? Tint { get => (string?)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    private static void OnTintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (IconView)d;
        if (e.NewValue is string { Length: > 0 } name) icon.SetResourceReference(ForegroundProperty, ThemeColors.Key(name));
        else icon.ClearValue(ForegroundProperty);
    }
}

public static class ThemeColors
{
    private static readonly HashSet<string> Known = ["amber", "teal", "coral", "sky", "violet", "lime", "rose", "slate"];
    public static string Key(string? name) => "Asset." + (name is not null && Known.Contains(name) ? name : "slate");

    public static Brush Brush(string? name) => (Brush)System.Windows.Application.Current.FindResource(Key(name));
    public static Brush Resource(string key) => (Brush)System.Windows.Application.Current.FindResource(key);
    public static Color ColorOf(string key) => ((SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;
}

/// <summary>Propriétés attachées pour teinter un élément avec une couleur nommée du thème (suit le changement de thème).</summary>
public static class Tinted
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
        "Background", typeof(string), typeof(Tinted), new PropertyMetadata(null, (d, e) => Apply(d, e.NewValue as string, true)));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground", typeof(string), typeof(Tinted), new PropertyMetadata(null, (d, e) => Apply(d, e.NewValue as string, false)));

    /// <summary>Lie le premier plan à une clé de pinceau du thème (« Success », « Danger »…).</summary>
    public static readonly DependencyProperty ForegroundKeyProperty = DependencyProperty.RegisterAttached(
        "ForegroundKey", typeof(string), typeof(Tinted), new PropertyMetadata(null, (d, e) => ApplyKey(d, e.NewValue as string, false)));
    public static readonly DependencyProperty BackgroundKeyProperty = DependencyProperty.RegisterAttached(
        "BackgroundKey", typeof(string), typeof(Tinted), new PropertyMetadata(null, (d, e) => ApplyKey(d, e.NewValue as string, true)));
    public static void SetForegroundKey(DependencyObject d, string? value) => d.SetValue(ForegroundKeyProperty, value);
    public static string? GetForegroundKey(DependencyObject d) => (string?)d.GetValue(ForegroundKeyProperty);
    public static void SetBackgroundKey(DependencyObject d, string? value) => d.SetValue(BackgroundKeyProperty, value);
    public static string? GetBackgroundKey(DependencyObject d) => (string?)d.GetValue(BackgroundKeyProperty);

    private static void ApplyKey(DependencyObject d, string? key, bool background)
    {
        if (d is not FrameworkElement fe || string.IsNullOrEmpty(key)) return;
        if (Target(fe, background) is { } property) fe.SetResourceReference(property, key);
    }

    private static DependencyProperty? Target(FrameworkElement fe, bool background) => (fe, background) switch
    {
        (Border, true) => Border.BackgroundProperty,
        (Panel, true) => Panel.BackgroundProperty,
        (Control, true) => Control.BackgroundProperty,
        (System.Windows.Shapes.Shape, _) => System.Windows.Shapes.Shape.FillProperty,
        (TextBlock, false) => TextBlock.ForegroundProperty,
        (Control, false) => Control.ForegroundProperty,
        _ => null,
    };

    public static void SetBackground(DependencyObject d, string? value) => d.SetValue(BackgroundProperty, value);
    public static string? GetBackground(DependencyObject d) => (string?)d.GetValue(BackgroundProperty);
    public static void SetForeground(DependencyObject d, string? value) => d.SetValue(ForegroundProperty, value);
    public static string? GetForeground(DependencyObject d) => (string?)d.GetValue(ForegroundProperty);

    private static void Apply(DependencyObject d, string? name, bool background)
    {
        if (d is not FrameworkElement fe || string.IsNullOrEmpty(name)) return;
        var property = Target(fe, background);
        if (property is not null) fe.SetResourceReference(property, ThemeColors.Key(name));
    }
}
