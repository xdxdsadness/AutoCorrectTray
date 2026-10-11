using System.Text.RegularExpressions;

namespace AutoCorrectTray.Punctuation;

/// <summary>Autopunctuation settings (pure logic, tested).</summary>
public sealed class PunctuationSettings
{
    public bool AutoPeriod { get; init; } = true;
    public bool AutoCapitalize { get; init; } = true;
    public bool SpaceAfterPunct { get; init; }
}

public enum IncomingAction
{
    Type,        // type as-is
    Capitalize,  // replace with uppercase (suppress the original key)
    SpaceAndType // insert a space before the character (suppress the original key)
}

/// <summary>Autopunctuation rules. All methods are pure - they work on already typed text.</summary>
public static class PunctuationEngine
{
    // A word (>=3 letters) + sentence-end punctuation + closing quotes/brackets + spaces - at the end of the text.
    // 1-2 letter words ("i.", "e.") do not count as sentence end - abbreviations.
    private static readonly Regex SentenceTail = new(
        @"(?<word>[\p{L}\p{Mn}''\-]{3,})(?<punct>[.!?…]+[""'»”’)\]]*[ \t]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// What to insert before Enter: null - nothing, otherwise (how many backspaces, what to type).
    /// Trailing spaces are replaced by the period.
    /// </summary>
    public static (int Backspaces, string Insert)? PeriodOnEnter(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var trimmed = text.TrimEnd();
        int trailingSpaces = text.Length - trimmed.Length;
        if (trimmed.Length == 0) return null;

        char last = trimmed[^1];
        if (last is '.' or '!' or '?' or '…') return null;   // already punctuated
        if (last is ',' or ';') return (trailingSpaces + 1, ".");
        if (!trimmed.Any(char.IsLetter)) return null;        // digits/symbols only - leave alone
        if (char.IsLetterOrDigit(last) ||
            last is ')' or ']' or '"' or '\'' or '»' or '”' or '’' or '%' or '$')
            return (trailingSpaces, ".");
        return null; // "—", ":", "(" etc. - ambiguous
    }

    /// <summary>Decides what to do with the incoming character.</summary>
    public static IncomingAction PlanIncoming(string textBeforeCaret, char incoming, PunctuationSettings s)
    {
        if (s.AutoCapitalize &&
            char.IsLetter(incoming) &&
            char.IsLower(incoming) &&
            IsSentenceStartAhead(textBeforeCaret))
            return IncomingAction.Capitalize;

        if (s.SpaceAfterPunct &&
            char.IsLetter(incoming) &&
            NeedsSpace(textBeforeCaret))
            return IncomingAction.SpaceAndType;

        return IncomingAction.Type;
    }

    /// <summary>The typed text ends with sentence-end punctuation.</summary>
    public static bool IsSentenceComplete(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && trimmed[^1] is '.' or '!' or '?' or '…';
    }

    private static bool IsSentenceStartAhead(string text)
    {
        if (string.IsNullOrEmpty(text)) return false; // position unknown - do not interfere
        return SentenceTail.IsMatch(text);
    }

    private static bool NeedsSpace(string text)
    {
        if (text.Length < 2) return false;
        char last = text[^1];
        if (last is not (',' or ';' or ':' or '.' or '!' or '?')) return false;
        char prev = text[^2];
        if (!char.IsLetterOrDigit(prev)) return false;
        // Numbers: "3.14", "1,5" - no space needed
        if ((last is '.' or ',') && char.IsDigit(prev)) return false;
        return true;
    }
}
