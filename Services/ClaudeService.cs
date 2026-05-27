using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VocabifyBot.Interfaces;

namespace VocabifyBot.Services;

public sealed class ClaudeService : IOpenAiService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-haiku-4-5";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _http;

    public ClaudeService(string apiKey)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private const string TranslationPrompt = @"You are an English-to-Ukrainian dictionary assistant for language learners.
Translate English words or phrases into Ukrainian following EXACTLY this format (one entry per line):

[LEVEL] english word (english synonym) — primary Ukrainian translation; також: secondary Ukrainian translation [IPA transcription] (English example sentence — Ukrainian translation)

STRICT rules:
- ONE entry per line, no line breaks inside an entry
- CEFR level in square brackets: [A1], [A2], [B1], [B2], [C1], or [C2]
- Exactly ONE English synonym in round brackets after the word
- Dash — separates English from Ukrainian
- Exactly ONE primary Ukrainian translation; use 'також:' for one secondary meaning
- Pronunciation in square brackets using standard IPA transcription with stress mark ˈ (e.g. [dɪˈmɪnɪʃ], [ˌʌndəˈstænd], [ˈhæpɪ])
- Exactly ONE example sentence with its Ukrainian translation in round brackets at the end
- Output ONLY the entries, nothing else

Example:
[B2] diminish (decrease) — зменшувати; також: применшувати [dɪˈmɪnɪʃ] (Her enthusiasm did not diminish over time — Її ентузіазм не зменшився з часом)";

    public async Task<string> TranslateWordsAsync(string words)
    {
        return await SendAsync(
            TranslationPrompt,
            $"Translate these words/phrases (one per line):\n{words.Trim()}");
    }

    public async Task<string> DetectTopicAsync(string words)
    {
        return await SendAsync(
            "Identify the most fitting topic/category for the given English words or phrases. " +
            "Reply with 2-4 words only (e.g. 'Phrasal Verbs', 'Business English', 'B2 Vocabulary', " +
            "'Adjectives', 'Travel & Transport'). No explanation, just the topic name.",
            words.Trim());
    }

    public async Task<string> GenerateWordsByLevelAsync(
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
            $@"You are an English vocabulary generator for Ukrainian learners.
Generate exactly {count} English words or phrases at CEFR level {level}.{topicClause}

Each entry MUST follow EXACTLY this format (one entry per line):
[{level}] english word (english synonym) — primary Ukrainian translation; також: secondary Ukrainian translation [IPA transcription] (English example sentence — Ukrainian translation)

STRICT rules:
- ONE entry per line, no blank lines, no line breaks inside an entry
- Every line starts with [{level}]
- Exactly ONE English synonym in round brackets after the word
- Exactly ONE primary Ukrainian translation; use 'також:' for one secondary meaning
- Pronunciation in square brackets using standard IPA transcription with stress mark ˈ (e.g. [dɪˈmɪnɪʃ], [ˌʌndəˈstænd], [ˈhæpɪ])
- Exactly ONE example sentence with its Ukrainian translation in round brackets at the end
- Choose natural, useful everyday words a learner at {level} would need{excludeClause}
- Output ONLY the entries, no headers, numbers or extra text

Example:
[B2] diminish (decrease) — зменшувати; також: применшувати [dɪˈmɪnɪʃ] (Her enthusiasm did not diminish over time — Її ентузіазм не зменшився з часом)";

        return await SendAsync(systemPrompt, $"Generate {count} {level} words.");
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
}
