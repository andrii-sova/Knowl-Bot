using MongoDB.Bson.Serialization.Attributes;

namespace KnowlBot.Models;

[BsonIgnoreExtraElements]
public class User
{
    [BsonId]
    public long            TelegramId  { get; set; }
    public string          Username    { get; set; } = "";
    public string          FirstName   { get; set; } = "";
    public string          Role        { get; set; } = "";
    public bool            IsActivated { get; set; }
    public DateTime        CreatedAt   { get; set; } = DateTime.UtcNow;
    public AccountSettings Settings    { get; set; } = new();

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Settings.DisplayNameOverride) ? Settings.DisplayNameOverride :
        !string.IsNullOrEmpty(Username)                          ? $"{FirstName} (@{Username})" :
        FirstName;
}
