using Anthropic;
using Anthropic.Models.Messages;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowlBot.Interfaces;
using KnowlBot.Models;

namespace KnowlBot.Services;

public sealed class ClaudeService : IAiService
{
    private const string Model = "claude-haiku-4-5";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AnthropicClient _client;

    public ClaudeService(string apiKey)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
    }

    private const string EnrichPrompt =
        "You are a certified English-Ukrainian translator and lexicographer with 15 years of professional experience. " +
        "You specialize in capturing the authentic, idiomatic meaning of words and phrases — not literal word-for-word translations.\n\n" +
        "You receive a list of English words or phrases, one per line.\n\n" +
        "Your job:\n" +
        "1. Extract only the English word/phrase from each line — IGNORE any translations, symbols, dashes, or annotations that may be present.\n" +
        "2. For each extracted word or phrase, produce a structured enriched entry.\n\n" +
        "CRITICAL for phrases and idioms: Translate the REAL meaning, not the literal words. " +
        "Example: 'I'm high' → 'я під кайфом' (not 'я високо'). " +
        "'Break a leg' → 'ні пуху ні пера' (not 'зламай ногу'). " +
        "'It's raining cats and dogs' → 'ллє як із відра'. " +
        "Always ask yourself: what does a native speaker actually mean when they say this?\n\n" +
        "Return ONLY a minified JSON array (no spaces/indentation). Each object must have EXACTLY these fields:\n" +
        "{\"w\":\"word/phrase (lowercase)\",\"l\":\"A1|A2|B1|B2|C1|C2\",\"s\":\"ONE synonym — single word — NO commas\",\"tr\":\"IPA no brackets e.g. dɪˈmɪnɪʃ\",\"m\":\"ONE primary Ukrainian equivalent — no commas\",\"o\":null or \"ONE secondary Ukrainian meaning — B2/C1/C2 only if genuinely different — else null\",\"ex\":\"max 6 words\",\"et\":\"Ukrainian translation of example\"}\n\n" +
        "Rules:\n" +
        "- Use Cambridge Dictionary as the authoritative reference\n" +
        "- For idioms/slang/phrasal expressions: translate the idiomatic meaning, not the literal words\n" +
        "- Translations MUST be in standard literary Ukrainian — NEVER use Russian or русизми\n" +
        "- m MUST be a single word or short phrase — NEVER list alternatives\n" +
        "- o MUST be null for A1/A2/B1 — no exceptions\n" +
        "- o MUST be null if it's just a synonym of m\n" +
        "- o MUST be Ukrainian ONLY — NEVER English — null if you cannot provide a genuinely different Ukrainian meaning\n" +
        "- et MUST be a Ukrainian string for A1/A2/B1 words — NEVER null for these levels\n" +
        "- et MUST be null for B2/C1/C2 words — no exceptions\n" +
        "- Output ONLY the JSON array, nothing else";

    private const int EnrichBatchSize = 10;

    public async Task<List<PendingWordEntry>> EnrichWordsAsync(string words)
    {
        var lines = words
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count <= EnrichBatchSize)
        {
            var response = await SendAsync(EnrichPrompt, $"Enrich these words (one per line):\n{string.Join('\n', lines)}");
            return ParseWordEntries(response);
        }

        // Process in batches to avoid API payload/timeout errors
        var results = new List<PendingWordEntry>();
        foreach (var batch in lines.Chunk(EnrichBatchSize))
        {
            var response = await SendAsync(EnrichPrompt, $"Enrich these words (one per line):\n{string.Join('\n', batch)}");
            results.AddRange(ParseWordEntries(response));
        }
        return results;
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
            $"You are a certified English-Ukrainian translator and lexicographer with 15 years of professional experience. " +
            $"You specialize in authentic, idiomatic translation — not literal word-for-word equivalents.\n" +
            $"Generate exactly {count} English words or phrases at CEFR level {level}.{topicClause}\n\n" +
            $"Return ONLY a minified JSON array (no spaces/indentation). Each object must have EXACTLY these fields:\n" +
            $"{{\"w\":\"word/phrase (lowercase)\",\"l\":\"{level}\",\"s\":\"ONE synonym — single word — NO commas\",\"tr\":\"IPA no brackets e.g. dɪˈmɪnɪʃ\",\"m\":\"ONE primary Ukrainian equivalent — no commas\",\"o\":{(IsHighLevel(level) ? "null or \"ONE secondary Ukrainian meaning — only if genuinely different — else null\"" : "null")},\"ex\":\"max 6 words\"{(IsHighLevel(level) ? "" : ",\\\"et\\\":\\\"Ukrainian translation of example\\\"")}  }}\n\n" +
            $"Rules:\n" +
            $"- Choose natural, useful everyday words a learner at {level} would need\n" +
            $"- For idioms/slang/phrasal expressions: translate the idiomatic meaning, not literal words\n" +
            $"- Use Cambridge Dictionary as the authoritative reference\n" +
            $"- Translations MUST be in standard literary Ukrainian — NEVER use Russian or русизми\n" +
            $"- m MUST be a single word or short phrase — NEVER list alternatives\n" +
            (IsHighLevel(level)
                ? "- o is ONLY for a genuinely different second Ukrainian meaning — null if it's a synonym of m\n" +
                  "- o MUST be Ukrainian ONLY — NEVER English — null if no genuinely different Ukrainian meaning exists\n" +
                  "- Do NOT include et field\n"
                : "- o MUST be null — no exceptions for this level\n" +
                  "- et MUST be a Ukrainian string — NEVER null or missing\n") +
            $"- Output ONLY the JSON array{excludeClause}";

        var response = await SendAsync(systemPrompt, $"Generate EXACTLY {count} {level} words. Output EXACTLY {count} JSON objects — no more, no less.");
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

    // claude-haiku-4-5 pricing per million tokens
    private const double InputCostPerMToken  = 0.80;
    private const double OutputCostPerMToken = 4.00;

    private async Task<string> SendAsync(string systemPrompt, string userMessage)
    {
        var message = await _client.Messages.Create(new()
        {
            Model = Model,
            MaxTokens = 4096,
            System = systemPrompt,
            Messages = new List<MessageParam>
            {
                new() { Role = Role.User, Content = userMessage }
            }
        });

        var u = message.Usage;
        double cost = (u.InputTokens  * InputCostPerMToken +
                       u.OutputTokens * OutputCostPerMToken)
                      / 1_000_000.0;

        Console.WriteLine(
            $"[WARNING] Claude cost: ${cost:F6} | in={u.InputTokens} out={u.OutputTokens}");

        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
                return textBlock.Text.Trim();
        }
        return string.Empty;
    }

    private sealed record WordEntryDto(
        [property: JsonPropertyName("w")]  string? Word,
        [property: JsonPropertyName("l")]  string? CefrLevel,
        [property: JsonPropertyName("s")]  string? Synonym,
        [property: JsonPropertyName("tr")] string? Transcription,
        [property: JsonPropertyName("m")]  string? MostlyUsedTranslation,
        [property: JsonPropertyName("o")]  string? OtherTranslation,
        [property: JsonPropertyName("ex")] string? ExampleUsage,
        [property: JsonPropertyName("et")] string? ExampleUsageTranslation
    );
}
