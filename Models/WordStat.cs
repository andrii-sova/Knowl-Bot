using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VocabifyBot.Models;

public class WordStat
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string   Id           { get; set; } = ObjectId.GenerateNewId().ToString();
    [BsonRepresentation(BsonType.ObjectId)]
    public string   WordId       { get; set; } = "";
    public long     StudentId    { get; set; }
    public int      CorrectCount { get; set; }
    public int      WrongCount   { get; set; }
    public DateTime LastSeenAt   { get; set; }
}
