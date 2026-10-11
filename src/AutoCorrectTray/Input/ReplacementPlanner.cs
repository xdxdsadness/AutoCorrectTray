namespace AutoCorrectTray.Input;

/// <summary>
/// Plans a word replacement: how many backspaces to send and what to type.
/// Pure logic - covered by tests.
/// </summary>
public static class ReplacementPlanner
{
    /// <summary>
    /// A word completed by a separator (space/punctuation) sits in the text
    /// before the caret: remove the word together with the separator and retype
    /// the separator - otherwise a space was left after the word and the word
    /// became a doubled one.
    /// null - the buffer does not match (stale hint, the user is typing something else).
    /// </summary>
    public static (int Backspaces, string Insert)? Plan(string word, string replacement, string? separator, string bufferText)
    {
        if (separator is not null)
        {
            var tail = word + separator;
            if (!bufferText.EndsWith(tail, StringComparison.Ordinal))
                return null;
            return (tail.Length, replacement + separator);
        }
        return (word.Length, replacement);
    }
}
