using InfraVault.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace InfraVault.Infrastructure.Windows;

/// <summary>
/// Relaie les événements du poste Windows utiles à la politique de verrouillage :
/// mise en veille et verrouillage de la session (Win+L, changement d'utilisateur).
/// Sans effet sur les autres systèmes (tests sous Linux).
/// </summary>
public sealed class WindowsSecurityService(ILogger<WindowsSecurityService> logger) : IPlatformSecurityService
{
    private bool _started;

    public event EventHandler? SystemSuspending;
    public event EventHandler? SessionLocked;

    public void Start()
    {
        if (_started || !OperatingSystem.IsWindows()) return;
        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _started = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            logger.LogWarning(ex, "Abonnement aux événements système impossible");
        }
    }

    public void Dispose()
    {
        if (!_started || !OperatingSystem.IsWindows()) return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _started = false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) SystemSuspending?.Invoke(this, EventArgs.Empty);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            SessionLocked?.Invoke(this, EventArgs.Empty);
    }
}
