using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace KnowlBot.Models;

public class UserWord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string   Id            { get; set; } = ObjectId.GenerateNewId().ToString();
    public string   WordId        { get; set; } = "";
    public long     UserId        { get; set; }
    public long     AddedByUserId { get; set; }
    public string?  Topic         { get; set; }
    [BsonRepresentation(BsonType.String)]
    public Guid?    BatchId       { get; set; }
    public DateTime AddedAt       { get; set; } = DateTime.UtcNow;
}
