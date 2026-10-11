using System.Windows;

namespace AutoCorrectTray.Ui;

/// <summary>
/// UI localization: string dictionaries Strings/en.xaml and Strings/ru.xaml
/// (same keys), dictionary swap on language change. XAML texts reference
/// strings via DynamicResource and update instantly; code strings use L10n.T.
/// </summary>
internal static class L10n
{
    private static readonly Uri EnUri = new("pack://application:,,,/Strings/en.xaml");
    private static readonly Uri RuUri = new("pack://application:,,,/Strings/ru.xaml");

    public static string Current { get; private set; } = "en";

    /// <summary>Language changed - open windows refresh via DynamicResource.</summary>
    public static event Action? LanguageChanged;

    public static void Apply(string? language)
    {
        Current = string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        var uri = Current == "ru" ? RuUri : EnUri;

        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < dicts.Count; i++)
        {
            if (IsStringDict(dicts[i].Source))
            {
                dicts[i] = new ResourceDictionary { Source = uri };
                LanguageChanged?.Invoke();
                return;
            }
        }
        dicts.Insert(0, new ResourceDictionary { Source = uri });
        LanguageChanged?.Invoke();
    }

    private static bool IsStringDict(Uri? source)
    {
        if (source == null) return false;
        var s = source.OriginalString.Replace('\\', '/');
        return s.EndsWith("Strings/en.xaml", StringComparison.OrdinalIgnoreCase) ||
               s.EndsWith("Strings/ru.xaml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>String by key; falls back to the key itself (missing entries are easy to spot).</summary>
    public static string T(string key) =>
        Application.Current.Resources[key] as string ?? key;

    /// <summary>Formatted string (placeholders {0}... in the resources).</summary>
    public static string T(string key, params object[] args) =>
        string.Format(T(key), args);
}
