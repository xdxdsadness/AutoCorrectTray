using System.Text;
using System.Text.RegularExpressions;

namespace AutoCorrectTray.Input;

/// <summary>
/// Typed-text buffer (text the user typed while the caret was observed).
/// Pure logic without Win32 - covered by tests.
/// </summary>
public sealed class TypedTextBuffer
{
    private readonly StringBuilder _sb = new();
    public const int MaxLength = 4000;

    /// <summary>Last completed sentence (for translation).</summary>
    public string LastSentence { get; private set; } = string.Empty;

    public string Text => _sb.ToString();
    public int Length => _sb.Length;

    public void Append(char c)
    {
        _sb.Append(c);
        TrimOverflow();
    }

    public void Append(string s)
    {
        _sb.Append(s);
        TrimOverflow();
    }

    private void TrimOverflow()
    {
        if (_sb.Length > MaxLength)
            _sb.Remove(0, _sb.Length - MaxLength);
    }

    public void Backspace()
    {
        if (_sb.Length == 0) return;
        int remove = 1;
        if (char.IsLowSurrogate(_sb[^1]) && _sb.Length >= 2 && char.IsHighSurrogate(_sb[^2]))
            remove = 2;
        _sb.Remove(_sb.Length - remove, remove);
    }

    /// <summary>Applies an insertion plan (backspaces + insert) to the buffer too.</summary>
    public void ApplyPlan(int backspaces, string insert)
    {
        for (int i = 0; i < backspaces && _sb.Length > 0; i++)
            Backspace();
        if (insert.Length > 0)
            Append(insert);
    }

    public void TrimEnd(int count)
    {
        count = Math.Min(count, _sb.Length);
        if (count > 0)
            _sb.Remove(_sb.Length - count, count);
    }

    /// <summary>Replaces the last word of the buffer (after a successful replacement in the field).</summary>
    public void ReplaceLastWord(string replacement)
    {
        var word = CurrentWord;
        if (word.Length > 0)
            TrimEnd(word.Length);
        Append(replacement);
    }

    public void Reset() => _sb.Clear();

    private static readonly Regex WordTailRegex = new(@"[\p{L}\p{Nd}\p{Mn}''\-]+$", RegexOptions.Compiled);

    /// <summary>Word being typed right now (at the tail of the buffer).</summary>
    public string CurrentWord
    {
        get
        {
            if (_sb.Length == 0) return string.Empty;
            var m = WordTailRegex.Match(_sb.ToString());
            return m.Success ? m.Value : string.Empty;
        }
    }

    private static readonly char[] SentenceSeparators = { '.', '!', '?', '…', '\n', '\r' };

    /// <summary>Current (unfinished) sentence - from the last separator to the end.</summary>
    public string CurrentSentence
    {
        get
        {
            var text = _sb.ToString();
            int idx = text.LastIndexOfAny(SentenceSeparators);
            var sentence = idx >= 0 ? text[(idx + 1)..] : text;
            return sentence.Trim();
        }
    }

    /// <summary>Sentence for translation: the current one, or the last completed one.</summary>
    public string PendingSentence
    {
        get
        {
            var current = CurrentSentence;
            if (current.Length >= 2) return current;
            return LastSentence;
        }
    }

    /// <summary>Commits the current sentence as "last" and clears the buffer (e.g. on Enter).</summary>
    public void CommitSentence()
    {
        var current = CurrentSentence;
        if (!string.IsNullOrWhiteSpace(current))
            LastSentence = current;
        Reset();
    }
}
