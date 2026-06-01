using MongoDB.Driver;
using KnowlBot.Interfaces;
using KnowlBot.Models;

namespace KnowlBot.Services;

public sealed class DatabaseService : IDatabaseService
{
    private readonly IMongoCollection<User>              _users;
    private readonly IMongoCollection<Word>              _words;
    private readonly IMongoCollection<UserWord>          _userWords;
    private readonly IMongoCollection<WordStat>          _wordStats;
    private readonly IMongoCollection<TeacherStudent>    _teacherStudents;
    private readonly IMongoCollection<PendingInvitation> _pendingInvitations;

    public DatabaseService(IMongoDatabase db)
    {
        _users              = db.GetCollection<User>("users");
        _words              = db.GetCollection<Word>("words");
        _userWords          = db.GetCollection<UserWord>("user_words");
        _wordStats          = db.GetCollection<WordStat>("word_stats");
        _teacherStudents    = db.GetCollection<TeacherStudent>("teacher_students");
        _pendingInvitations = db.GetCollection<PendingInvitation>("pending_invitations");
    }

    public async Task InitializeAsync()
    {
        // Global words: unique by normalised OriginalWord
        await _words.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<Word>(
                Builders<Word>.IndexKeys.Ascending(w => w.OriginalWord),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<Word>(
                Builders<Word>.IndexKeys.Ascending(w => w.CefrLevel))
        });

        // user_words: unique per (word, student)
        await _userWords.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<UserWord>(
                Builders<UserWord>.IndexKeys.Combine(
                    Builders<UserWord>.IndexKeys.Ascending(uw => uw.WordId),
                    Builders<UserWord>.IndexKeys.Ascending(uw => uw.UserId)),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<UserWord>(
                Builders<UserWord>.IndexKeys.Ascending(uw => uw.UserId)),
            new CreateIndexModel<UserWord>(
                Builders<UserWord>.IndexKeys.Ascending(uw => uw.AddedByUserId))
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

    public async Task SaveWordsFromEntriesAsync(
        IEnumerable<PendingWordEntry> entries, long addedById, long forStudentId, string? topic, Guid batchId)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;

        var now = DateTime.UtcNow;

        foreach (var entry in list)
        {
            var normalized = entry.Word.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalized)) continue;

            var wordId = await FindOrCreateWordAsync(entry, normalized, now);
            if (wordId is null) continue;

