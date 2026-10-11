using System.Threading;
using System.Windows;
using AutoCorrectTray.Logging;
using AutoCorrectTray.Settings;
using AutoCorrectTray.Ui;

namespace AutoCorrectTray;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private SettingsWindow? _settingsWindow;

    public AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\AutoCorrectTray.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var log = new Logger();
        DispatcherUnhandledException += (_, args) =>
        {
            log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };

        var store = new SettingsStore(log);
        Services = new AppServices(Dispatcher, log, store);
        Services.Initialize();
        ThemeManager.Apply(store.Current.Theme);   // palette before any window opens
        L10n.Apply(store.Current.Language);        // UI language before any window opens

        Services.ShowSettingsRequested += ShowSettings;
        Services.Tray.ExitRequested += Shutdown;

        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            ShowSettings();

        log.Info("Application started");
    }

    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(Services);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
