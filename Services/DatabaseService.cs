using MongoDB.Driver;
using VocabifyBot.Interfaces;
using VocabifyBot.Models;

namespace VocabifyBot.Services;

public sealed class DatabaseService : IDatabaseService
{
    private readonly IMongoCollection<User>              _users;
    private readonly IMongoCollection<Word>              _words;
    private readonly IMongoCollection<WordStat>          _wordStats;
    private readonly IMongoCollection<TeacherStudent>    _teacherStudents;
    private readonly IMongoCollection<PendingInvitation> _pendingInvitations;

    public DatabaseService(IMongoDatabase db)
    {
        _users              = db.GetCollection<User>("users");
        _words              = db.GetCollection<Word>("words");
        _wordStats          = db.GetCollection<WordStat>("word_stats");
        _teacherStudents    = db.GetCollection<TeacherStudent>("teacher_students");
        _pendingInvitations = db.GetCollection<PendingInvitation>("pending_invitations");
    }

    public async Task InitializeAsync()
    {
        await _words.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<Word>(Builders<Word>.IndexKeys.Ascending(w => w.ForStudentId)),
            new CreateIndexModel<Word>(Builders<Word>.IndexKeys.Ascending(w => w.AddedByUserId)),
            new CreateIndexModel<Word>(Builders<Word>.IndexKeys.Combine(
                Builders<Word>.IndexKeys.Ascending(w => w.ForStudentId),
                Builders<Word>.IndexKeys.Ascending(w => w.EnglishLevel)))
        });

        await _wordStats.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<WordStat>(Builders<WordStat>.IndexKeys.Ascending(s => s.StudentId)),
            new CreateIndexModel<WordStat>(
                Builders<WordStat>.IndexKeys.Combine(
                    Builders<WordStat>.IndexKeys.Ascending(s => s.WordId),
                    Builders<WordStat>.IndexKeys.Ascending(s => s.StudentId)),
                new CreateIndexOptions { Unique = true })
        });

        await _teacherStudents.Indexes.CreateOneAsync(new CreateIndexModel<TeacherStudent>(
            Builders<TeacherStudent>.IndexKeys.Combine(
                Builders<TeacherStudent>.IndexKeys.Ascending(ts => ts.TeacherId),
                Builders<TeacherStudent>.IndexKeys.Ascending(ts => ts.StudentId)),
            new CreateIndexOptions { Unique = true }));

        await _pendingInvitations.Indexes.CreateOneAsync(new CreateIndexModel<PendingInvitation>(
            Builders<PendingInvitation>.IndexKeys.Combine(
                Builders<PendingInvitation>.IndexKeys.Ascending(p => p.TeacherId),
                Builders<PendingInvitation>.IndexKeys.Ascending(p => p.StudentUsername)),
            new CreateIndexOptions { Unique = true }));
    }

    public async Task<User?> GetUserAsync(long telegramId)
    {
        return await _users.Find(u => u.TelegramId == telegramId).FirstOrDefaultAsync();
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        var clean = username.TrimStart('@').Trim().ToLowerInvariant();
        return await _users.Find(u => u.Username == clean).FirstOrDefaultAsync();
    }

    public async Task UpsertUserAsync(User user)
    {
        var normalizedUsername = (user.Username ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();
        var existing = await _users.Find(u => u.TelegramId == user.TelegramId).FirstOrDefaultAsync();
        if (existing is null)
        {
            user.Username = normalizedUsername;
            user.CreatedAt = user.CreatedAt == default ? DateTime.UtcNow : user.CreatedAt;
            await _users.InsertOneAsync(user);
            return;
        }

        var update = Builders<User>.Update
            .Set(u => u.Username, normalizedUsername)
            .Set(u => u.FirstName, user.FirstName);

        await _users.UpdateOneAsync(u => u.TelegramId == user.TelegramId, update);
    }

    public async Task UpdateDisplayNameAsync(long userId, string? name)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await _users.UpdateOneAsync(
            u => u.TelegramId == userId,
            Builders<User>.Update.Set(u => u.DisplayNameOverride, clean));
    }

    public async Task<bool> IsStudentLinkedToAnyTeacherAsync(long studentId)
    {
        return await _teacherStudents.Find(ts => ts.StudentId == studentId).AnyAsync();
    }

    public async Task LinkTeacherStudentAsync(long teacherId, long studentId)
    {
        try
        {
            await _teacherStudents.InsertOneAsync(new TeacherStudent { TeacherId = teacherId, StudentId = studentId });
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
        }

        await _users.UpdateOneAsync(
            u => u.TelegramId == studentId,
            Builders<User>.Update.Set(u => u.IsActivated, true));
    }

    public async Task UnlinkTeacherStudentAsync(long teacherId, long studentId)
    {
        await _teacherStudents.DeleteOneAsync(ts => ts.TeacherId == teacherId && ts.StudentId == studentId);
    }

    public async Task<List<User>> GetStudentsForTeacherAsync(long teacherId)
    {
        var links = await _teacherStudents.Find(ts => ts.TeacherId == teacherId).ToListAsync();
        if (links.Count == 0)
        {
            return [];
        }

        var studentIds = links.Select(link => link.StudentId).ToList();
        var students = await _users.Find(Builders<User>.Filter.In(u => u.TelegramId, studentIds)).ToListAsync();
        var studentMap = students.ToDictionary(student => student.TelegramId);
        return studentIds.Select(id => studentMap.GetValueOrDefault(id)).OfType<User>().ToList();
    }

    public async Task AddPendingInvitationAsync(long teacherId, string studentUsername)
    {
        var clean = studentUsername.TrimStart('@').Trim().ToLowerInvariant();
        try
        {
            await _pendingInvitations.InsertOneAsync(new PendingInvitation
            {
                TeacherId = teacherId,
                StudentUsername = clean,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
        }
    }

    public async Task<List<PendingInvitation>> GetPendingInvitationsForTeacherAsync(long teacherId)
    {
        return await _pendingInvitations
            .Find(p => p.TeacherId == teacherId)
            .SortBy(p => p.StudentUsername)
            .ToListAsync();
    }

    public async Task ClaimPendingInvitationsAsync(long studentId, string username)
    {
        var clean = username.TrimStart('@').Trim().ToLowerInvariant();
        var pending = await _pendingInvitations.Find(p => p.StudentUsername == clean).ToListAsync();
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var invitation in pending)
        {
            try
            {
                await _teacherStudents.InsertOneAsync(new TeacherStudent
                {
                    TeacherId = invitation.TeacherId,
                    StudentId = studentId
                });
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
            }
        }

        await _users.UpdateOneAsync(
            u => u.TelegramId == studentId,
            Builders<User>.Update.Set(u => u.IsActivated, true));

        await _pendingInvitations.DeleteManyAsync(p => p.StudentUsername == clean);
    }

    public async Task RemovePendingInvitationAsync(long teacherId, string studentUsername)
    {
        var clean = studentUsername.TrimStart('@').Trim().ToLowerInvariant();
        await _pendingInvitations.DeleteOneAsync(p => p.TeacherId == teacherId && p.StudentUsername == clean);
    }

    public async Task SaveWordsAsync(IEnumerable<Word> words)
    {
        var list = words.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var batchTime = DateTime.UtcNow;
        foreach (var word in list)
        {
            word.CreatedAt = batchTime;
        }

        await _words.InsertManyAsync(list);
    }

    public async Task<List<Word>> GetWordsForBrowsingAsync(long teacherId, long studentId, string filter)
    {
        var fb = Builders<Word>.Filter;
        var query = fb.Eq(w => w.ForStudentId, studentId);
        query = filter switch
        {
            "teacher" => fb.And(query, fb.Eq(w => w.AddedByUserId, teacherId)),
            "student" => fb.And(query, fb.Eq(w => w.AddedByUserId, studentId)),
            _ => query
        };

        var words = await _words.Find(query).ToListAsync();
        return words
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenBy(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetWordsForStudentAsync(long studentId)
    {
        var words = await _words.Find(w => w.ForStudentId == studentId).ToListAsync();
        return words
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetPoolWordsAsync(long teacherId, long studentId, string? level, int count)
    {
        var alreadySentOriginals = await _words.Find(w => w.AddedByUserId == teacherId && w.ForStudentId == studentId)
            .Project(w => w.OriginalWord)
            .ToListAsync();

        var fb = Builders<Word>.Filter;
        var filter = fb.And(
            fb.Eq(w => w.AddedByUserId, teacherId),
            fb.Ne(w => w.ForStudentId, studentId),
            fb.Nin(w => w.OriginalWord, alreadySentOriginals));

        if (!string.IsNullOrEmpty(level))
        {
            filter = fb.And(filter, fb.Eq(w => w.EnglishLevel, level));
        }

        var candidates = await _words.Find(filter)
            .Project(w => new Word
            {
                OriginalWord = w.OriginalWord,
                Translation = w.Translation,
                EnglishLevel = w.EnglishLevel,
                Topic = w.Topic
            })
            .ToListAsync();

        var shuffled = candidates.OrderBy(_ => Random.Shared.Next()).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Word>();

        foreach (var candidate in shuffled)
        {
            if (!seen.Add(candidate.OriginalWord))
            {
                continue;
            }

            result.Add(candidate);
            if (result.Count >= count)
            {
                break;
            }
        }

        return result;
    }

    public async Task<List<Word>> GetWordsSentToStudentAsync(long teacherId, long studentId, int top = 50)
    {
        var words = await _words
            .Find(w => w.AddedByUserId == teacherId && w.ForStudentId == studentId)
            .ToListAsync();

        return words
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .Take(top)
            .ToList();
    }

    public async Task<List<Word>> GetWordsForQuizAsync(long studentId, string? level, string? topic, int count)
    {
        var fb = Builders<Word>.Filter;
        var filter = fb.Eq(w => w.ForStudentId, studentId);
        if (!string.IsNullOrEmpty(level))
        {
            filter = fb.And(filter, fb.Eq(w => w.EnglishLevel, level));
        }
        if (!string.IsNullOrEmpty(topic))
        {
            filter = fb.And(filter, fb.Eq(w => w.Topic, topic));
        }

        var raw = await _words.Find(filter).ToListAsync();
        var unique = raw
            .GroupBy(w => w.OriginalWord.ToLowerInvariant())
            .Select(group => group.OrderByDescending(w => w.CreatedAt).First())
            .ToList();

        if (unique.Count == 0)
        {
            return unique;
        }

        var ids = unique.Select(w => w.Id).ToList();
        var stats = await _wordStats
            .Find(s => s.StudentId == studentId && ids.Contains(s.WordId))
            .ToListAsync();
        var statMap = stats.ToDictionary(s => s.WordId);

        var normal = new List<Word>();
        var deprioritized = new List<Word>();

        foreach (var word in unique)
        {
            if (statMap.TryGetValue(word.Id, out var stat))
            {
                var total = stat.CorrectCount + stat.WrongCount;
                if (total >= 5)
                {
                    var accuracy = (double)stat.CorrectCount / total;
                    if (accuracy >= 0.8 || accuracy <= 0.2)
                    {
                        deprioritized.Add(word);
                        continue;
                    }
                }
            }

            normal.Add(word);
        }

        var result = normal.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        if (result.Count < count)
        {
            result.AddRange(deprioritized.OrderBy(_ => Random.Shared.Next()).Take(count - result.Count));
        }

        return result;
    }

    public async Task RecordQuizAnswerAsync(long studentId, string wordId, bool isCorrect)
    {
        var existing = await _wordStats.Find(s => s.WordId == wordId && s.StudentId == studentId).FirstOrDefaultAsync();
        if (existing is null)
        {
            try
            {
                await _wordStats.InsertOneAsync(new WordStat
                {
                    WordId = wordId,
                    StudentId = studentId,
                    CorrectCount = isCorrect ? 1 : 0,
                    WrongCount = isCorrect ? 0 : 1,
                    LastSeenAt = DateTime.UtcNow
                });
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
            }
            return;
        }

        var update = Builders<WordStat>.Update.Set(s => s.LastSeenAt, DateTime.UtcNow);
        update = isCorrect
            ? update.Inc(s => s.CorrectCount, 1)
            : update.Inc(s => s.WrongCount, 1);

        await _wordStats.UpdateOneAsync(s => s.Id == existing.Id, update);
    }

    public async Task<List<Word>> GetWordsForMistakesAsync(long studentId, int count)
    {
        var topStats = await _wordStats
            .Find(s => s.StudentId == studentId && s.WrongCount > 0)
            .SortByDescending(s => s.WrongCount)
            .Limit(count)
            .ToListAsync();

        if (topStats.Count == 0)
        {
            return [];
        }

        var wordIds = topStats.Select(s => s.WordId).ToList();
        var words = await _words.Find(Builders<Word>.Filter.In(w => w.Id, wordIds)).ToListAsync();
        var wordMap = words.ToDictionary(w => w.Id);

        return topStats
            .Select(stat => wordMap.GetValueOrDefault(stat.WordId))
            .OfType<Word>()
            .ToList();
    }

    public async Task ReduceWrongCountAsync(long studentId, string wordId)
    {
        var stat = await _wordStats.Find(s => s.WordId == wordId && s.StudentId == studentId).FirstOrDefaultAsync();
        if (stat is null)
        {
            return;
        }

        await _wordStats.UpdateOneAsync(
            s => s.Id == stat.Id,
            Builders<WordStat>.Update.Set(s => s.WrongCount, Math.Max(0, stat.WrongCount / 2)));
    }

    public async Task<List<string>> GetTopicsForStudentAsync(long studentId)
    {
        var topics = await _words.Find(w => w.ForStudentId == studentId && w.Topic != null)
            .Project(w => w.Topic!)
            .ToListAsync();

        return topics.Distinct().OrderBy(topic => topic).ToList();
    }

    public async Task<List<Word>> GetWordsByTopicAsync(long studentId, string topic)
    {
        var words = await _words.Find(w => w.ForStudentId == studentId && w.Topic == topic).ToListAsync();
        return words
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetWordsByLevelAsync(long studentId, string level, int top = 50)
    {
        var words = await _words.Find(w => w.ForStudentId == studentId && w.EnglishLevel == level)
            .SortBy(w => w.CreatedAt)
            .Limit(top)
            .ToListAsync();

        return words;
    }

    public async Task<List<string>> GetAllWordOriginalsAsync(long studentId)
    {
        var originals = await _words.Find(w => w.ForStudentId == studentId)
            .Project(w => w.OriginalWord)
            .ToListAsync();

        return originals.Distinct().ToList();
    }

    public async Task<List<Word>> SearchWordsAsync(long studentId, string query, int maxResults = 15)
    {
        var q = query.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q))
        {
            return [];
        }

        var all = await _words.Find(w => w.ForStudentId == studentId).ToListAsync();
        var substringHits = all
            .Where(w => w.OriginalWord.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || w.Translation.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .Take(maxResults)
            .ToList();

        if (substringHits.Count >= maxResults)
        {
            return substringHits;
        }

        var substringIds = substringHits.Select(w => w.Id).ToHashSet();
        var candidates = all.Where(w => !substringIds.Contains(w.Id)).ToList();

        var fuzzyHits = candidates
            .Select(w =>
            {
                var distance = Levenshtein(q, w.OriginalWord.ToLowerInvariant());
                return (Word: w, Distance: distance);
            })
            .Where(x => x.Distance <= AdaptiveThreshold(q.Length, x.Word.OriginalWord.Length))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Word.EnglishLevel ?? "Z")
            .Take(maxResults - substringHits.Count)
            .Select(x => x.Word)
            .ToList();

        return [.. substringHits, .. fuzzyHits];
    }

    public async Task DeleteWordsByLevelAsync(long studentId, string level)
    {
        var ids = await _words.Find(w => w.ForStudentId == studentId && w.EnglishLevel == level)
            .Project(w => w.Id)
            .ToListAsync();

        if (ids.Count == 0)
        {
            return;
        }

        await _words.DeleteManyAsync(w => w.ForStudentId == studentId && w.EnglishLevel == level);
        await _wordStats.DeleteManyAsync(Builders<WordStat>.Filter.In(s => s.WordId, ids));
    }

    public async Task DeleteWordsByIdsAsync(IEnumerable<string> wordIds)
    {
        var ids = wordIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await _words.DeleteManyAsync(Builders<Word>.Filter.In(w => w.Id, ids));
        await _wordStats.DeleteManyAsync(Builders<WordStat>.Filter.In(s => s.WordId, ids));
    }

    private static int AdaptiveThreshold(int queryLength, int wordLength)
    {
        var maxLength = Math.Max(queryLength, wordLength);
        return maxLength switch
        {
            <= 3 => 0,
            <= 5 => 1,
            <= 8 => 2,
            <= 12 => 3,
            _ => (int)(maxLength * 0.25)
        };
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }
        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        var curr = new int[b.Length + 1];

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }

            Array.Copy(curr, prev, curr.Length);
        }

        return prev[b.Length];
    }
}
