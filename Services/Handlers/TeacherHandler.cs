using VocabifyBot.Interfaces;
using VocabifyBot.Models;
using VocabifyBot.UI;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace VocabifyBot.Services.Handlers;

public sealed class TeacherHandler : HandlerBase
{
    private const int ChunkSize = 20;

    public TeacherHandler(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states)
        : base(bot, db, states)
    {
    }

    public async Task HandleCallbackAsync(string data, long userId, long chatId, CancellationToken ct)
    {
        switch (data)
        {
            case "menu_add_student":
                SetState(userId, new ConversationState { State = UserState.AwaitingStudentUsername });
                await Bot.SendMessage(
                    chatId,
                    "👤 Enter your student's Telegram username (with or without @):",
                    replyMarkup: Keyboards.BackButton("back_from_add_student"),
                    cancellationToken: ct);
                return;
            case "back_from_add_student":
                ResetState(userId);
                await SendMenuAsync(chatId, "Teacher", ct);
                return;
            case "menu_send_words":
                await ShowStudentSelectionAsync(userId, chatId, "send", ct);
                return;
            case "menu_my_students":
                await ShowMyStudentsAsync(userId, chatId, ct);
                return;
            case "menu_words_sent":
                await ShowStudentSelectionAsync(userId, chatId, "words_sent", ct);
                return;
            case "menu_remove_student":
                await ShowStudentSelectionAsync(userId, chatId, "remove", ct);
                return;
            case "type_words":
                await PromptManualWordsAsync(userId, chatId, ct);
                return;
            case "pool_start":
                await SendPoolLevelSelectionAsync(userId, chatId, ct);
                return;
            case "pool_shuffle":
                await FetchAndShowPoolPreviewAsync(userId, chatId, ct);
                return;
            case "pool_confirm":
                await ConfirmPoolPreviewAsync(userId, chatId, ct);
                return;
            case "pool_cancel":
                ResetState(userId);
                await SendMenuAsync(chatId, "Teacher", ct);
                return;
            case "browse_next":
                await HandleBrowseNextAsync(userId, chatId, ct);
                return;
            case "browse_cancel":
                await CancelBrowsingAsync(userId, chatId, ct);
                return;
            case "back_from_send_words":
                ResetState(userId);
                await ShowStudentSelectionAsync(userId, chatId, "send", ct);
                return;
            case "menu_search":
                await ShowSearchStudentSelectionAsync(userId, chatId, ct);
                return;
        }

        if (data.StartsWith("search_for_"))
        {
            await HandleSearchStudentSelectedAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("send_to_"))
        {
            await HandleStudentSelectedAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("pool_level_"))
        {
            await HandlePoolLevelAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("pool_count_"))
        {
            await HandlePoolCountAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("words_sent_to_"))
        {
            await HandleWordsSentStudentAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("wfilter_"))
        {
            await HandleWordFilterAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("wmode_"))
        {
            await HandleWordModeAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("remove_student_"))
        {
            await HandleRemoveStudentAsync(chatId, data, ct);
            return;
        }

        if (data.StartsWith("confirm_remove_"))
        {
            await HandleConfirmRemoveAsync(userId, chatId, data, ct);
        }
    }

    private async Task ShowStudentSelectionAsync(long teacherId, long chatId, string mode, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);
        if (students.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                "You have no students yet. Use *Add Student* first.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        var prefix = mode switch
        {
            "send" => "send_to_",
            "remove" => "remove_student_",
            _ => "words_sent_to_"
        };

        var title = mode switch
        {
            "send" => "👥 Choose a student to send vocabulary to:",
            "remove" => "👥 Choose a student to remove:",
            _ => "👥 Choose a student to view words sent:"
        };

        await Bot.SendMessage(chatId, title, replyMarkup: Keyboards.StudentList(students, prefix), cancellationToken: ct);
    }

    private async Task ShowMyStudentsAsync(long teacherId, long chatId, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);
        var pending  = await Db.GetPendingInvitationsForTeacherAsync(teacherId);

