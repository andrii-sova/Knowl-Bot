using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VocabifyBot.Interfaces;

namespace VocabifyBot.Services;

public sealed class ClaudeService : IOpenAiService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-opus-4-7";

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
Translate English words or phrases into Ukrainian following EXACTLY this format for each entry (one entry per line):

[LEVEL] phrase — Ukrainian translation, synonyms; також: additional meaning [Ukrainian Cyrillic pronunciation] (Example sentence — Ukrainian translation)

Rules:
- Each entry must be on a SINGLE line — no line breaks inside an entry
- Start every line with the CEFR level in square brackets: [A0], [A1], [A2], [B1], [B2], [C1], or [C2]
- Pronunciation MUST be in square brackets [] using ONLY Ukrainian Cyrillic letters (e.g. [пут ю оф], [гет ап], [брейк даун])
- Include 1-3 short example sentences with translations in the same parentheses
- List all common Ukrainian meanings separated by commas; use 'також:' for secondary meanings
- Output ONLY the translated entries, nothing else

Example output:
[B2] put you off — відбити бажання, знеохотити, відвернути (від чогось); також: відкласти [пут ю оф] (The smell put me off my food — Запах відбив мені апетит; Don't let his comments put you off — Не дозволяй його коментарям знеохотити тебе)";

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

For each entry output a SINGLE line in EXACTLY this format:
[{level}] english phrase — Ukrainian translation, synonyms; також: secondary meaning [Ukrainian Cyrillic pronunciation] (Example sentence — Ukrainian translation)

Rules:
- One entry per line, no blank lines between entries
- Every line must start with [{level}]
- Pronunciation MUST be in square brackets [] using ONLY Ukrainian Cyrillic letters
- Include 1–2 short example sentences with Ukrainian translation in parentheses
- Choose natural, useful everyday words a learner at {level} would need{excludeClause}
- Output ONLY the word entries, no headers, numbers or extra text";

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
