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
        "  \"mostly_used_translation\": \"primary Ukrainian translation (words only, no punctuation)\",\n" +
        "  \"other_translation\": null or \"secondary Ukrainian translation for B2+ words if a meaningful second meaning exists\",\n" +
        "  \"example_usage\": \"a natural example sentence in English\",\n" +
        "  \"example_usage_translation\": \"Ukrainian translation of the example sentence\"\n" +
        "}\n\n" +
        "Rules:\n" +
        "- mostly_used_translation and other_translation must be Ukrainian words only — no IPA, no examples, no extra punctuation\n" +
        "- other_translation must be null for A1/A2/B1 words\n" +
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
            "  \"mostly_used_translation\": \"primary Ukrainian translation (words only)\",\n" +
            $"  \"other_translation\": {(IsHighLevel(level) ? "null or \"secondary Ukrainian translation if a meaningful second meaning exists\"" : "null")},\n" +
            "  \"example_usage\": \"a natural example sentence in English\",\n" +
            "  \"example_usage_translation\": \"Ukrainian translation of the example sentence\"\n" +
            "}\n\n" +
            $"Rules:\n" +
            $"- Choose natural, useful everyday words a learner at {level} would need\n" +
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
                .Select(d => new PendingWordEntry
                {
                    Word                    = (d.Word ?? "").Trim().ToLowerInvariant(),
                    CefrLevel               = (d.CefrLevel ?? "").Trim().ToUpper(),
                    Synonym                 = (d.Synonym ?? "").Trim(),
                    Transcription           = (d.Transcription ?? "").Trim(),
                    MostlyUsedTranslation   = (d.MostlyUsedTranslation ?? "").Trim(),
                    OtherTranslation        = string.IsNullOrWhiteSpace(d.OtherTranslation) ? null : d.OtherTranslation.Trim(),
                    ExampleUsage            = (d.ExampleUsage ?? "").Trim(),
                    ExampleUsageTranslation = (d.ExampleUsageTranslation ?? "").Trim()
                })
                .Where(p => !string.IsNullOrEmpty(p.Word))
                .ToList();
        }
        catch
        {
            return [];
        }
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
