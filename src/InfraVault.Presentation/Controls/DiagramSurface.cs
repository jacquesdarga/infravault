using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InfraVault.Presentation.ViewModels;

namespace InfraVault.Presentation.Controls;

/// <summary>Surface du schéma de cartographie : clic pour ouvrir une ressource, Ctrl+molette pour zoomer.</summary>
public sealed class DiagramSurface : Image
{
    private Point? _pressed;

    public DiagramSurface()
    {
        Cursor = Cursors.Hand;
        Stretch = System.Windows.Media.Stretch.Fill;
    }

    private ScrollViewer? _host;

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        Loaded -= OnLoaded;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DependencyObject? current = this;
        while (current is not null and not ScrollViewer) current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        if (current is not ScrollViewer host || ReferenceEquals(host, _host)) { Report(); return; }
        _host = host;
        host.SizeChanged += (_, _) => Report();
        Report();
    }

    private void Report()
    {
        if (_host is not null && DataContext is TopologyViewModel vm) vm.SetViewport(_host.ActualWidth, _host.ActualHeight);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = e.GetPosition(this);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var released = e.GetPosition(this);
        // Un vrai clic (pas un glissement) ouvre la ressource sous le pointeur.
        if (_pressed is { } p && Math.Abs(p.X - released.X) < 4 && Math.Abs(p.Y - released.Y) < 4)
            (DataContext as TopologyViewModel)?.OpenAt(released);
        _pressed = null;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (Keyboard.Modifiers != ModifierKeys.Control || DataContext is not TopologyViewModel vm) return;
        if (e.Delta > 0) vm.ZoomInCommand.Execute(null);
        else vm.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }
}
