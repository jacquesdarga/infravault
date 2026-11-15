using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using InfraVault.Application;
using InfraVault.Application.Interfaces;
using InfraVault.Infrastructure;
using InfraVault.Presentation.Services;
using InfraVault.Presentation.ViewModels;
using InfraVault.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace InfraVault.Presentation;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Interface en français quel que soit le réglage régional du poste.
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        var dataDirectory = ArgumentValue(e.Args, "--data-dir") ?? Environment.GetEnvironmentVariable("INFRAVAULT_DATA_DIR");

        // Une seule instance par dossier de données : deux processus sur le même coffre se marcheraient dessus.
        var paths = new Infrastructure.Backup.AppPaths(dataDirectory);
        _singleInstance = new Mutex(true, "InfraVault-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.DataDirectory.ToLowerInvariant())))[..16], out var first);
        if (!first)
        {
            MessageBox.Show("InfraVault est déjà ouvert sur ce coffre.", "InfraVault", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(paths.LogDirectory);
        // Journal technique : uniquement des messages et des identifiants, jamais de secret ni de contenu déchiffré.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(Path.Combine(paths.LogDirectory, "infravault-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14, fileSizeLimitBytes: 5_000_000, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(dispose: true));
        services.AddInfraVaultApplication();
        services.AddInfraVaultInfrastructure(dataDirectory);
        services.AddSingleton<PreferencesStore>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<SessionGuard>();
        services.AddSingleton<UiContext>();
        services.AddSingleton<AppViewModel>();
        _services = services.BuildServiceProvider();

        var logger = _services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += (_, args) => HandleUnhandled(args.Exception, logger, () => args.Handled = true);
        TaskScheduler.UnobservedTaskException += (_, args) => { logger.LogError(args.Exception, "Tâche non observée"); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => logger.LogCritical(args.ExceptionObject as Exception, "Erreur fatale");

        var preferences = _services.GetRequiredService<PreferencesStore>();
        preferences.Load();
        var theme = _services.GetRequiredService<ThemeService>();
        theme.Initialize();

        var app = _services.GetRequiredService<AppViewModel>();
        var window = new MainWindow(app, theme, preferences);
        MainWindow = window;
        _services.GetRequiredService<SessionGuard>().Start(window);
        window.Show();
        logger.LogInformation("InfraVault {Version} démarré", Shared.Constants.AppInfo.Version);

        await app.StartAsync();

#if DEBUG
        if (ArgumentValue(e.Args, "--snapshot") is { } snapshotDirectory)
            await Diagnostics.SnapshotRunner.RunAsync(_services, window, snapshotDirectory);
#endif
    }

    private void HandleUnhandled(Exception exception, Microsoft.Extensions.Logging.ILogger logger, Action markHandled)
    {
        markHandled();
        if (exception is VaultLockedException) return;
        logger.LogError(exception, "Exception non gérée dans l'interface");
        var message = exception is InfraVaultException
            ? exception.Message
            : "Une erreur inattendue s'est produite. Le détail est dans le journal de l'application.";
        _services?.GetService<ToastService>()?.Error(message);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Fermeture = verrouillage : la clé est effacée et l'événement journalisé.
            if (_services is not null)
            {
                _services.GetRequiredService<IClipboardService>().ClearIfOwned();
                var session = _services.GetRequiredService<IVaultSession>();
                if (session.IsUnlocked)
                {
                    Task.Run(() => _services.GetRequiredService<Application.Services.AuthenticationService>().LockAsync("Fermeture")).Wait(TimeSpan.FromSeconds(3));
                    session.Lock();
                }
                _services.Dispose();
            }
        }
        catch (Exception)
        {
            // La fermeture ne doit jamais échouer.
        }
        finally
        {
            Log.CloseAndFlush();
            _singleInstance?.Dispose();
        }
        base.OnExit(e);
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