        if (students.Count == 0 && pending.Count == 0)
        {
            await Bot.SendMessage(chatId, "You have no students yet. Use *Add Student* to invite someone.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        var lines = new System.Text.StringBuilder("👥 *Your students:*\n\n");
        var i = 1;
        foreach (var s in students)
            lines.AppendLine($"{i++}. ✅ {WordFormatter.EscapeMarkdown(s.DisplayName)}");
        foreach (var p in pending)
            lines.AppendLine($"{i++}. ⏳ @{WordFormatter.EscapeMarkdown(p.StudentUsername)} _(awaiting activation)_");

        await Bot.SendMessage(chatId, lines.ToString().TrimEnd(),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task HandleStudentSelectedAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var studentId = long.Parse(data["send_to_".Length..]);
        var student = await Db.GetUserAsync(studentId);

        SetState(userId, new ConversationState { SelectedStudentId = studentId });
        await Bot.SendMessage(
            chatId,
            $"📤 Sending vocabulary to *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}*\n\nHow would you like to add words?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.SendWordChoice(),
            cancellationToken: ct);
    }

    private async Task PromptManualWordsAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var student = await Db.GetUserAsync(state.SelectedStudentId.Value);
        SetState(userId, new ConversationState
        {
            State = UserState.AwaitingWordsForStudent,
            SelectedStudentId = state.SelectedStudentId
        });

        await Bot.SendMessage(
            chatId,
            $"✏️ Send vocabulary for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? state.SelectedStudentId.Value.ToString())}* (one word/phrase per line):",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.BackButton("back_from_send_words"),
            cancellationToken: ct);
    }

    private async Task SendPoolLevelSelectionAsync(long userId, long chatId, CancellationToken ct)
    {
        if (GetState(userId).SelectedStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        await Bot.SendMessage(
            chatId,
            "🎯 *Assign from Pool*\n\nSelect a CEFR level to filter words:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.CefrLevelButtons("pool_level_"),
            cancellationToken: ct);
    }

    private async Task HandlePoolLevelAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        MutateState(userId, state => state.PoolLevel = data["pool_level_".Length..] == "any" ? null : data["pool_level_".Length..]);
        await Bot.SendMessage(chatId, "📦 How many words would you like to assign?", replyMarkup: Keyboards.PoolCountButtons(), cancellationToken: ct);
    }

    private async Task HandlePoolCountAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        if (!int.TryParse(data["pool_count_".Length..], out var count))
        {
            return;
        }

        MutateState(userId, state => state.PoolCount = count);
        await FetchAndShowPoolPreviewAsync(userId, chatId, ct);
    }

    private async Task FetchAndShowPoolPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var words = await Db.GetPoolWordsAsync(userId, state.SelectedStudentId.Value, state.PoolLevel, state.PoolCount);
        if (words.Count == 0)
        {
            var levelNote = state.PoolLevel is not null ? $" at *{state.PoolLevel}*" : string.Empty;
            await Bot.SendMessage(
                chatId,
                $"📭 No new words found in your pool{levelNote} for this student.\n\nTry a different level or count.",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.PoolEmptyState(),
                cancellationToken: ct);
            return;
        }

        state.PoolPreview = words;
        SetState(userId, state);

        var student = await Db.GetUserAsync(state.SelectedStudentId.Value);
        var levelLabel = state.PoolLevel is not null ? $" *[{state.PoolLevel}]*" : string.Empty;
        var preview = string.Join("\n\n", words.Select(WordFormatter.FormatWordLine));

