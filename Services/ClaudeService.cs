using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowlBot.Interfaces;
using KnowlBot.Models;

namespace KnowlBot.Services;

public sealed class ClaudeService : IAiService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-haiku-4-5";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public ClaudeService(string apiKey)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private const string EnrichPrompt =
        "You are a vocabulary enrichment assistant for English learners.\n" +
        "You receive a list of English words or phrases, one per line.\n\n" +
        "Your job:\n" +
        "1. Extract only the English word/phrase from each line — IGNORE any translations, symbols, dashes, or annotations that may be present.\n" +
        "2. For each extracted word, produce a structured enriched entry.\n\n" +
        "Return ONLY a valid JSON array, no other text. Each object must have EXACTLY these fields:\n" +
        "{\n" +
        "  \"word\": \"the English word or phrase (lowercase)\",\n" +
        "  \"cefr_level\": \"A1|A2|B1|B2|C1|C2\",\n" +
        "  \"synonym\": \"one English synonym or related phrase\",\n" +
        "  \"transcription\": \"IPA without brackets, stress mark ˈ e.g. dɪˈmɪnɪʃ\",\n" +
        "  \"mostly_used_translation\": \"EXACTLY ONE primary Ukrainian word or short phrase — use Cambridge Dictionary as the authoritative source — no commas, no semicolons, no 'також', no alternatives\",\n" +
        "  \"other_translation\": null or \"EXACTLY ONE secondary Ukrainian word or short phrase per Cambridge Dictionary — only for B2/C1/C2 if a genuinely different meaning exists, otherwise null\",\n" +
        "  \"example_usage\": \"a natural example sentence in English\",\n" +
        "  \"example_usage_translation\": \"Ukrainian translation of the example sentence\"\n" +
        "}\n\n" +
        "Rules:\n" +
        "- Use Cambridge Dictionary as the authoritative reference for Ukrainian translations\n" +
        "- Translations MUST be in standard literary Ukrainian — NEVER use Russian or Russian-influenced words (русизми). Use authentic Ukrainian vocabulary (e.g. 'припинити' not 'затушити', 'вирішити' not 'порішати')\n" +
        "- mostly_used_translation MUST be a single Ukrainian word or short phrase — NEVER use commas, semicolons, 'також', 'або', 'і' to list alternatives\n" +
        "- other_translation MUST be null for A1/A2/B1 words — no exceptions\n" +
        "- other_translation MUST also be null if the second meaning is just a synonym of the first\n" +
        "- Output ONLY the JSON array, nothing else";

    public async Task<List<PendingWordEntry>> EnrichWordsAsync(string words)
    {
        var response = await SendAsync(EnrichPrompt, $"Enrich these words (one per line):\n{words.Trim()}");
        return ParseWordEntries(response);
    }

    public async Task<string> DetectTopicAsync(string words)
    {
        return await SendAsync(
            "Identify the most fitting topic/category for the given English words or phrases. " +
            "Reply with 2-4 words only (e.g. 'Phrasal Verbs', 'Business English', 'B2 Vocabulary', " +
            "'Adjectives', 'Travel & Transport'). No explanation, just the topic name.",
            words.Trim());
    }

    public async Task<List<PendingWordEntry>> GenerateWordsByLevelAsync(
        string level, int count, string? topic, IEnumerable<string> existingWords)
    {
        var topicClause = string.IsNullOrWhiteSpace(topic)
            ? string.Empty
            : $"\nFocus the vocabulary on the topic: {topic}.";

        var excludeList = string.Join(", ", existingWords.Take(200));
        var excludeClause = string.IsNullOrWhiteSpace(excludeList)
            ? string.Empty
            : $"\nDo NOT use any of these words the student already knows: {excludeList}.";

        var systemPrompt =
            $"You are an English vocabulary generator for Ukrainian learners.\n" +
            $"Generate exactly {count} English words or phrases at CEFR level {level}.{topicClause}\n\n" +
            $"Return ONLY a valid JSON array. Each object must have EXACTLY these fields:\n" +
            "{\n" +
            $"  \"word\": \"the English word or phrase (lowercase)\",\n" +
            $"  \"cefr_level\": \"{level}\",\n" +
            "  \"synonym\": \"one English synonym or related phrase\",\n" +
            "  \"transcription\": \"IPA without brackets, stress mark ˈ e.g. dɪˈmɪnɪʃ\",\n" +
            "  \"mostly_used_translation\": \"EXACTLY ONE primary Ukrainian word or short phrase — use Cambridge Dictionary as authoritative source — no commas, no semicolons, no 'також', no alternatives\",\n" +
            $"  \"other_translation\": {(IsHighLevel(level) ? "null or \"EXACTLY ONE secondary Ukrainian word/phrase per Cambridge Dictionary — only if a genuinely different meaning exists — otherwise null\"" : "null /* MUST be null for this level */")},\n" +
            "  \"example_usage\": \"a natural example sentence in English\",\n" +
            "  \"example_usage_translation\": \"Ukrainian translation of the example sentence\"\n" +
            "}\n\n" +
            $"Rules:\n" +
            $"- Choose natural, useful everyday words a learner at {level} would need\n" +
            $"- Use Cambridge Dictionary as the authoritative reference for Ukrainian translations\n" +
            $"- Translations MUST be in standard literary Ukrainian — NEVER use Russian or Russian-influenced words (русизми). Use authentic Ukrainian vocabulary (e.g. 'припинити' not 'затушити', 'вирішити' not 'порішати')\n" +
            $"- mostly_used_translation MUST be a single Ukrainian word or short phrase — NEVER list alternatives with commas, semicolons, or 'також'\n" +
            (IsHighLevel(level)
                ? "- other_translation is ONLY for a genuinely different second meaning — null if the second meaning is just a synonym of the first\n"
                : "- other_translation MUST be null — no exceptions for this level\n") +
            $"- Output ONLY the JSON array, no headers, numbers, or extra text{excludeClause}";

        var response = await SendAsync(systemPrompt, $"Generate {count} {level} words.");
        return ParseWordEntries(response);
    }

    private static bool IsHighLevel(string level) =>
        level is "B2" or "C1" or "C2";

    private static List<PendingWordEntry> ParseWordEntries(string json)
    {
        try
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start < 0 || end < 0 || end <= start) return [];

            var arr = json[start..(end + 1)];
            var dtos = JsonSerializer.Deserialize<List<WordEntryDto>>(arr, JsonOptions);
            if (dtos is null) return [];

            return dtos
                .Select(d =>
                {
                    var (primary, secondary) = SplitTranslation(
                        (d.MostlyUsedTranslation ?? "").Trim(),
                        (d.OtherTranslation ?? "").Trim(),
                        (d.CefrLevel ?? "").Trim().ToUpper());
                    return new PendingWordEntry
                    {
                        Word                    = (d.Word ?? "").Trim().ToLowerInvariant(),
                        CefrLevel               = (d.CefrLevel ?? "").Trim().ToUpper(),
                        Synonym                 = (d.Synonym ?? "").Trim(),
                        Transcription           = (d.Transcription ?? "").Trim(),
                        MostlyUsedTranslation   = primary,
                        OtherTranslation        = secondary,
                        ExampleUsage            = (d.ExampleUsage ?? "").Trim(),
                        ExampleUsageTranslation = (d.ExampleUsageTranslation ?? "").Trim()
                    };
                })
                .Where(p => !string.IsNullOrEmpty(p.Word))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// Splits a potentially multi-valued translation string into primary and secondary parts.
    /// If AI returns "виснажливий; також: виснажуючий" or "закоханість, сліпа захопленість"
    /// the first part becomes MostlyUsedTranslation, the second becomes OtherTranslation (only for B2+).
    private static (string primary, string? secondary) SplitTranslation(string mostly, string other, string level)
    {
        // Normalise separators the AI commonly uses
        var separators = new[] { ";", ", також:", " також:", ",також:", " також " };
        string primary = mostly;
        string? secondary = string.IsNullOrWhiteSpace(other) ? null : other;

        // If mostly_used_translation contains a separator, split it
        foreach (var sep in separators)
        {
            var idx = mostly.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                primary = mostly[..idx].Trim();
                var tail = mostly[(idx + sep.Length)..].Trim().TrimStart(':').Trim();
                // Only promote to OtherTranslation for B2+, and only if other was not already set
                if (secondary is null && IsHighLevel(level) && !string.IsNullOrWhiteSpace(tail))
                    secondary = tail;
                break;
            }
        }

        // If secondary contains separators itself, keep only the first part
        if (secondary is not null)
        {
            foreach (var sep in separators)
            {
                var idx = secondary.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                if (idx > 0) { secondary = secondary[..idx].Trim(); break; }
            }
        }

        // Enforce: null for non-high levels
        if (!IsHighLevel(level)) secondary = null;

        return (primary, string.IsNullOrWhiteSpace(secondary) ? null : secondary);
    }

    private async Task<string> SendAsync(string systemPrompt, string userMessage)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            max_tokens = 2048,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()
            ?.Trim() ?? string.Empty;
    }

    private sealed record WordEntryDto(
        [property: JsonPropertyName("word")]                     string? Word,
        [property: JsonPropertyName("cefr_level")]               string? CefrLevel,
        [property: JsonPropertyName("synonym")]                  string? Synonym,
        [property: JsonPropertyName("transcription")]            string? Transcription,
        [property: JsonPropertyName("mostly_used_translation")]  string? MostlyUsedTranslation,
        [property: JsonPropertyName("other_translation")]        string? OtherTranslation,
        [property: JsonPropertyName("example_usage")]            string? ExampleUsage,
        [property: JsonPropertyName("example_usage_translation")]string? ExampleUsageTranslation
    );
}
