namespace AutoCorrectTray.Translation;

public enum TranslationMode
{
    Selection,          // selection read via UIA
    ClipboardSelection, // selection copied via Ctrl+C
    Typed,              // last typed sentence (no selection)
}

public sealed record LanguageInfo(string Code, string Name);

public static class Languages
{
    public static readonly LanguageInfo Auto = new("auto", "Detect language");

    public static readonly IReadOnlyList<LanguageInfo> All = new[]
    {
        Auto,
        new LanguageInfo("ru", "Russian"),
        new LanguageInfo("en", "English"),
        new LanguageInfo("uk", "Ukrainian"),
        new LanguageInfo("de", "German"),
        new LanguageInfo("fr", "French"),
        new LanguageInfo("es", "Spanish"),
        new LanguageInfo("it", "Italian"),
        new LanguageInfo("pl", "Polish"),
        new LanguageInfo("pt", "Portuguese"),
        new LanguageInfo("tr", "Turkish"),
        new LanguageInfo("zh-CN", "Chinese"),
        new LanguageInfo("ja", "Japanese"),
        new LanguageInfo("ko", "Korean"),
        new LanguageInfo("ar", "Arabic"),
        new LanguageInfo("kk", "Kazakh"),
        new LanguageInfo("be", "Belarusian"),
    };

    public static IReadOnlyList<LanguageInfo> Concrete => All.Skip(1).ToList();
}
