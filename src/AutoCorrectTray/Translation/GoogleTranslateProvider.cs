using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AutoCorrectTray.Translation;

public sealed record TranslationResult(string Text, string? DetectedLanguage);

public interface ITranslationProvider
{
    Task<TranslationResult?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default);
}

/// <summary>Tries providers in order: the first successful answer wins.</summary>
public sealed class FallbackTranslationService : ITranslationProvider
{
    private readonly ITranslationProvider[] _providers;

    public FallbackTranslationService(params ITranslationProvider[] providers)
    {
        _providers = providers;
    }

    public async Task<TranslationResult?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.TranslateAsync(text, sourceLanguage, targetLanguage, ct);
                if (result is { Text.Length: > 0 })
                    return result;
            }
            catch
            {
                // try the next provider
            }
        }
        return null;
    }
}

/// <summary>
/// Free Google Translate endpoint (no API key). The host is injected -
/// primary and alternate (clients5) in case of blocking.
/// </summary>
public sealed class GoogleTranslateProvider : ITranslationProvider
{
    public const string DefaultEndpoint = "https://translate.googleapis.com/translate_a/single";
    public const string AlternateEndpoint = "https://clients5.google.com/translate_a/single";

    private readonly string _endpoint;

    // Direct connection without the system proxy: proxy clients (VPN etc.)
    // often break this host, while direct access works.
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseProxy = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    static GoogleTranslateProvider()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AutoCorrectTray/1.0");
    }

    public GoogleTranslateProvider(string endpoint = DefaultEndpoint)
    {
        _endpoint = endpoint;
    }

    public async Task<TranslationResult?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var source = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
        var url = _endpoint +
                  "?client=gtx" +
                  $"&sl={Uri.EscapeDataString(source)}" +
                  $"&tl={Uri.EscapeDataString(targetLanguage)}" +
                  "&dt=t&ie=UTF-8&oe=UTF-8" +
                  $"&q={Uri.EscapeDataString(text)}";

        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var parsed = ParseResponse(json);
        return parsed is null ? null : new TranslationResult(parsed.Value.text, parsed.Value.detected);
    }

    /// <summary>Parses a response of the form [[["translation","original",...],...],null,"lang",...]. Separated for tests.</summary>
    internal static (string text, string? detected)? ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
                return null;

            var sb = new StringBuilder();
            var chunks = root[0];
            if (chunks.ValueKind == JsonValueKind.Array)
            {
                foreach (var chunk in chunks.EnumerateArray())
                {
                    if (chunk.ValueKind == JsonValueKind.Array &&
                        chunk.GetArrayLength() > 0 &&
                        chunk[0].ValueKind == JsonValueKind.String)
                        sb.Append(chunk[0].GetString());
                }
            }

            string? detected = root[2].ValueKind == JsonValueKind.String ? root[2].GetString() : null;
            var text = System.Net.WebUtility.HtmlDecode(sb.ToString());
            return text.Length == 0 ? null : (text, detected);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Fallback MyMemory provider (requires an explicit language pair - no auto-detect).
/// Quality is below Google: last resort when the primary providers are unavailable.
/// </summary>
public sealed class MyMemoryProvider : ITranslationProvider
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseProxy = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    public async Task<TranslationResult?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage == "auto")
            return null; // a concrete source language is required

        var url = "https://api.mymemory.translated.net/get" +
                  $"?q={Uri.EscapeDataString(text)}" +
                  $"&langpair={Uri.EscapeDataString(sourceLanguage)}|{Uri.EscapeDataString(targetLanguage)}";

        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;
        if (doc.RootElement.TryGetProperty("responseStatus", out var status) &&
            status.ValueKind == JsonValueKind.Number && status.GetInt32() != 200)
            return null;
        if (!doc.RootElement.TryGetProperty("responseData", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("translatedText", out var translated) ||
            translated.ValueKind != JsonValueKind.String)
            return null;

        var result = System.Net.WebUtility.HtmlDecode(translated.GetString() ?? string.Empty);
        return result.Length == 0 ? null : new TranslationResult(result, sourceLanguage);
    }
}
