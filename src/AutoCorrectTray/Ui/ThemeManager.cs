using System.Windows;

namespace AutoCorrectTray.Ui;

/// <summary>
/// Light/dark theme switching: swaps the palette dictionary in the app resources.
/// Styles in App.xaml reference brushes via DynamicResource, so all open windows
/// repaint instantly.
/// </summary>
internal static class ThemeManager
{
    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml");

    public static bool IsDark { get; private set; }

    public static void Apply(string? theme)
    {
        IsDark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        var uri = IsDark ? DarkUri : LightUri;

        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < dicts.Count; i++)
        {
            // App.xaml references the palette with a relative Source - compare by file name
            if (IsPalette(dicts[i].Source))
            {
                dicts[i] = new ResourceDictionary { Source = uri };
                return;
            }
        }
        dicts.Insert(0, new ResourceDictionary { Source = uri });
    }

    private static bool IsPalette(Uri? source)
    {
        if (source == null) return false;
        var s = source.OriginalString.Replace('\\', '/');
        return s.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
               s.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase);
    }
}
