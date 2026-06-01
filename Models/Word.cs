using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace KnowlBot.Models;

public class Word
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string   Id                      { get; set; } = ObjectId.GenerateNewId().ToString();
    public string   OriginalWord            { get; set; } = "";
    public string   CefrLevel               { get; set; } = "";
    public string   Synonym                 { get; set; } = "";
    public string   Transcription           { get; set; } = "";
    public string   MostlyUsedTranslation   { get; set; } = "";
    public string?  OtherTranslation        { get; set; }
    public string   ExampleUsage            { get; set; } = "";
    public string   ExampleUsageTranslation { get; set; } = "";
    public DateTime CreatedAt               { get; set; } = DateTime.UtcNow;

    // Populated at query time from UserWord; not persisted in DB
    [BsonIgnore] public string? Topic         { get; set; }
    [BsonIgnore] public long    AddedByUserId { get; set; }
    [BsonIgnore] public Guid?   BatchId       { get; set; }

    // Backward-compat computed properties used by handlers/quiz
    [BsonIgnore] public string? EnglishLevel => CefrLevel;
    [BsonIgnore] public string  Translation  =>
        OtherTranslation is not null
            ? $"{MostlyUsedTranslation}, {OtherTranslation}"
            : MostlyUsedTranslation;
}
