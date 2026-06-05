namespace KnowlBot.Models;

public sealed record PendingWordEntry
{
    public string  Word                    { get; init; } = "";
    public string  CefrLevel               { get; init; } = "";
    public string  Synonym                 { get; init; } = "";
    public string  Transcription           { get; init; } = "";
    public string  MostlyUsedTranslation   { get; init; } = "";
    public string? OtherTranslation        { get; init; }
    public string  ExampleUsage            { get; init; } = "";
    public string  ExampleUsageTranslation { get; init; } = "";
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
    AwaitingGenTopic,
    AwaitingStudentGenTopic,
    AwaitingWordRemoval,
    AwaitingWordDeleteInput
}

public sealed class ConversationState
{
    public UserState State { get; set; } = UserState.None;
    public long? SelectedStudentId { get; set; }

    public List<PendingWordEntry> PendingWords { get; set; } = new();
    public long? PendingAddedByUserId { get; set; }
    public long? PendingForStudentId { get; set; }

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
    public long? DeleteStudentId { get; set; }
    public List<Word> DeleteWords { get; set; } = new();
    public string? DeleteLevel { get; set; }
    public string? DeleteMode { get; set; } // "selected" | "keep"
    public string? BrowsingFilter { get; set; }
    public string? BrowsingMode { get; set; }
    public List<Word> BrowsingWords { get; set; } = new();
    public List<List<Word>> BrowsingGroups { get; set; } = new();
    public int BrowsingGroupIdx { get; set; }
    public int BrowsingOffset { get; set; }

    public List<string> GenSelectedLevels { get; set; } = new();
    public int GenLevelMessageId { get; set; }
    public int GenCount { get; set; }
    public string? GenTopic { get; set; }
    public List<PendingWordEntry> GenPreview { get; set; } = new();

    /// Levels sorted in CEFR order; "Any" when none selected.
    public string GenLevelDisplay
    {
        get
        {
            if (GenSelectedLevels.Count == 0) return "Any";
            var order = new[] { "A1", "A2", "B1", "B2", "C1", "C2" };
            return string.Join(", ", order.Where(GenSelectedLevels.Contains));
        }
    }
}
