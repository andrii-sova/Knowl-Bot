using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VocabifyBot.Models;

public class TeacherStudent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id        { get; set; } = ObjectId.GenerateNewId().ToString();
    public long   TeacherId { get; set; }
    public long   StudentId { get; set; }
}
