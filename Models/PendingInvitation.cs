using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VocabifyBot.Models;

public class PendingInvitation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string   Id              { get; set; } = ObjectId.GenerateNewId().ToString();
    public long     TeacherId       { get; set; }
    public string   StudentUsername { get; set; } = "";
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;
}
