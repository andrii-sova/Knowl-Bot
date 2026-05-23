namespace VocabifyBot.Models;

public class User
{
    public long     TelegramId          { get; set; }
    public string   Username            { get; set; } = "";
    public string   FirstName           { get; set; } = "";
    public string?  DisplayNameOverride { get; set; }      // user-set custom display name
    public string   Role                { get; set; } = "";
    public bool     IsActivated         { get; set; }      // true once a teacher has ever linked this student
    public DateTime CreatedAt           { get; set; }

    // Navigation
    public List<TeacherStudent> LinkedStudents { get; set; } = new();
    public List<TeacherStudent> LinkedTeachers { get; set; } = new();
    public List<Word>           WordsSent      { get; set; } = new();
    public List<Word>           WordsReceived  { get; set; } = new();

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(DisplayNameOverride) ? DisplayNameOverride :
        !string.IsNullOrEmpty(Username)                 ? $"{FirstName} (@{Username})" :
        FirstName;
}

