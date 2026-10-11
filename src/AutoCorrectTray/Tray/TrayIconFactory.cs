using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoCorrectTray.Tray;

/// <summary>Tray icon - the app logo. Paused state uses a muted version.
/// The small versions (tray.png / tray_paused.png) are pre-resampled with
/// sharpening - they look noticeably crisper in the tray than runtime scaling.</summary>
internal static class TrayIconFactory
{
    private const string TrayUri = "pack://application:,,,/Assets/tray.png";
    private const string TrayPausedUri = "pack://application:,,,/Assets/tray_paused.png";
    private const string LogoUri = "pack://application:,,,/Assets/logo.png";

    public static ImageSource Create(bool paused)
    {
        var logo = new BitmapImage(new Uri(paused ? TrayPausedUri : TrayUri));
        logo.Freeze();
        return logo;
    }

    /// <summary>Logo for window icons (title bar, Alt+Tab).</summary>
    public static ImageSource CreateAppIcon()
    {
        var logo = new BitmapImage(new Uri(LogoUri));
        logo.Freeze();
        return logo;
    }
}
