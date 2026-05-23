using VocabifyBot.Data;
using VocabifyBot.Interfaces;
using VocabifyBot.Models;
using Microsoft.EntityFrameworkCore;

namespace VocabifyBot.Services;

public class DatabaseService : IDatabaseService
{
    private readonly DbContextOptions<EnglishBotDbContext> _options;

    public DatabaseService(DbContextOptions<EnglishBotDbContext> options)
    {
        _options = options;
    }

    private EnglishBotDbContext Ctx() => new EnglishBotDbContext(_options);

    public async Task InitializeAsync()
    {
        await using var ctx = Ctx();
        await ctx.Database.EnsureCreatedAsync();
    }

    // ── Users ────────────────────────────────────────────────────────────────

    public async Task<User?> GetUserAsync(long telegramId)
    {
        await using var ctx = Ctx();
        return await ctx.Users.FindAsync(telegramId);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        await using var ctx = Ctx();
        var clean = username.TrimStart('@').Trim().ToLower();
        return await ctx.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == clean);
    }

    public async Task UpsertUserAsync(User user)
    {
        await using var ctx = Ctx();
        var existing = await ctx.Users.FindAsync(user.TelegramId);
        if (existing is null)
        {
            user.CreatedAt = DateTime.Now;
            ctx.Users.Add(user);
        }
        else
        {
            existing.Username  = user.Username;
            existing.FirstName = user.FirstName;
        }
        await ctx.SaveChangesAsync();
    }

    public async Task UpdateDisplayNameAsync(long userId, string? name)
    {
        await using var ctx = Ctx();
        var user = await ctx.Users.FindAsync(userId);
        if (user is null) return;
        user.DisplayNameOverride = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await ctx.SaveChangesAsync();
    }

    // ── Teacher ↔ Student links ──────────────────────────────────────────────

    public async Task<bool> IsStudentLinkedToAnyTeacherAsync(long studentId)
    {
        await using var ctx = Ctx();
        return await ctx.TeacherStudents.AnyAsync(ts => ts.StudentId == studentId);
    }

    public async Task LinkTeacherStudentAsync(long teacherId, long studentId)
    {
        await using var ctx = Ctx();
        var exists = await ctx.TeacherStudents
            .AnyAsync(ts => ts.TeacherId == teacherId && ts.StudentId == studentId);
        if (!exists)
        {
            ctx.TeacherStudents.Add(new TeacherStudent { TeacherId = teacherId, StudentId = studentId });
            await ctx.SaveChangesAsync();
        }
    }

    public async Task UnlinkTeacherStudentAsync(long teacherId, long studentId)
    {
        await using var ctx = Ctx();
        var link = await ctx.TeacherStudents
            .FirstOrDefaultAsync(ts => ts.TeacherId == teacherId && ts.StudentId == studentId);
        if (link is not null)
        {
            ctx.TeacherStudents.Remove(link);
            await ctx.SaveChangesAsync();
        }
    }

    public async Task<List<User>> GetStudentsForTeacherAsync(long teacherId)
    {
        await using var ctx = Ctx();
        return await ctx.TeacherStudents
            .Where(ts => ts.TeacherId == teacherId)
            .Select(ts => ts.Student)
            .ToListAsync();
    }

    // ── Pending invitations ───────────────────────────────────────────────────

    public async Task AddPendingInvitationAsync(long teacherId, string studentUsername)
    {
        await using var ctx = Ctx();
        var clean = studentUsername.TrimStart('@').Trim().ToLower();
        var exists = await ctx.PendingInvitations
            .AnyAsync(p => p.TeacherId == teacherId && p.StudentUsername == clean);
        if (!exists)
        {
            ctx.PendingInvitations.Add(new PendingInvitation
            {
                TeacherId       = teacherId,
                StudentUsername = clean,
                CreatedAt       = DateTime.Now
            });
            await ctx.SaveChangesAsync();
        }
    }

    public async Task<List<PendingInvitation>> GetPendingInvitationsForTeacherAsync(long teacherId)
    {
        await using var ctx = Ctx();
        return await ctx.PendingInvitations
            .Where(p => p.TeacherId == teacherId)
            .OrderBy(p => p.StudentUsername)
            .ToListAsync();
    }

    /// <summary>
    /// When a student activates their account, link them to any teacher who has a pending invitation for their username.
    /// </summary>
    public async Task ClaimPendingInvitationsAsync(long studentId, string username)
    {
        await using var ctx = Ctx();
        var clean = username.TrimStart('@').Trim().ToLower();
        var pending = await ctx.PendingInvitations
            .Where(p => p.StudentUsername == clean)
            .ToListAsync();

        foreach (var inv in pending)
        {
            var alreadyLinked = await ctx.TeacherStudents
                .AnyAsync(ts => ts.TeacherId == inv.TeacherId && ts.StudentId == studentId);
            if (!alreadyLinked)
                ctx.TeacherStudents.Add(new TeacherStudent
                {
                    TeacherId = inv.TeacherId,
                    StudentId = studentId
                });
        }

        ctx.PendingInvitations.RemoveRange(pending);
        await ctx.SaveChangesAsync();
    }

    public async Task RemovePendingInvitationAsync(long teacherId, string studentUsername)
    {
        await using var ctx = Ctx();
        var clean = studentUsername.TrimStart('@').Trim().ToLower();
        var inv = await ctx.PendingInvitations
            .FirstOrDefaultAsync(p => p.TeacherId == teacherId && p.StudentUsername == clean);
        if (inv is not null)
        {
            ctx.PendingInvitations.Remove(inv);
            await ctx.SaveChangesAsync();
        }
    }



    public async Task SaveWordsAsync(IEnumerable<Word> words)
    {
        await using var ctx = Ctx();
        var batch = DateTime.Now;
        foreach (var w in words)
        {
            w.CreatedAt = batch;
            ctx.Words.Add(w);
        }
        await ctx.SaveChangesAsync();
    }

    /// <summary>Loads words for a student filtered by who added them.</summary>
    /// <param name="filter">"teacher" | "student" | "both"</param>
    public async Task<List<Word>> GetWordsForBrowsingAsync(long teacherId, long studentId, string filter)
    {
        await using var ctx = Ctx();
        var query = ctx.Words.Where(w => w.ForStudentId == studentId);
        query = filter switch
        {
            "teacher" => query.Where(w => w.AddedByUserId == teacherId),
            "student" => query.Where(w => w.AddedByUserId == studentId),
            _         => query
        };
        return await query.OrderBy(w => w.EnglishLevel ?? "Z").ThenBy(w => w.CreatedAt).ToListAsync();
    }

    /// <summary>Words in a student's vocabulary (sent by teacher or added personally).</summary>
    public async Task<List<Word>> GetWordsForStudentAsync(long studentId, int top = 50)
    {
        await using var ctx = Ctx();
        return await ctx.Words
            .Where(w => w.ForStudentId == studentId)
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .Take(top)
            .ToListAsync();
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> random words from the teacher's word pool
    /// that have NOT yet been assigned to <paramref name="studentId"/>.
    /// Optionally filtered by CEFR level.
    /// </summary>
    public async Task<List<Word>> GetPoolWordsAsync(long teacherId, long studentId, string? level, int count)
    {
        await using var ctx = Ctx();

        // IDs of words already sent to this student by this teacher
        var alreadySentOriginals = await ctx.Words
            .Where(w => w.AddedByUserId == teacherId && w.ForStudentId == studentId)
            .Select(w => w.OriginalWord)
            .ToListAsync();

        var query = ctx.Words
            .Where(w => w.AddedByUserId == teacherId
                     && w.ForStudentId  != studentId
                     && !alreadySentOriginals.Contains(w.OriginalWord));

        if (!string.IsNullOrEmpty(level))
            query = query.Where(w => w.EnglishLevel == level);

        // Load candidates into memory and shuffle for randomness
        var candidates = await query
            .Select(w => new { w.OriginalWord, w.Translation, w.EnglishLevel, w.Topic })
            .Distinct()
            .ToListAsync();

        // Deduplicate by OriginalWord (keep first occurrence after shuffle)
        var rng = new Random();
        var shuffled = candidates.OrderBy(_ => rng.Next()).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Word>();
        foreach (var c in shuffled)
        {
            if (!seen.Add(c.OriginalWord)) continue;
            result.Add(new Word
            {
                OriginalWord = c.OriginalWord,
                Translation  = c.Translation,
                EnglishLevel = c.EnglishLevel,
                Topic        = c.Topic
            });
            if (result.Count >= count) break;
        }
        return result;
    }

    public async Task<List<Word>> GetWordsSentToStudentAsync(long teacherId, long studentId, int top = 50)
    {
        await using var ctx = Ctx();
        return await ctx.Words
            .Where(w => w.AddedByUserId == teacherId && w.ForStudentId == studentId)
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .Take(top)
            .ToListAsync();
    }

    // ── Quiz ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns up to <paramref name="count"/> words for a quiz, using smart selection:
    /// prioritises words in the "learning zone" (0.2–0.8 accuracy after ≥5 attempts),
    /// deprioritises highly-mastered (≥0.8) and highly-struggling (≤0.2) ones.
    /// </summary>
    public async Task<List<Word>> GetWordsForQuizAsync(
        long studentId, string? level, string? topic, int count)
    {
        await using var ctx = Ctx();

        var query = ctx.Words.Where(w => w.ForStudentId == studentId);
        if (!string.IsNullOrEmpty(level)) query = query.Where(w => w.EnglishLevel == level);
        if (!string.IsNullOrEmpty(topic)) query = query.Where(w => w.Topic == topic);

        var raw = await query.ToListAsync();

        // Deduplicate — keep the most recent entry for each original word
        var unique = raw
            .GroupBy(w => w.OriginalWord.ToLower())
            .Select(g => g.OrderByDescending(w => w.CreatedAt).First())
            .ToList();

        if (unique.Count == 0) return unique;

        // Load accuracy stats
        var ids   = unique.Select(w => w.Id).ToList();
        var stats = await ctx.WordStats
            .Where(s => s.StudentId == studentId && ids.Contains(s.WordId))
            .ToDictionaryAsync(s => s.WordId);

        var rng          = new Random();
        var normal       = new List<Word>();
        var deprioritized = new List<Word>();

        foreach (var w in unique)
        {
            if (stats.TryGetValue(w.Id, out var stat))
            {
                var total = stat.CorrectCount + stat.WrongCount;
                if (total >= 5)
                {
                    var acc = (double)stat.CorrectCount / total;
                    if (acc >= 0.8 || acc <= 0.2) { deprioritized.Add(w); continue; }
                }
            }
            normal.Add(w);
        }

        // Shuffle both pools
        normal        = normal.OrderBy(_ => rng.Next()).ToList();
        deprioritized = deprioritized.OrderBy(_ => rng.Next()).ToList();

        // Fill from normal first, pad with deprioritized only if needed
        var result = normal.Take(count).ToList();
        if (result.Count < count)
            result.AddRange(deprioritized.Take(count - result.Count));

        return result;
    }

    /// <summary>Upserts a quiz answer stat for a specific word and student.</summary>
    public async Task RecordQuizAnswerAsync(long studentId, int wordId, bool isCorrect)
    {
        await using var ctx = Ctx();
        var stat = await ctx.WordStats
            .FirstOrDefaultAsync(s => s.StudentId == studentId && s.WordId == wordId);

        if (stat is null)
        {
            ctx.WordStats.Add(new WordStat
            {
                WordId       = wordId,
                StudentId    = studentId,
                CorrectCount = isCorrect ? 1 : 0,
                WrongCount   = isCorrect ? 0 : 1,
                LastSeenAt   = DateTime.Now
            });
        }
        else
        {
            if (isCorrect) stat.CorrectCount++;
            else           stat.WrongCount++;
            stat.LastSeenAt = DateTime.Now;
        }
        await ctx.SaveChangesAsync();
    }

    public async Task<List<string>> GetTopicsForStudentAsync(long studentId)
    {
        await using var ctx = Ctx();
        return await ctx.Words
            .Where(w => w.ForStudentId == studentId && w.Topic != null)
            .Select(w => w.Topic!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }

    /// <summary>Words for a student filtered by topic.</summary>
    public async Task<List<Word>> GetWordsByTopicAsync(long studentId, string topic, int top = 50)
    {
        await using var ctx = Ctx();
        return await ctx.Words
            .Where(w => w.ForStudentId == studentId && w.Topic == topic)
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .ThenByDescending(w => w.CreatedAt)
            .Take(top)
            .ToListAsync();
    }

    /// <summary>
    /// Fuzzy word search using a two-pass strategy:
    ///   1. SQL-side pre-filter with LIKE for substring matches (fast index scan).
    ///   2. In-memory Levenshtein on remaining words with an adaptive distance
    ///      threshold that scales with word length so short words require closer
    ///      matches than long ones.
    /// Results are ordered: exact substring hits first, then fuzzy hits by distance,
    /// both groups sorted by CEFR level.
    /// </summary>
    public async Task<List<Word>> SearchWordsAsync(long studentId, string query, int maxResults = 15)
    {
        await using var ctx = Ctx();
        var q = query.Trim().ToLower();
        if (string.IsNullOrEmpty(q)) return [];

        // Pass 1 — SQL: substring match (uses index seek on varchar column).
        var substringHits = await ctx.Words
            .Where(w => w.ForStudentId == studentId &&
                        (w.OriginalWord.ToLower().Contains(q) || w.Translation.ToLower().Contains(q)))
            .OrderBy(w => w.EnglishLevel ?? "Z")
            .Take(maxResults)
            .ToListAsync();

        if (substringHits.Count >= maxResults)
            return substringHits;

        // Pass 2 — in-memory Levenshtein on words NOT already in the substring results.
        var substringIds = substringHits.Select(w => w.Id).ToHashSet();

        var candidates = await ctx.Words
            .Where(w => w.ForStudentId == studentId && !substringIds.Contains(w.Id))
            .ToListAsync();

        var fuzzyHits = candidates
            .Select(w =>
            {
                var orig = w.OriginalWord.ToLower();
                var dist = Levenshtein(q, orig);
                return (Word: w, Distance: dist);
            })
            .Where(x => x.Distance <= AdaptiveThreshold(q.Length, x.Word.OriginalWord.Length))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Word.EnglishLevel ?? "Z")
            .Take(maxResults - substringHits.Count)
            .Select(x => x.Word)
            .ToList();

        return [.. substringHits, .. fuzzyHits];
    }

    /// <summary>
    /// Maximum allowed Levenshtein distance based on the lengths of query and target word.
    /// Short words get a tight threshold; longer words allow proportionally more edits.
    /// </summary>
    private static int AdaptiveThreshold(int queryLen, int wordLen)
    {
        var maxLen = Math.Max(queryLen, wordLen);
        return maxLen switch
        {
            <= 3  => 0,   // "cat" must match exactly
            <= 5  => 1,   // "house" allows 1 typo
            <= 8  => 2,   // "morning" allows 2 typos
            <= 12 => 3,   // "comfortable" allows 3 typos
            _     => (int)(maxLen * 0.25) // 25% of longer word length
        };
    }

    /// <summary>Standard iterative Levenshtein distance (O(n*m) time, O(n) space).</summary>
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
