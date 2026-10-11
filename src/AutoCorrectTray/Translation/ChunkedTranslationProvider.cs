using System.Text;

namespace AutoCorrectTray.Translation;

/// <summary>
/// Splits long text into chunks (sentence/line/word boundaries) and joins the
/// translations. Needed for "whole window translation": the free endpoint
/// rejects very long requests.
/// </summary>
public sealed class ChunkedTranslationProvider : ITranslationProvider
{
    public const int ChunkLimit = 1200;

    private readonly ITranslationProvider _inner;

    public ChunkedTranslationProvider(ITranslationProvider inner) => _inner = inner;

    public async Task<TranslationResult?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= ChunkLimit)
            return await _inner.TranslateAsync(text, sourceLanguage, targetLanguage, ct);

        var chunks = Split(text, ChunkLimit);
        var sb = new StringBuilder();
        string? detected = null;
        foreach (var chunk in chunks)
        {
            var result = await _inner.TranslateAsync(chunk, sourceLanguage, targetLanguage, ct);
            if (result is not { Text.Length: > 0 }) return null; // any chunk failing means the whole thing failed
            detected ??= result.DetectedLanguage;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(result.Text);
        }
        return new TranslationResult(sb.ToString(), detected);
    }

    /// <summary>Pure function (covered by tests): splits on \n, sentence boundary, or word.</summary>
    internal static List<string> Split(string text, int limit)
    {
        var result = new List<string>();
        var rest = text;
        while (rest.Length > limit)
        {
            // best cut: line end → sentence end → space, closest to the limit
            int cut = rest.LastIndexOf('\n', limit - 1, limit - 1);
            if (cut < limit / 2)
            {
                cut = rest.LastIndexOf(". ", limit - 1, limit - 2);
                if (cut < limit / 2) cut = rest.LastIndexOf(' ', limit - 1, limit - 1);
                if (cut < limit / 2) cut = limit; // no boundary at all - hard cut
                else cut++; // keep the period
            }
            else cut++; // include the \n

            result.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) result.Add(rest);
        return result;
    }
}
