using VocabifyBot.Models;

namespace VocabifyBot.Interfaces;

public interface IDatabaseService
{
    Task InitializeAsync();
    Task<User?> GetUserAsync(long telegramId);
    Task<User?> GetUserByUsernameAsync(string username);
    Task UpsertUserAsync(User user);
    Task UpdateDisplayNameAsync(long userId, string? name);
    Task<bool> IsStudentLinkedToAnyTeacherAsync(long studentId);
    Task LinkTeacherStudentAsync(long teacherId, long studentId);
    Task UnlinkTeacherStudentAsync(long teacherId, long studentId);
    Task<List<User>> GetStudentsForTeacherAsync(long teacherId);
    Task AddPendingInvitationAsync(long teacherId, string studentUsername);
    Task<List<PendingInvitation>> GetPendingInvitationsForTeacherAsync(long teacherId);
    Task ClaimPendingInvitationsAsync(long studentId, string username);
    Task RemovePendingInvitationAsync(long teacherId, string studentUsername);
    Task DeleteWordsByLevelAsync(long studentId, string level);
    Task DeleteWordsByIdsAsync(IEnumerable<string> wordIds);
    Task SaveWordsAsync(IEnumerable<Word> words);
    Task<List<Word>> GetWordsForBrowsingAsync(long teacherId, long studentId, string filter);
    Task<List<Word>> GetWordsForStudentAsync(long studentId);
    Task<List<Word>> GetPoolWordsAsync(long teacherId, long studentId, string? level, int count);
    Task<List<Word>> GetWordsSentToStudentAsync(long teacherId, long studentId, int top = 50);
    Task<List<Word>> GetWordsForQuizAsync(long studentId, string? level, string? topic, int count);
    Task RecordQuizAnswerAsync(long studentId, string wordId, bool isCorrect);
    Task<List<string>> GetTopicsForStudentAsync(long studentId);
    Task<List<Word>> GetWordsByTopicAsync(long studentId, string topic);
    Task<List<Word>> GetWordsByLevelAsync(long studentId, string level, int top = 50);
    Task<List<string>> GetAllWordOriginalsAsync(long studentId);
    Task<List<Word>> SearchWordsAsync(long studentId, string query, int maxResults = 15);
    Task<List<Word>> GetWordsForMistakesAsync(long studentId, int count);
    Task ReduceWrongCountAsync(long studentId, string wordId);
}
