using MongoDB.Bson.Serialization.Attributes;

namespace VocabifyBot.Models;

public class User
{
    [BsonId]
    public long     TelegramId          { get; set; }
    public string   Username            { get; set; } = "";
    public string   FirstName           { get; set; } = "";
    public string?  DisplayNameOverride { get; set; }
    public string   Role                { get; set; } = "";
    public bool     IsActivated         { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(DisplayNameOverride) ? DisplayNameOverride :
        !string.IsNullOrEmpty(Username)                 ? $"{FirstName} (@{Username})" :
        FirstName;
}
