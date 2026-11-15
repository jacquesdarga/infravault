using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using Microsoft.Extensions.Logging;

namespace InfraVault.Presentation.Services;

/// <summary>
/// Politique de verrouillage : inactivité, mise en veille, verrouillage de session Windows et
/// réduction de la fenêtre. Déclenche aussi les sauvegardes automatiques lorsqu'elles sont dues.
/// </summary>
public sealed class SessionGuard(
    IVaultSession session, AuthenticationService authentication, VaultService vault, BackupService backup,
    IPlatformSecurityService platform, IClipboardService clipboard, ToastService toasts, ILogger<SessionGuard> logger)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastActivity = DateTime.UtcNow;
    private DateTime _lastBackupCheck = DateTime.MinValue;
    private bool _backupRunning;

    /// <summary>Temps restant avant verrouillage automatique ; null si désactivé ou verrouillé.</summary>
    public TimeSpan? Remaining { get; private set; }
    public event EventHandler? Tick;

    public void Start(Window window)
    {
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is MouseEventArgs or KeyboardEventArgs or TouchEventArgs or StylusEventArgs)
                _lastActivity = DateTime.UtcNow;
        };
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized && vault.Current.LockOnMinimize) _ = LockAsync("Fenêtre réduite");
        };
        platform.SystemSuspending += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            if (vault.Current.LockOnSuspend) _ = LockAsync("Mise en veille");
        });
        platform.SessionLocked += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            if (vault.Current.LockOnSessionLock) _ = LockAsync("Session Windows verrouillée");
        });
        platform.Start();
        session.StateChanged += (_, _) => _lastActivity = DateTime.UtcNow;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Touch() => _lastActivity = DateTime.UtcNow;

    public async Task LockAsync(string reason)
    {
        if (!session.IsUnlocked) return;
        try
        {
            clipboard.ClearIfOwned();
            await authentication.LockAsync(reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Verrouillage : erreur d'audit, verrouillage forcé");
            session.Lock();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!session.IsUnlocked)
        {
            Remaining = null;
            Tick?.Invoke(this, EventArgs.Empty);
            return;
        }

        var minutes = vault.Current.AutoLockMinutes;
        if (minutes > 0)
        {
            var remaining = TimeSpan.FromMinutes(minutes) - (DateTime.UtcNow - _lastActivity);
            Remaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            if (remaining <= TimeSpan.Zero)
            {
                _ = LockAsync("Inactivité");
                return;
            }
        }
        else
        {
            Remaining = null;
        }

        if (!_backupRunning && DateTime.UtcNow - _lastBackupCheck > TimeSpan.FromMinutes(5))
        {
            _lastBackupCheck = DateTime.UtcNow;
            _ = RunAutomaticBackupAsync();
        }
        Tick?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAutomaticBackupAsync()
    {
        _backupRunning = true;
        try
        {
            if (await backup.RunAutomaticIfDueAsync()) toasts.Show("Sauvegarde automatique effectuée.", ToastKind.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sauvegarde automatique en échec");
            toasts.Warning("La sauvegarde automatique a échoué. Vérifiez le dossier de sauvegarde.");
        }
        finally
        {
            _backupRunning = false;
        }
    }
}
