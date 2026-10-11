namespace AutoCorrectTray.Spelling;

public enum ScriptKind { None, Cyrillic, Latin, Mixed }

public static class LanguageDetector
{
    /// <summary>Detects the script of a word: Cyrillic / Latin / mixed.</summary>
    public static ScriptKind Detect(string word)
    {
        bool cyrillic = false, latin = false;
        foreach (var ch in word)
        {
            if (char.IsWhiteSpace(ch) || char.IsDigit(ch) || char.IsPunctuation(ch)) continue;
            if (ch is >= '\u0400' and <= '\u04FF')
                cyrillic = true;
            else if (char.IsLetter(ch))
                latin = true;
        }
        return (cyrillic, latin) switch
        {
            (true, true) => ScriptKind.Mixed,
            (true, false) => ScriptKind.Cyrillic,
            (false, true) => ScriptKind.Latin,
            _ => ScriptKind.None,
        };
    }
}
