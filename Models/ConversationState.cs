namespace VocabifyBot.Models;

public sealed record PendingWordEntry
{
    public string Original { get; init; } = "";
    public string Translation { get; init; } = "";
    public string? EnglishLevel { get; init; }
}

public enum UserState
{
    None,
    AwaitingDisplayName,
    AwaitingStudentUsername,
    AwaitingWordsForStudent,
    AwaitingPersonalWords,
    AwaitingTopicChoice,
    AwaitingTopicName,
    AwaitingQuizCustomAmount,
    AwaitingSearchQuery,
    AwaitingGenTopic
}

public sealed class ConversationState
{
    public UserState State { get; set; } = UserState.None;
    public long? SelectedStudentId { get; set; }

    public List<PendingWordEntry> PendingWords { get; set; } = new();
    public long? PendingAddedByUserId { get; set; }
    public long? PendingForStudentId { get; set; }
    public string PendingTranslationText { get; set; } = "";

    public string? PoolLevel { get; set; }
    public int PoolCount { get; set; }
    public List<Word> PoolPreview { get; set; } = new();

    public string? QuizDirection { get; set; }
    public string? QuizLevel { get; set; }
    public string? QuizTopic { get; set; }
    public int QuizAmount { get; set; }
    public bool IsMistakesQuiz { get; set; }
    public List<Word> QuizWords { get; set; } = new();
    public int QuizIndex { get; set; }
    public int QuizScore { get; set; }
    public int QuizMessageId { get; set; }

    public List<string> CachedTopics { get; set; } = new();

    public List<Word> VocabWords { get; set; } = new();
    public int VocabPage { get; set; }
    public string VocabHeader { get; set; } = "";

    public long? BrowsingStudentId { get; set; }
    public string? BrowsingFilter { get; set; }
    public string? BrowsingMode { get; set; }
    public List<Word> BrowsingWords { get; set; } = new();
    public List<List<Word>> BrowsingGroups { get; set; } = new();
    public int BrowsingGroupIdx { get; set; }
    public int BrowsingOffset { get; set; }

    public string? GenLevel { get; set; }
    public int GenCount { get; set; }
    public string? GenTopic { get; set; }
    public List<PendingWordEntry> GenPreview { get; set; } = new();
}