            try
            {
                await _userWords.InsertOneAsync(new UserWord
                {
                    WordId        = wordId,
                    UserId        = forStudentId,
                    AddedByUserId = addedById,
                    Topic         = topic,
                    BatchId       = batchId,
                    AddedAt       = now
                });
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Student already has this word — skip silently
            }
        }
    }

    private async Task<string?> FindOrCreateWordAsync(PendingWordEntry entry, string normalized, DateTime now)
    {
        var existing = await _words.Find(w => w.OriginalWord == normalized).FirstOrDefaultAsync();
        if (existing is not null) return existing.Id;

        var word = new Word
        {
            OriginalWord            = normalized,
            CefrLevel               = entry.CefrLevel,
            Synonym                 = entry.Synonym,
            Transcription           = entry.Transcription,
            MostlyUsedTranslation   = entry.MostlyUsedTranslation,
            OtherTranslation        = entry.OtherTranslation,
            ExampleUsage            = entry.ExampleUsage,
            ExampleUsageTranslation = entry.ExampleUsageTranslation,
            CreatedAt               = now
        };

        try
        {
            await _words.InsertOneAsync(word);
            return word.Id;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Race condition: another process inserted it first
            var raced = await _words.Find(w => w.OriginalWord == normalized).FirstOrDefaultAsync();
            return raced?.Id;
        }
    }

    public async Task AssignPoolWordsAsync(IEnumerable<Word> poolWords, long teacherId, long studentId, Guid batchId)
    {
        var now = DateTime.UtcNow;
        foreach (var word in poolWords)
        {
            try
            {
                await _userWords.InsertOneAsync(new UserWord
                {
                    WordId        = word.Id,
                    UserId        = studentId,
                    AddedByUserId = teacherId,
                    Topic         = word.Topic,
                    BatchId       = batchId,
                    AddedAt       = now
                });
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Already assigned — skip
            }
        }
    }

    public async Task<List<Word>> GetWordsForBrowsingAsync(long teacherId, long studentId, string filter)
    {
        var fb = Builders<UserWord>.Filter;
        var query = fb.Eq(uw => uw.UserId, studentId);
        query = filter switch
        {
            "teacher" => fb.And(query, fb.Eq(uw => uw.AddedByUserId, teacherId)),
            "student" => fb.And(query, fb.Eq(uw => uw.AddedByUserId, studentId)),
            _         => query
        };

        var userWords = await _userWords.Find(query).ToListAsync();
        if (userWords.Count == 0) return [];

        var wordIds = userWords.Select(uw => uw.WordId).ToList();
        var words   = await _words.Find(Builders<Word>.Filter.In(w => w.Id, wordIds)).ToListAsync();

        return HydrateWords(userWords, words)
            .OrderBy(w => w.CefrLevel ?? "Z")
            .ThenBy(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetWordsForStudentAsync(long studentId)
    {
        var userWords = await _userWords.Find(uw => uw.UserId == studentId).ToListAsync();
        if (userWords.Count == 0) return [];

        var wordIds = userWords.Select(uw => uw.WordId).ToList();
        var words   = await _words.Find(Builders<Word>.Filter.In(w => w.Id, wordIds)).ToListAsync();

        return HydrateWords(userWords, words)
            .OrderBy(w => w.CefrLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetPoolWordsAsync(long teacherId, long studentId, string? level, int count)
    {
        var teacherWordIds = await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.AddedByUserId, teacherId))
            .Project(uw => uw.WordId)
            .ToListAsync();

        if (teacherWordIds.Count == 0) return [];

        var studentWordIdSet = (await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId))
            .Project(uw => uw.WordId)
            .ToListAsync())
            .ToHashSet();

        var poolWordIds = teacherWordIds
            .Where(id => !studentWordIdSet.Contains(id))
            .Distinct()
            .ToList();

        if (poolWordIds.Count == 0) return [];

        var wordFilter = Builders<Word>.Filter.In(w => w.Id, poolWordIds);
        if (!string.IsNullOrEmpty(level))
            wordFilter = Builders<Word>.Filter.And(wordFilter, Builders<Word>.Filter.Eq(w => w.CefrLevel, level));

        var candidates = await _words.Find(wordFilter).ToListAsync();
        if (candidates.Count == 0) return [];

        var selected    = candidates.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        var selectedIds = selected.Select(w => w.Id).ToList();

        // Populate topic from the teacher's original user_word
        var teacherUserWords = await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.AddedByUserId, teacherId),
                Builders<UserWord>.Filter.In(uw => uw.WordId, selectedIds)))
            .ToListAsync();

        var topicMap = teacherUserWords
            .GroupBy(uw => uw.WordId)
            .ToDictionary(g => g.Key, g => g.First().Topic);

        foreach (var word in selected)
            word.Topic = topicMap.GetValueOrDefault(word.Id);

        return selected;
    }

    public async Task<List<Word>> GetWordsSentToStudentAsync(long teacherId, long studentId, int top = 50)
    {
        var userWords = await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.Eq(uw => uw.AddedByUserId, teacherId)))
            .SortByDescending(uw => uw.AddedAt)
            .Limit(top)
            .ToListAsync();

        if (userWords.Count == 0) return [];

        var wordIds = userWords.Select(uw => uw.WordId).ToList();
        var words   = await _words.Find(Builders<Word>.Filter.In(w => w.Id, wordIds)).ToListAsync();

        return HydrateWords(userWords, words)
            .OrderBy(w => w.CefrLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetWordsForQuizAsync(long studentId, string? level, string? topic, int count)
    {
        var fb = Builders<UserWord>.Filter;
        var uwFilter = fb.Eq(uw => uw.UserId, studentId);
        if (!string.IsNullOrEmpty(topic))
            uwFilter = fb.And(uwFilter, fb.Eq(uw => uw.Topic, topic));

        var userWords = await _userWords.Find(uwFilter).ToListAsync();
        if (userWords.Count == 0) return [];

        var wordIds    = userWords.Select(uw => uw.WordId).ToList();
        var wordFilter = Builders<Word>.Filter.In(w => w.Id, wordIds);
        if (!string.IsNullOrEmpty(level))
            wordFilter = Builders<Word>.Filter.And(wordFilter, Builders<Word>.Filter.Eq(w => w.CefrLevel, level));

        var words = await _words.Find(wordFilter).ToListAsync();
        if (words.Count == 0) return [];

        // Hydrate with user_word metadata
        var uwMap = userWords.ToDictionary(uw => uw.WordId);
        foreach (var word in words)
        {
            if (uwMap.TryGetValue(word.Id, out var uw))
            {
                word.Topic         = uw.Topic;
                word.AddedByUserId = uw.AddedByUserId;
                word.BatchId       = uw.BatchId;
                word.CreatedAt     = uw.AddedAt;
            }
        }

        var ids     = words.Select(w => w.Id).ToList();
        var stats   = await _wordStats.Find(s => s.StudentId == studentId && ids.Contains(s.WordId)).ToListAsync();
        var statMap = stats.ToDictionary(s => s.WordId);

        var normal       = new List<Word>();
        var deprioritized = new List<Word>();

        foreach (var word in words)
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
            result.AddRange(deprioritized.OrderBy(_ => Random.Shared.Next()).Take(count - result.Count));

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
        var userWordIdSet = (await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId))
            .Project(uw => uw.WordId)
            .ToListAsync())
            .ToHashSet();

        if (userWordIdSet.Count == 0) return [];

        var topStats = await _wordStats
            .Find(s => s.StudentId == studentId && s.WrongCount > 0)
            .SortByDescending(s => s.WrongCount)
            .Limit(count * 2)
            .ToListAsync();

        var validStats = topStats.Where(s => userWordIdSet.Contains(s.WordId)).Take(count).ToList();
        if (validStats.Count == 0) return [];

        var wordIds = validStats.Select(s => s.WordId).ToList();
        var words   = await _words.Find(Builders<Word>.Filter.In(w => w.Id, wordIds)).ToListAsync();

        var userWords = await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.In(uw => uw.WordId, wordIds)))
            .ToListAsync();

        return HydrateWords(userWords, words);
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
        var topics = await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.Ne(uw => uw.Topic, null)))
            .Project(uw => uw.Topic!)
            .ToListAsync();

        return topics.Distinct().OrderBy(t => t).ToList();
    }

    public async Task<List<Word>> GetWordsByTopicAsync(long studentId, string topic)
    {
        var userWords = await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.Eq(uw => uw.Topic, topic)))
            .ToListAsync();

        if (userWords.Count == 0) return [];

        var wordIds = userWords.Select(uw => uw.WordId).ToList();
        var words   = await _words.Find(Builders<Word>.Filter.In(w => w.Id, wordIds)).ToListAsync();

        return HydrateWords(userWords, words)
            .OrderBy(w => w.CefrLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .ToList();
    }

    public async Task<List<Word>> GetWordsByLevelAsync(long studentId, string level, int top = 50)
    {
        var userWordIds = await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId))
            .Project(uw => uw.WordId)
            .ToListAsync();

        if (userWordIds.Count == 0) return [];

        var words = await _words
            .Find(Builders<Word>.Filter.And(
                Builders<Word>.Filter.In(w => w.Id, userWordIds),
                Builders<Word>.Filter.Eq(w => w.CefrLevel, level)))
            .Limit(top)
            .ToListAsync();

        if (words.Count == 0) return [];

        var wordIdSet = words.Select(w => w.Id).ToList();
        var userWords = await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.In(uw => uw.WordId, wordIdSet)))
            .ToListAsync();

        return HydrateWords(userWords, words);
    }

    public async Task<List<string>> GetAllWordOriginalsAsync(long studentId)
    {
        var userWordIds = await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId))
            .Project(uw => uw.WordId)
            .ToListAsync();

        if (userWordIds.Count == 0) return [];

        return await _words
            .Find(Builders<Word>.Filter.In(w => w.Id, userWordIds))
            .Project(w => w.OriginalWord)
            .ToListAsync();
    }

    public async Task<List<Word>> SearchWordsAsync(long studentId, string query, int maxResults = 15)
    {
        var q = query.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q)) return [];

        var userWordIds = await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId))
            .Project(uw => uw.WordId)
            .ToListAsync();

        if (userWordIds.Count == 0) return [];

        var all = await _words.Find(Builders<Word>.Filter.In(w => w.Id, userWordIds)).ToListAsync();
        var userWordsMap = (await _userWords
            .Find(Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.In(uw => uw.WordId, userWordIds)))
            .ToListAsync())
            .ToDictionary(uw => uw.WordId);

        var substringHits = all
            .Where(w => w.OriginalWord.Contains(q, StringComparison.OrdinalIgnoreCase)
                     || (w.MostlyUsedTranslation ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                     || (w.OtherTranslation ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.CefrLevel ?? "Z")
            .Take(maxResults)
            .ToList();

        if (substringHits.Count >= maxResults)
            return HydrateWords(substringHits.Select(w => userWordsMap[w.Id]), substringHits);

        var substringIds = substringHits.Select(w => w.Id).ToHashSet();
        var fuzzyHits = all.Where(w => !substringIds.Contains(w.Id))
            .Select(w => (Word: w, Distance: Levenshtein(q, w.OriginalWord.ToLowerInvariant())))
            .Where(x => x.Distance <= AdaptiveThreshold(q.Length, x.Word.OriginalWord.Length))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Word.CefrLevel ?? "Z")
            .Take(maxResults - substringHits.Count)
            .Select(x => x.Word)
            .ToList();

        var combined = substringHits.Concat(fuzzyHits).ToList();
        var uwCombined = combined.Where(w => userWordsMap.ContainsKey(w.Id)).Select(w => userWordsMap[w.Id]);
        return HydrateWords(uwCombined, combined);
    }

    public async Task DeleteWordsByIdsAsync(IEnumerable<string> wordIds, long studentId)
    {
        var ids = wordIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count == 0) return;

        await _userWords.DeleteManyAsync(
            Builders<UserWord>.Filter.And(
                Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId),
                Builders<UserWord>.Filter.In(uw => uw.WordId, ids)));

        await _wordStats.DeleteManyAsync(
            Builders<WordStat>.Filter.And(
                Builders<WordStat>.Filter.Eq(s => s.StudentId, studentId),
                Builders<WordStat>.Filter.In(s => s.WordId, ids)));
    }

    public async Task DeleteWordsByLevelAsync(long studentId, string level)
    {
        var userWordIds = await _userWords
            .Find(Builders<UserWord>.Filter.Eq(uw => uw.UserId, studentId))
            .Project(uw => uw.WordId)
            .ToListAsync();

        if (userWordIds.Count == 0) return;

        var matchingIds = await _words
            .Find(Builders<Word>.Filter.And(
                Builders<Word>.Filter.In(w => w.Id, userWordIds),
                Builders<Word>.Filter.Eq(w => w.CefrLevel, level)))
            .Project(w => w.Id)
            .ToListAsync();

        if (matchingIds.Count == 0) return;

        await DeleteWordsByIdsAsync(matchingIds, studentId);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static List<Word> HydrateWords(IEnumerable<UserWord> userWords, IEnumerable<Word> globalWords)
    {
        var wordMap = globalWords.ToDictionary(w => w.Id);
        var result  = new List<Word>();

        foreach (var uw in userWords)
        {
            if (!wordMap.TryGetValue(uw.WordId, out var gw)) continue;

            result.Add(new Word
            {
                Id                      = gw.Id,
                OriginalWord            = gw.OriginalWord,
                CefrLevel               = gw.CefrLevel,
                Synonym                 = gw.Synonym,
                Transcription           = gw.Transcription,
                MostlyUsedTranslation   = gw.MostlyUsedTranslation,
                OtherTranslation        = gw.OtherTranslation,
                ExampleUsage            = gw.ExampleUsage,
                ExampleUsageTranslation = gw.ExampleUsageTranslation,
                CreatedAt               = uw.AddedAt,
                Topic                   = uw.Topic,
                AddedByUserId           = uw.AddedByUserId,
                BatchId                 = uw.BatchId
            });
        }

        return result;
    }

    private static int AdaptiveThreshold(int queryLength, int wordLength)
    {
        var maxLength = Math.Max(queryLength, wordLength);
        return maxLength switch
        {
            <= 3  => 0,
            <= 5  => 1,
            <= 8  => 2,
            <= 12 => 3,
            _     => (int)(maxLength * 0.25)
        };
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

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
