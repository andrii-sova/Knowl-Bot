namespace VocabifyBot.Models;

public class PendingWordEntry
{
    public string  Original     { get; set; } = "";
    public string  Translation  { get; set; } = "";
    public string? EnglishLevel { get; set; }
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
    AwaitingSearchQuery
}

public class ConversationState
{
    public UserState State             { get; set; } = UserState.None;
    public long?     SelectedStudentId { get; set; }

    // Pending words waiting to be saved after topic is decided
    public List<PendingWordEntry> PendingWords           { get; set; } = new();
    public long?                  PendingAddedByUserId   { get; set; }
    public long?                  PendingForStudentId    { get; set; }
    public string                 PendingTranslationText { get; set; } = "";

    // Assign from Pool state
    public string?            PoolLevel   { get; set; }  // null = any
    public int                PoolCount   { get; set; }
    public List<VocabifyBot.Models.Word> PoolPreview { get; set; } = new();

    // Quiz session
    public string?            QuizDirection { get; set; }  // "eu" (Eng→Ukr) | "ue" (Ukr→Eng)
    public string?            QuizLevel     { get; set; }  // null = any
    public string?            QuizTopic     { get; set; }  // null = any
    public int                QuizAmount    { get; set; }
    public List<VocabifyBot.Models.Word> QuizWords { get; set; } = new();
    public int                QuizIndex     { get; set; }
    public int                QuizScore     { get; set; }
    public int                QuizMessageId { get; set; }  // message ID of current question

    // Student vocab browsing (topic buttons)
    public List<string> CachedTopics { get; set; } = new();

    // Words Sent browsing session (teacher)
    public long?            BrowsingStudentId { get; set; }
    public string?          BrowsingFilter    { get; set; } // "teacher" | "student" | "both"
    public string?          BrowsingMode      { get; set; } // "topic" | "chunks" | "messages" | "all"
    public List<Word>       BrowsingWords     { get; set; } = new(); // flat sorted list
    public List<List<Word>> BrowsingGroups    { get; set; } = new(); // grouped (topic / message)
    public int              BrowsingGroupIdx  { get; set; } = 0;     // current group pointer
    public int              BrowsingOffset    { get; set; } = 0;     // chunk offset
}
