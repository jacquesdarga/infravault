using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using InfraVault.Domain.Enums;
using InfraVault.Shared.Constants;

namespace InfraVault.Presentation.Controls;

/// <summary>Convertit bool / null / chaîne vide / nombre en visibilité. Paramètre « ! » pour inverser.</summary>
public sealed class VisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i > 0,
            long l => l > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter is "!") visible = !visible;
        if (parameter is "bool") return visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Vrai si la valeur est égale au paramètre (boutons radio liés à une valeur).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equal = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
        return targetType == typeof(Visibility) ? (equal ? Visibility.Visible : Visibility.Collapsed) : equal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter! : Binding.DoNothing;
}

/// <summary>Date UTC → texte local. Paramètre : « date », « relative » ou vide (date et heure).</summary>
public sealed class DateConverter : IValueConverter
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc) return "—";
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return parameter switch
        {
            "date" => local.ToString("d MMM yyyy", Fr),
            "relative" => Relative(local),
            _ => local.ToString("d MMM yyyy · HH:mm", Fr),
        };
    }

    public static string Relative(DateTime local)
    {
        var delta = DateTime.Now - local;
        if (delta < TimeSpan.Zero)
        {
            var ahead = -delta;
            return ahead.TotalDays >= 1 ? $"dans {Math.Ceiling(ahead.TotalDays)} j" : $"dans {Math.Ceiling(ahead.TotalHours)} h";
        }
        if (delta.TotalSeconds < 60) return "à l'instant";
        if (delta.TotalMinutes < 60) return $"il y a {(int)delta.TotalMinutes} min";
        if (delta.TotalHours < 24) return $"il y a {(int)delta.TotalHours} h";
        if (delta.TotalDays < 30) return $"il y a {(int)delta.TotalDays} j";
        return local.ToString("d MMM yyyy", Fr);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        string[] units = ["o", "Ko", "Mo", "Go"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes:0.#} {units[i]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Statut, environnement ou expiration → libellé français ou pinceau du thème (paramètre « brush » / « soft »).</summary>
public sealed class StateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (label, key) = value switch
        {
            AssetStatuses.Active => ("Actif", "Success"),
            AssetStatuses.Maintenance => ("Maintenance", "Warning"),
            AssetStatuses.Retired => ("Retiré", "TextFaint"),
            AssetStatuses.Unknown => ("Inconnu", "TextFaint"),
            ExpiryState.Expired => ("Expiré", "Danger"),
            ExpiryState.ExpiringSoon => ("Expire bientôt", "Warning"),
            ExpiryState.Valid => ("Valide", "Success"),
            ExpiryState.None => ("Sans expiration", "TextFaint"),
            true => ("Succès", "Success"),
            false => ("Échec", "Danger"),
            _ => (value?.ToString() ?? "", "TextMuted"),
        };
        return parameter switch
        {
            "brush" => System.Windows.Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray,
            "soft" => System.Windows.Application.Current.TryFindResource(key + "Soft") as Brush ?? System.Windows.Application.Current.TryFindResource("SurfaceAlt") as Brush ?? Brushes.Transparent,
            _ => label,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Multiplie une valeur (0..1) par le paramètre : largeur de barre proportionnelle.</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture)
           * System.Convert.ToDouble(parameter ?? 1, CultureInfo.InvariantCulture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Liaison bidirectionnelle du mot de passe d'un <see cref="PasswordBox"/> et focus initial.</summary>
public static class Assist
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(Assist),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(Assist), new PropertyMetadata(false));

    public static readonly DependencyProperty BindPasswordProperty = DependencyProperty.RegisterAttached(
        "BindPassword", typeof(bool), typeof(Assist), new PropertyMetadata(false, OnBindChanged));

    public static readonly DependencyProperty FocusOnLoadProperty = DependencyProperty.RegisterAttached(
        "FocusOnLoad", typeof(bool), typeof(Assist), new PropertyMetadata(false, OnFocusOnLoadChanged));

    /// <summary>Exécute la commande à l'appui sur Entrée.</summary>
    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand", typeof(System.Windows.Input.ICommand), typeof(Assist), new PropertyMetadata(null, OnEnterCommandChanged));

    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);
    public static bool GetBindPassword(DependencyObject d) => (bool)d.GetValue(BindPasswordProperty);
    public static void SetBindPassword(DependencyObject d, bool value) => d.SetValue(BindPasswordProperty, value);
    public static bool GetFocusOnLoad(DependencyObject d) => (bool)d.GetValue(FocusOnLoadProperty);
    public static void SetFocusOnLoad(DependencyObject d, bool value) => d.SetValue(FocusOnLoadProperty, value);
    public static System.Windows.Input.ICommand? GetEnterCommand(DependencyObject d) => (System.Windows.Input.ICommand?)d.GetValue(EnterCommandProperty);
    public static void SetEnterCommand(DependencyObject d, System.Windows.Input.ICommand? value) => d.SetValue(EnterCommandProperty, value);

    private static void OnBindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box || (bool)box.GetValue(HookedProperty)) return;
        box.SetValue(HookedProperty, true);
        box.PasswordChanged += (_, _) =>
        {
            if (GetPassword(box) != box.Password) SetPassword(box, box.Password);
        };
    }

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box && box.Password != (string?)e.NewValue) box.Password = (string?)e.NewValue ?? "";
    }

    private static void OnFocusOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || e.NewValue is not true) return;
        fe.Loaded += (_, _) => fe.Dispatcher.BeginInvoke(() =>
        {
            fe.Focus();
            System.Windows.Input.Keyboard.Focus(fe);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void OnEnterCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.OldValue is not null) return;
        element.KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter) return;
            if (element is TextBox { AcceptsReturn: true }) return;
            var command = GetEnterCommand(element);
            if (command?.CanExecute(null) == true)
            {
                // Valide d'abord la liaison en cours pour que le ViewModel voie la dernière saisie.
                if (element is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                command.Execute(null);
                args.Handled = true;
            }
        };
    }
}