        await Bot.SendMessage(
            chatId,
            $"🎯 Preview — {words.Count} words{levelLabel} for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}*:\n\n{preview}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.PoolPreviewButtons(),
            cancellationToken: ct);
    }

    private async Task ConfirmPoolPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null || state.PoolPreview.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var batchId = Guid.NewGuid();
        var wordsToSave = state.PoolPreview.Select(word => new Word
        {
            OriginalWord = word.OriginalWord,
            Translation = word.Translation,
            EnglishLevel = word.EnglishLevel,
            Topic = word.Topic,
            BatchId = batchId,
            AddedByUserId = userId,
            ForStudentId = state.SelectedStudentId.Value
        });

        await Db.SaveWordsAsync(wordsToSave);

        var teacher = await Db.GetUserAsync(userId);
        var student = await Db.GetUserAsync(state.SelectedStudentId.Value);
        var levelLabel = state.PoolLevel is not null ? $" [{state.PoolLevel}]" : string.Empty;

        await Bot.SendMessage(
            chatId,
            $"✅ {state.PoolPreview.Count} words assigned to *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}*!",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        try
        {
            var body = string.Join("\n\n", state.PoolPreview.Select(WordFormatter.FormatWordLine));
            await Bot.SendMessage(
                state.SelectedStudentId.Value,
                $"📚 New vocabulary from {WordFormatter.EscapeMarkdown(teacher?.DisplayName ?? "your teacher")}{WordFormatter.EscapeMarkdown(levelLabel)}:\n\n{body}",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch
        {
        }

        ResetState(userId);
        await SendMenuAsync(chatId, "Teacher", ct);
    }

    private async Task HandleWordsSentStudentAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var studentId = long.Parse(data["words_sent_to_".Length..]);
        var student = await Db.GetUserAsync(studentId);

        MutateState(userId, state => state.BrowsingStudentId = studentId);
        await Bot.SendMessage(
            chatId,
            $"🔍 What words do you want to see for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}*?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.WordFilterSelection(),
            cancellationToken: ct);
    }

    private async Task HandleWordFilterAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        MutateState(userId, state => state.BrowsingFilter = data["wfilter_".Length..]);
        await Bot.SendMessage(chatId, "📂 How would you like to view the words?", replyMarkup: Keyboards.WordModeSelection(), cancellationToken: ct);
    }

    private async Task HandleWordModeAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.BrowsingStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var mode = data["wmode_".Length..];
        var words = await Db.GetWordsForBrowsingAsync(userId, state.BrowsingStudentId.Value, state.BrowsingFilter ?? "both");
        if (words.Count == 0)
        {
            await Bot.SendMessage(chatId, "📭 No words found for the selected filter.", cancellationToken: ct);
            await SendMenuAsync(chatId, "Teacher", ct);
            return;
        }

        state.BrowsingMode = mode;
        state.BrowsingWords = words;
        state.BrowsingOffset = 0;
        state.BrowsingGroupIdx = 0;
        state.BrowsingGroups = mode switch
        {
            "topic" => GroupByTopic(words),
            "messages" => GroupByBatch(words),
            _ => new List<List<Word>>()
        };
        SetState(userId, state);

        await SendBrowsingPageAsync(userId, chatId, ct);
    }

    private async Task HandleBrowseNextAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.BrowsingMode == "chunks")
        {
            state.BrowsingOffset += ChunkSize;
        }
        else
        {
            state.BrowsingGroupIdx += 1;
        }

        SetState(userId, state);
        await SendBrowsingPageAsync(userId, chatId, ct);
    }

    private async Task CancelBrowsingAsync(long userId, long chatId, CancellationToken ct)
    {
        MutateState(userId, state =>
        {
            state.BrowsingStudentId = null;
            state.BrowsingFilter = null;
            state.BrowsingMode = null;
            state.BrowsingWords = new List<Word>();
            state.BrowsingGroups = new List<List<Word>>();
            state.BrowsingOffset = 0;
            state.BrowsingGroupIdx = 0;
        });

        await SendMenuAsync(chatId, "Teacher", ct);
    }

    private async Task SendBrowsingPageAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        string header;
        List<Word> page;
        bool hasMore;

        if (state.BrowsingMode == "chunks")
        {
            var offset = state.BrowsingOffset;
            page = state.BrowsingWords.Skip(offset).Take(ChunkSize).ToList();
            hasMore = offset + ChunkSize < state.BrowsingWords.Count;
            header = $"📦 Words {offset + 1}–{offset + page.Count} of {state.BrowsingWords.Count}";
        }
        else if (state.BrowsingMode == "all")
        {
            await SendAllWordsAsync(chatId, state.BrowsingWords, ct);
            await SendMenuAsync(chatId, "Teacher", ct);
            return;
        }
        else
        {
            var groups = state.BrowsingGroups;
            var index = state.BrowsingGroupIdx;
            if (index >= groups.Count)
            {
                await Bot.SendMessage(chatId, "✅ No more groups.", cancellationToken: ct);
                await SendMenuAsync(chatId, "Teacher", ct);
                return;
            }

            page = groups[index];
            hasMore = index + 1 < groups.Count;
            header = state.BrowsingMode == "topic"
                ? $"🏷️ Topic: *{WordFormatter.EscapeMarkdown(page[0].Topic ?? "No topic")}* ({page.Count} words) — group {index + 1}/{groups.Count}"
                : $"💬 Message {index + 1}/{groups.Count} — {page[0].CreatedAt:dd MMM yyyy HH:mm} ({page.Count} words)";
        }

        var body = string.Join("\n\n", page.Select(WordFormatter.FormatWordLine));
        await Bot.SendMessage(
            chatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.BrowseNavigation(hasMore),
            cancellationToken: ct);
    }

    private async Task SendAllWordsAsync(long chatId, IReadOnlyList<Word> words, CancellationToken ct)
    {
        for (var i = 0; i < words.Count; i += ChunkSize)
        {
            var slice = words.Skip(i).Take(ChunkSize).ToList();
            var header = $"📋 Words {i + 1}–{i + slice.Count} of {words.Count}";
            var body = string.Join("\n\n", slice.Select(WordFormatter.FormatWordLine));

            await Bot.SendMessage(
                chatId,
                $"{header}\n\n{body}",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
    }

    private async Task HandleRemoveStudentAsync(long chatId, string data, CancellationToken ct)
    {
        var studentId = long.Parse(data["remove_student_".Length..]);
        var student = await Db.GetUserAsync(studentId);

        await Bot.SendMessage(
            chatId,
            $"⚠️ Remove *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}* from your student list?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ConfirmRemoveStudent(studentId),
            cancellationToken: ct);
    }

    private async Task HandleConfirmRemoveAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var studentId = long.Parse(data["confirm_remove_".Length..]);
        var student = await Db.GetUserAsync(studentId);

        await Db.UnlinkTeacherStudentAsync(userId, studentId);
        await Bot.SendMessage(
            chatId,
            $"✅ *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}* has been removed from your student list.",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await SendMenuAsync(chatId, "Teacher", ct);
    }

    private static List<List<Word>> GroupByTopic(List<Word> words) =>
        words
            .GroupBy(word => word.Topic ?? string.Empty)
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToList();

    private static List<List<Word>> GroupByBatch(List<Word> words) =>
        words
            .GroupBy(word => word.BatchId ?? Guid.Empty)
            .OrderBy(group => group.First().CreatedAt)
            .Select(group => group.ToList())
            .ToList();

    // ── Search ────────────────────────────────────────────────────────────────

    private async Task ShowSearchStudentSelectionAsync(long teacherId, long chatId, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);
        if (students.Count == 0)
        {
            await Bot.SendMessage(chatId,
                "You have no students yet. Use *Add Student* first.",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        await Bot.SendMessage(
            chatId,
            "🔍 Choose a student to search vocabulary for:",
            replyMarkup: Keyboards.StudentList(students, "search_for_"),
            cancellationToken: ct);
    }

    private async Task HandleSearchStudentSelectedAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var studentId = long.Parse(data["search_for_".Length..]);
        var student   = await Db.GetUserAsync(studentId);

        SetState(userId, new ConversationState
        {
            State             = UserState.AwaitingSearchQuery,
            SelectedStudentId = studentId
        });

        await Bot.SendMessage(
            chatId,
            $"🔍 Searching vocabulary for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}*\n\nType the word or phrase to search:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.BackButton("back_to_menu"),
            cancellationToken: ct);
    }

    public async Task HandleSearchQueryAsync(long userId, long chatId, string query, CancellationToken ct)
    {
        var state     = GetState(userId);
        var studentId = state.SelectedStudentId;
        if (studentId is null) { await GoMenuAsync(userId, chatId, ct); return; }

        var results = await Db.SearchWordsAsync(studentId.Value, query);
        ResetState(userId);

        var navigation = Keyboards.TeacherSearchResultNavigation(studentId.Value);

        if (results.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                $"🔍 No results found for *{WordFormatter.EscapeMarkdown(query)}*.",
                parseMode: ParseMode.Markdown,
                replyMarkup: navigation,
                cancellationToken: ct);
            return;
        }

        var student = await Db.GetUserAsync(studentId.Value);
        var body    = string.Join("\n\n", results.Select(WordFormatter.FormatWordLine));

        await Bot.SendMessage(
            chatId,
            $"🔍 *{results.Count}* result(s) for *{WordFormatter.EscapeMarkdown(query)}* in {WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}'s vocabulary:\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: navigation,
            cancellationToken: ct);
    }
}
