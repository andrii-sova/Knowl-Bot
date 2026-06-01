using KnowlBot.Models;

namespace KnowlBot.Interfaces;

public interface IAiService
{
    Task<List<PendingWordEntry>> EnrichWordsAsync(string words);
    Task<string> DetectTopicAsync(string words);
    Task<List<PendingWordEntry>> GenerateWordsByLevelAsync(string level, int count, string? topic, IEnumerable<string> existingWords);
}
