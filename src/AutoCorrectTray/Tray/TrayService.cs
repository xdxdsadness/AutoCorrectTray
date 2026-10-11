using System.Windows;
using System.Windows.Controls;
using AutoCorrectTray.Settings;
using AutoCorrectTray.Ui;
using Hardcodet.Wpf.TaskbarNotification;

namespace AutoCorrectTray.Tray;

/// <summary>Tray icon, menu, balloons. The menu is localized and refreshed on
/// UI language change.</summary>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _settingsItem;
    private readonly MenuItem _pauseItem;
    private readonly MenuItem _aboutItem;
    private readonly MenuItem _exitItem;
    private readonly AppServices _svc;

    public event Action? ExitRequested;

    public TrayService(AppServices svc)
    {
        _svc = svc;
        _icon = new TaskbarIcon
        {
            ToolTipText = L10n.T("tray.tooltip"),
            IconSource = TrayIconFactory.Create(false),
        };

        var menu = new ContextMenu();

        _settingsItem = new MenuItem { Header = L10n.T("tray.settings"), FontWeight = FontWeights.SemiBold };
        _settingsItem.Click += (_, _) => svc.RequestShowSettings();
        menu.Items.Add(_settingsItem);

        _pauseItem = new MenuItem { Header = L10n.T("tray.pause"), IsCheckable = true };
        _pauseItem.Click += (_, _) => { if (svc.Paused != _pauseItem.IsChecked) svc.TogglePause(); };
        menu.Items.Add(_pauseItem);

        _aboutItem = new MenuItem { Header = L10n.T("tray.about") };
        _aboutItem.Click += (_, _) => ShowAbout();
        menu.Items.Add(_aboutItem);

        _exitItem = new MenuItem { Header = L10n.T("tray.exit") };
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(_exitItem);

        _icon.ContextMenu = menu;
        _icon.TrayLeftMouseUp += (_, _) => svc.RequestShowSettings();

        L10n.LanguageChanged += UpdateTexts;

        if (svc.SettingsStore.IsFirstRun)
        {
            ShowBalloon(L10n.T("balloon.firstrun.title"),
                L10n.T("balloon.firstrun.body", svc.Settings.FixHotkey, svc.Settings.TranslateHotkey));
        }
    }

    private void ShowAbout()
    {
        var s = _svc.Settings;
        MessageBox.Show(
            L10n.T("about.body", s.FixHotkey, s.TranslateHotkey, s.CaptureHotkey, s.PauseHotkey),
            L10n.T("about.title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>UI language changed - refresh menu headers and the tooltip.</summary>
    private void UpdateTexts()
    {
        try
        {
            _settingsItem.Header = L10n.T("tray.settings");
            _pauseItem.Header = L10n.T("tray.pause");
            _aboutItem.Header = L10n.T("tray.about");
            _exitItem.Header = L10n.T("tray.exit");
            _icon.ToolTipText = Paused() ? L10n.T("tray.tooltip.paused") : L10n.T("tray.tooltip");
        }
        catch { }
    }

    public void UpdateState()
    {
        _pauseItem.IsChecked = Paused();
        _icon.ToolTipText = Paused() ? L10n.T("tray.tooltip.paused") : L10n.T("tray.tooltip");
        _icon.IconSource = TrayIconFactory.Create(Paused());
    }

    private bool Paused() => _pauseItem.IsChecked == true;

    public void ShowBalloon(string title, string message) =>
        _icon.ShowBalloonTip(title, message, BalloonIcon.Info);

    public void Dispose()
    {
        L10n.LanguageChanged -= UpdateTexts;
        _icon.Dispose();
    }
}
