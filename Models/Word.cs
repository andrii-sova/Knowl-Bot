using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VocabifyBot.Models;

public class Word
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string   Id            { get; set; } = ObjectId.GenerateNewId().ToString();
    public string   OriginalWord  { get; set; } = "";
    public string   Translation   { get; set; } = "";
    public string?  Topic         { get; set; }
    public string?  EnglishLevel  { get; set; }
    [BsonRepresentation(BsonType.String)]
    public Guid?    BatchId       { get; set; }
    public long     AddedByUserId { get; set; }
    public long     ForStudentId  { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
}
