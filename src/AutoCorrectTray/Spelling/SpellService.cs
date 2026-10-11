using System.IO;
using AutoCorrectTray.Logging;
using WeCantSpell.Hunspell;

namespace AutoCorrectTray.Spelling;

/// <summary>
/// Spell checking on the Hunspell engine (the same one as in Chrome and LibreOffice).
/// ru_RU and en_US dictionaries sit next to the exe; the user's personal dictionary is in settings.
/// </summary>
public sealed class SpellService
{
    private readonly Logger _log;
    private readonly Func<List<string>> _userWordsProvider;
    private readonly object _gate = new();
    private WordList? _russian;
    private WordList? _english;

    public bool Ready { get; private set; }

    public SpellService(Logger log, Func<List<string>> userWordsProvider)
    {
        _log = log;
        _userWordsProvider = userWordsProvider;
    }

    public void WarmUp() => Task.Run(LoadDictionaries);

    private void LoadDictionaries()
    {
        try
        {
            var dir = FindDictionariesDirectory();
            if (dir == null)
            {
                _log.Error("Dictionaries folder not found - spell checking will be disabled");
                return;
            }

            var ru = TryLoad(Path.Combine(dir, "ru_RU.dic"));
            if (ru != null) { lock (_gate) _russian = ru; _log.Info("ru_RU dictionary loaded"); }

            var en = TryLoad(Path.Combine(dir, "en_US.dic"));
            if (en != null) { lock (_gate) _english = en; _log.Info("en_US dictionary loaded"); }

            Ready = _russian != null || _english != null;
            if (!Ready)
                _log.Error("No dictionary loaded - spell checking disabled");
        }
        catch (Exception ex)
        {
            _log.Error("Loading dictionaries", ex);
        }
    }

    private static WordList? TryLoad(string dicPath)
    {
        try
        {
            if (!File.Exists(dicPath)) return null;
            var affPath = Path.ChangeExtension(dicPath, ".aff");
            return File.Exists(affPath) ? WordList.CreateFromFiles(dicPath, affPath) : WordList.CreateFromFiles(dicPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FindDictionariesDirectory()
    {
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        string baseDir = AppContext.BaseDirectory;
        var candidates = new[] { exeDir, baseDir, Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..")) };
        foreach (var dir in candidates)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(dir, "Dictionaries")); }
            catch { continue; }
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.dic").Any())
                return candidate;
        }
        return null;
    }

    public bool IsMisspelled(string rawWord)
    {
        var word = CleanWord(rawWord);
        if (word.Length < 2) return false;
        if (!word.All(char.IsLetter)) return false;          // digits, URLs, emails - skip
        if (IsUserWord(word)) return false;

        return LanguageDetector.Detect(word) switch
        {
            ScriptKind.Cyrillic => !Check(_russian, word),
            ScriptKind.Latin => !Check(_english, word),
            ScriptKind.Mixed => true,                        // mixed scripts - almost certainly a typo
            _ => false,
        };
    }

    private static bool Check(WordList? list, string word)
    {
        if (list == null) return true; // no dictionary loaded - not an error
        try { return list.Check(word); }
        catch { return true; }
    }

    private bool IsUserWord(string word)
    {
        var words = _userWordsProvider();
        return words is not null && words.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));
    }

    public string[] Suggest(string rawWord, int max)
    {
        var word = CleanWord(rawWord);
        var list = LanguageDetector.Detect(word) switch
        {
            ScriptKind.Cyrillic => _russian,
            ScriptKind.Latin => _english,
            _ => null,
        };
        if (list == null) return Array.Empty<string>();
        try
        {
            return list.Suggest(word)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string CleanWord(string word) =>
        word.Trim(' ', '.', ',', '!', '?', ':', ';', '"', '\'', '«', '»', '(', ')', '[', ']', '{', '}',
                  '…', '—', '–', '-', '“', '”', '’', '*', '/', '\\', '\t');
}
