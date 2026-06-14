using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.UI;

namespace KnowlBot.Services.Handlers;

public sealed class TeacherHandler(
    ITelegramBotClient bot,
    IDatabaseService db,
    ConversationStateManager states,
    IAiService openAi)
    : HandlerBase(bot, db, states)
{
    private const int ChunkSize = 20;
    private IAiService OpenAi { get; } = openAi;

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
            case "gen_start":
                await SendGenLevelSelectionAsync(userId, chatId, ct);
                return;
            case "gen_topic_skip":
                MutateState(userId, state =>
                {
                    state.State = UserState.None;
                    state.GenTopic = null;
                });
                await GenerateAndShowPreviewAsync(userId, chatId, ct);
                return;
            case "gen_remove":
                await HandleGenRemoveAsync(userId, chatId, ct);
                return;
            case "gen_confirm":
                await ConfirmGenPreviewAsync(userId, chatId, ct);
                return;
            case "gen_cancel":
                ResetState(userId);
                await SendMenuAsync(chatId, "Teacher", ct);
                return;
            case "browse_next":
                await HandleBrowseNextAsync(userId, chatId, ct);
                return;
            case "browse_prev":
                await HandleBrowsePrevAsync(userId, chatId, ct);
                return;
            case "browse_cancel":
                await CancelBrowsingAsync(userId, chatId, ct);
                return;
            case "back_from_send_words":
                ResetState(userId);
                await ShowStudentSelectionAsync(userId, chatId, "send", ct);
                return;
            case "menu_delete_words":
                await ShowDeleteWordsStudentSelectionAsync(userId, chatId, ct);
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

        if (data.StartsWith("gen_level_toggle_"))
        {
            await HandleGenLevelToggleAsync(userId, chatId, data["gen_level_toggle_".Length..], ct);
            return;
        }

        if (data == "gen_level_done")
        {
            await HandleGenLevelDoneAsync(userId, chatId, ct);
            return;
        }

        if (data.StartsWith("gen_count_"))
        {
            await HandleGenCountAsync(userId, chatId, data, ct);
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

        if (data.StartsWith("wtopic_"))
        {
            await HandleTopicSelectedAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("wlevel_"))
        {
            await HandleLevelBrowseSelectedAsync(userId, chatId, data["wlevel_".Length..], ct);
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

        if (data.StartsWith("delwords_student_"))
        {
            await HandleDeleteWordsStudentSelectedAsync(userId, chatId, data, ct);
            return;
        }

        if (data == "delwords_level")
        {
            await Bot.SendMessage(chatId, "🔤 Select the CEFR level:", replyMarkup: Keyboards.DeleteWordsByLevelButtons(), cancellationToken: ct);
            return;
        }

        if (data.StartsWith("delwords_lvl_"))
        {
            await HandleDeleteByLevelPreviewAsync(userId, chatId, data["delwords_lvl_".Length..], ct);
            return;
        }

        if (data == "delwords_confirm")
        {
            await HandleDeleteConfirmAsync(userId, chatId, ct);
            return;
        }

        if (data == "delwords_pick_delete")
        {
            MutateState(userId, s => { s.State = UserState.AwaitingWordDeleteInput; s.DeleteMode = "selected"; });
            await Bot.SendMessage(chatId,
                "✂️ Type the *numbers* of the words you want to *DELETE* (e.g. `1,3,5`):",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.BackButton("back_to_menu"),
                cancellationToken: ct);
            return;
        }

        if (data == "delwords_pick_keep")
        {
            MutateState(userId, s => { s.State = UserState.AwaitingWordDeleteInput; s.DeleteMode = "keep"; });
            await Bot.SendMessage(chatId,
                "✅ Type the *numbers* of the words you want to *KEEP* (e.g. `1,3,5`). All others will be deleted:",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.BackButton("back_to_menu"),
                cancellationToken: ct);
            return;
        }

        if (data.StartsWith("gexp_"))
        {
            if (int.TryParse(data["gexp_".Length..], out var idx))
                await HandleWordExpandAsync(userId, chatId, idx, ct);
        }
    }

    private async Task HandleWordExpandAsync(long userId, long chatId, int idx, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.ActiveWordMessageId == 0 || state.ActiveWordLines.Count == 0) return;

        MutateState(userId, s =>
        {
            if (s.ExpandedWordIndices.Contains(idx)) s.ExpandedWordIndices.Remove(idx);
            else s.ExpandedWordIndices.Add(idx);
        });

        state = GetState(userId);
        var body = BuildExpandableBody(state.ActiveWordLines, state.ExpandedWordIndices, state.WordsExpandedByDefault);
        var text = $"{state.ActiveWordHeader}\n\n{body}";

        try
        {
            await Bot.EditMessageText(
                chatId,
                state.ActiveWordMessageId,
                text,
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.ExpandableWordKeyboard(state.ActiveWordLines.Count, state.ExpandedWordIndices, "gexp_", Keyboards.GenPreviewActionRows()),
                cancellationToken: ct);
        }
        catch { }
    }

    public async Task HandleGenTopicInputAsync(long userId, long chatId, string topic, CancellationToken ct)
    {
        MutateState(userId, state =>
        {
            state.State = UserState.None;
            state.GenTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
        });

        await GenerateAndShowPreviewAsync(userId, chatId, ct);
    }

    public async Task HandleSearchQueryAsync(long userId, long chatId, string query, CancellationToken ct)
    {
        var state = GetState(userId);
        var studentId = state.SelectedStudentId;
        if (studentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

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
        var teacher = await Db.GetUserAsync(userId);
        var expandByDefault = teacher?.Settings.WordsExpandedByDefault ?? false;
        var entries = results.Select(WordFormatter.ToDisplayEntry).ToList();
        var expanded = InitialExpandedSet(entries.Count, expandByDefault);
        var body = BuildExpandableBody(entries, expanded, expandByDefault);

        await Bot.SendMessage(
            chatId,
            $"🔍 *{results.Count}* result(s) for *{WordFormatter.EscapeMarkdown(query)}* in {WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}'s vocabulary:\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "gexp_", navigation.InlineKeyboard.Select(r => r.ToArray()).ToArray()),
            cancellationToken: ct);
    }

    private async Task ShowStudentSelectionAsync(long teacherId, long chatId, string mode, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);

        if (mode == "send" || mode == "words_sent")
        {
            var prefix = mode == "send" ? "send_to_" : "words_sent_to_";
            var myselfLabel = mode == "send" ? "📚 Myself" : "📖 My Words";
            var title = mode == "send" ? "👥 Choose a recipient:" : "👥 Choose a student to view words sent:";

            var rows = new List<InlineKeyboardButton[]>
            {
                new[] { InlineKeyboardButton.WithCallbackData(myselfLabel, $"{prefix}{teacherId}") }
            };
            foreach (var s in students)
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(s.DisplayName, $"{prefix}{s.TelegramId}") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "back_to_menu") });

            await Bot.SendMessage(chatId, title, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
            return;
        }

        if (students.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                "You have no students yet. Use *Add Student* first.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        // mode == "remove" only reaches here
        await Bot.SendMessage(chatId, "👥 Choose a student to remove:", replyMarkup: Keyboards.StudentList(students, "remove_student_"), cancellationToken: ct);
    }

    private async Task ShowMyStudentsAsync(long teacherId, long chatId, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);
        var pending = await Db.GetPendingInvitationsForTeacherAsync(teacherId);

        if (students.Count == 0 && pending.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                "You have no students yet. Use *Add Student* to invite someone.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        var lines = new StringBuilder("👥 *Your students:*\n\n");
        var i = 1;
        foreach (var student in students)
        {
            lines.AppendLine($"{i++}. ✅ {WordFormatter.EscapeMarkdown(student.DisplayName)}");
        }

        foreach (var invitation in pending)
        {
            lines.AppendLine($"{i++}. ⏳ @{WordFormatter.EscapeMarkdown(invitation.StudentUsername)} _(awaiting activation)_");
        }

        await Bot.SendMessage(chatId, lines.ToString().TrimEnd(), parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task HandleStudentSelectedAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        if (!long.TryParse(data["send_to_".Length..], out var studentId))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        SetState(userId, new ConversationState { SelectedStudentId = studentId });

        var messageText = studentId == userId
            ? "📚 Adding words to *your personal vocabulary*\n\nHow would you like to add words?"
            : $"📤 Sending vocabulary to *{WordFormatter.EscapeMarkdown((await Db.GetUserAsync(studentId))?.DisplayName ?? studentId.ToString())}*\n\nHow would you like to add words?";

        await Bot.SendMessage(
            chatId,
            messageText,
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
        var teacher = await Db.GetUserAsync(userId);
        var expandByDefault = teacher?.Settings.WordsExpandedByDefault ?? false;
        var levelLabel = state.PoolLevel is not null ? $" *[{state.PoolLevel}]*" : string.Empty;
        var header = $"🎯 Preview — {words.Count} words{levelLabel} for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}*:";
        var entries = words.Select(WordFormatter.ToDisplayEntry).ToList();
        var expanded = InitialExpandedSet(entries.Count, expandByDefault);
        var body = BuildExpandableBody(entries, expanded, expandByDefault);

        await Bot.SendMessage(
            chatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "gexp_", Keyboards.PoolPreviewButtons().InlineKeyboard.Select(r => r.ToArray()).ToArray()),
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
        await Db.AssignPoolWordsAsync(state.PoolPreview, userId, state.SelectedStudentId.Value, batchId);

        var teacher = await Db.GetUserAsync(userId);
        var student = await Db.GetUserAsync(state.SelectedStudentId.Value);
        var levelLabel = state.PoolLevel is not null ? $" [{state.PoolLevel}]" : string.Empty;

        await Bot.SendMessage(
            chatId,
            state.SelectedStudentId.Value == userId
                ? $"✅ {state.PoolPreview.Count} words added to *your personal vocabulary*!"
                : $"✅ {state.PoolPreview.Count} words assigned to *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}*!",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        if (state.SelectedStudentId.Value != userId)
        try
        {
            var notifyHeader = $"📚 New vocabulary from {WordFormatter.EscapeMarkdown(teacher?.DisplayName ?? "your teacher")}{levelLabel}:";
            await SendExpandableWordNotificationAsync(state.SelectedStudentId.Value, state.SelectedStudentId.Value, state.PoolPreview, notifyHeader, ct);
        }
        catch
        {
        }

        ResetState(userId);
        await SendMenuAsync(chatId, "Teacher", ct);
    }

    private async Task SendGenLevelSelectionAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        MutateState(userId, s =>
        {
            s.State = UserState.None;
            s.GenSelectedLevels = new();
            s.GenLevelMessageId = 0;
            s.GenCount = 0;
            s.GenTopic = null;
            s.GenPreview = new();
        });

        var msg = await Bot.SendMessage(
            chatId,
            "🤖 *Generate by Level*\n\nSelect one or more CEFR levels (tap to toggle), then press Done:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.CefrLevelMultiSelectButtons(new HashSet<string>(), "gen_level_toggle_", "gen_level_done", "gen_cancel"),
            cancellationToken: ct);

        MutateState(userId, s => s.GenLevelMessageId = msg.MessageId);
    }

    private async Task HandleGenLevelToggleAsync(long userId, long chatId, string level, CancellationToken ct)
    {
        var state = GetState(userId);

        // Ignore stale callbacks from old messages
        if (state.GenLevelMessageId == 0) return;

        MutateState(userId, s =>
        {
            if (s.GenSelectedLevels.Contains(level))
                s.GenSelectedLevels.Remove(level);
            else
                s.GenSelectedLevels.Add(level);
        });

        var updated = GetState(userId);
        var selected = new HashSet<string>(updated.GenSelectedLevels);

        try
        {
            await Bot.EditMessageReplyMarkup(
                chatId,
                updated.GenLevelMessageId,
                replyMarkup: Keyboards.CefrLevelMultiSelectButtons(selected, "gen_level_toggle_", "gen_level_done", "gen_cancel"),
                cancellationToken: ct);
        }
        catch { }
    }

    private async Task HandleGenLevelDoneAsync(long userId, long chatId, CancellationToken ct)
    {
        MutateState(userId, s => { s.GenCount = 0; s.GenTopic = null; s.GenPreview = new(); s.GenLevelMessageId = 0; });
        var state = GetState(userId);

        await Bot.SendMessage(
            chatId,
            $"📦 How many *{state.GenLevelDisplay}* words would you like to generate?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.GenCountButtons(),
            cancellationToken: ct);
    }

    private async Task HandleGenCountAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        if (!int.TryParse(data["gen_count_".Length..], out var count))
        {
            return;
        }

        MutateState(userId, state =>
        {
            state.State = UserState.AwaitingGenTopic;
            state.GenCount = count;
            state.GenTopic = null;
            state.GenPreview = new();
        });

        var state = GetState(userId);
        await Bot.SendMessage(
            chatId,
            $"🏷️ Enter a topic for the *{state.GenLevelDisplay}* words _(optional)_.\n\n_e.g. Business English, Travel, Phrasal Verbs_",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.GenTopicPromptButtons(),
            cancellationToken: ct);
    }

    private async Task GenerateAndShowPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null || state.GenCount <= 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var notice = await Bot.SendMessage(chatId, "⏳ Generating…", cancellationToken: ct);
        try
        {
            var existingWords = (await Db.GetAllWordOriginalsAsync(state.SelectedStudentId.Value)).ToList();
            var generated = await GenerateForLevelsAsync(state.GenSelectedLevels, state.GenCount, state.GenTopic, existingWords);

            MutateState(userId, s =>
            {
                s.State = UserState.None;
                s.GenPreview = generated;
            });

            if (generated.Count == 0)
            {
                await Bot.SendMessage(
                    chatId,
                    "⚠️ AI couldn't generate words. Try regenerating or change the settings.",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Regenerate", "gen_retry") },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "gen_start") }
                    }),
                    cancellationToken: ct);
                return;
            }

            await ShowGenPreviewAsync(userId, chatId, ct);
        }
        finally
        {
            try { await Bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct); }
            catch { }
        }
    }

    private async Task HandleGenRemoveAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.GenPreview.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        MutateState(userId, s => s.State = UserState.AwaitingWordRemoval);

        var teacher = await Db.GetUserAsync(userId);
        var expandByDefault = teacher?.Settings.WordsExpandedByDefault ?? false;
        var entries = state.GenPreview.Select(WordFormatter.ToDisplayEntry).ToList();
        var expanded = InitialExpandedSet(entries.Count, expandByDefault);
        var body = BuildExpandableBody(entries, expanded, expandByDefault);

        await Bot.SendMessage(
            chatId,
            $"✂️ *Remove words from preview*\n\n{body}\n\n_Enter the numbers to remove, separated by commas (e.g. `2, 4`):_",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "gexp_", Keyboards.BackButton("gen_retry").InlineKeyboard.Select(r => r.ToArray()).ToArray()),
            cancellationToken: ct);
    }

    public async Task HandleWordRemovalInputAsync(long userId, long chatId, string input, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.GenPreview.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var indices = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n - 1 : -1)
            .Where(i => i >= 0 && i < state.GenPreview.Count)
            .ToHashSet();

        if (indices.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                "⚠️ No valid numbers found. Please send numbers like `1, 3` matching the list above.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        var updated = state.GenPreview.Where((_, i) => !indices.Contains(i)).ToList();
        if (updated.Count == 0)
        {
            await Bot.SendMessage(chatId, "⚠️ That would remove all words. Please keep at least one.", cancellationToken: ct);
            return;
        }

        MutateState(userId, s =>
        {
            s.State = UserState.None;
            s.GenPreview = updated;
        });

        await Bot.SendMessage(
            chatId,
            $"✅ Removed {indices.Count} word(s). Updated preview:",
            cancellationToken: ct);

        await GenerateAndShowExistingPreviewAsync(userId, chatId, ct);
    }

    private async Task GenerateAndShowExistingPreviewAsync(long userId, long chatId, CancellationToken ct)
        => await ShowGenPreviewAsync(userId, chatId, ct);

    private async Task ShowGenPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var student = await Db.GetUserAsync(state.SelectedStudentId!.Value);
        var topicNote = state.GenTopic is not null ? $" · 🏷️ _{WordFormatter.EscapeMarkdown(state.GenTopic)}_" : string.Empty;
        var header = $"🤖 *{state.GenPreview.Count} {state.GenLevelDisplay} words* for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}*{topicNote}:";

        var entries = state.GenPreview.Select(WordFormatter.ToDisplayEntry).ToList();
        var teacher = await Db.GetUserAsync(userId);
        var expandByDefault = teacher?.Settings.WordsExpandedByDefault ?? false;
        var expanded = InitialExpandedSet(entries.Count, expandByDefault);

        MutateState(userId, s =>
        {
            s.ActiveWordLines = entries;
            s.ActiveWordHeader = header;
            s.ActiveWordContext = "gen";
            s.ExpandedWordIndices = expanded;
            s.WordsExpandedByDefault = expandByDefault;
        });

        var body = BuildExpandableBody(entries, expanded, expandByDefault);
        var msg = await Bot.SendMessage(
            chatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "gexp_", Keyboards.GenPreviewActionRows()),
            cancellationToken: ct);

        MutateState(userId, s => s.ActiveWordMessageId = msg.MessageId);
    }

    private async Task ConfirmGenPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null || state.GenPreview.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var batchId = Guid.NewGuid();
        await Db.SaveWordsFromEntriesAsync(
            state.GenPreview,
            userId,
            state.SelectedStudentId.Value,
            state.GenTopic,
            batchId);

        var teacher = await Db.GetUserAsync(userId);
        var student = await Db.GetUserAsync(state.SelectedStudentId.Value);
        await Bot.SendMessage(
            chatId,
            state.SelectedStudentId.Value == userId
                ? $"✅ *{state.GenPreview.Count}* {state.GenLevelDisplay} words added to *your personal vocabulary*!"
                : $"✅ *{state.GenPreview.Count}* {state.GenLevelDisplay} words saved for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? string.Empty)}*!",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        if (state.SelectedStudentId.Value != userId)
        try
        {
            var topicNote = state.GenTopic is not null ? $" · {WordFormatter.EscapeMarkdown(state.GenTopic)}" : string.Empty;
            var notifyHeader = $"📚 New *{state.GenLevelDisplay}* vocabulary from {WordFormatter.EscapeMarkdown(teacher?.DisplayName ?? "your teacher")}{topicNote}:";
            await SendExpandablePendingNotificationAsync(state.SelectedStudentId.Value, state.SelectedStudentId.Value, state.GenPreview, notifyHeader, ct);
        }
        catch
        {
        }

        ResetState(userId);
        await SendMenuAsync(chatId, "Teacher", ct);
    }

    /// Distributes <paramref name="totalCount"/> evenly across <paramref name="levels"/>,
    /// calling the AI once per level and accumulating results while avoiding cross-level duplicates.
    /// When <paramref name="levels"/> is empty, generates from any level in a single call.
    private async Task<List<PendingWordEntry>> GenerateForLevelsAsync(
        List<string> levels, int totalCount, string? topic, List<string> existingWords)
    {
        if (levels.Count <= 1)
        {
            var level = levels.Count == 1 ? levels[0] : "any";
            return await OpenAi.GenerateWordsByLevelAsync(level, totalCount, topic, existingWords);
        }

        var result = new List<PendingWordEntry>();
        var exclusions = existingWords.ToList();
        var baseCount  = totalCount / levels.Count;
        var remainder  = totalCount % levels.Count;

        // CEFR order
        var ordered = new[] { "A1", "A2", "B1", "B2", "C1", "C2" }
            .Where(levels.Contains)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var perLevel = baseCount + (i < remainder ? 1 : 0);
            if (perLevel <= 0) continue;

            var words = await OpenAi.GenerateWordsByLevelAsync(ordered[i], perLevel, topic, exclusions);
            result.AddRange(words);
            exclusions.AddRange(words.Select(w => w.Word));
        }

        return result;
    }

    private async Task HandleWordsSentStudentAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        if (!long.TryParse(data["words_sent_to_".Length..], out var studentId))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }
        var student = await Db.GetUserAsync(studentId);

        // For self, skip the "By teacher / By student" filter — show all words directly
        if (studentId == userId)
        {
            MutateState(userId, state => { state.BrowsingStudentId = studentId; state.BrowsingFilter = "both"; });
            await Bot.SendMessage(chatId, "📂 How would you like to view your words?", replyMarkup: Keyboards.WordModeSelection(), cancellationToken: ct);
            return;
        }

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

        if (mode == "level")
        {
            await Bot.SendMessage(chatId, "🔤 Select a CEFR level to browse:",
                replyMarkup: Keyboards.CefrLevelBrowseButtons(), cancellationToken: ct);
            return;
        }

        var words = await Db.GetWordsForBrowsingAsync(userId, state.BrowsingStudentId.Value, state.BrowsingFilter ?? "both");
        if (words.Count == 0)
        {
            await Bot.SendMessage(chatId, "📭 No words found for the selected filter.", cancellationToken: ct);
            await SendMenuAsync(chatId, "Teacher", ct);
            return;
        }

        if (mode == "topic")
        {
            var topics = words
                .Select(w => w.Topic ?? "")
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (topics.Count == 0)
            {
                await Bot.SendMessage(chatId, "ℹ️ No topics found for these words.", cancellationToken: ct);
                await SendMenuAsync(chatId, "Teacher", ct);
                return;
            }

            MutateState(userId, s =>
            {
                s.BrowsingWords = words;
                s.CachedTopics = topics;
                s.BrowsingMode = "topic_pick";
            });

            await Bot.SendMessage(chatId, "🏷️ Select a topic:", replyMarkup: Keyboards.TopicSelectionButtons(topics), cancellationToken: ct);
            return;
        }

        // "chunks" or "messages"
        state.BrowsingMode = mode;
        state.BrowsingWords = words;
        state.BrowsingOffset = 0;
        state.BrowsingGroupIdx = 0;
        state.BrowsingGroups = mode == "messages" ? GroupByBatch(words) : new List<List<Word>>();
        SetState(userId, state);

        await SendBrowsingPageAsync(userId, chatId, ct);
    }

    private async Task HandleTopicSelectedAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        if (!int.TryParse(data["wtopic_".Length..], out var idx))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var state = GetState(userId);
        if (idx < 0 || idx >= state.CachedTopics.Count || state.BrowsingWords.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var topic = state.CachedTopics[idx];
        var filtered = state.BrowsingWords.Where(w => (w.Topic ?? "") == topic).ToList();

        var label = string.IsNullOrEmpty(topic) ? "(no topic)" : topic;
        MutateState(userId, s =>
        {
            s.BrowsingMode = "chunks";
            s.BrowsingWords = filtered;
            s.BrowsingOffset = 0;
        });

        await SendBrowsingPageAsync(userId, chatId, ct);
    }

    private async Task HandleLevelBrowseSelectedAsync(long userId, long chatId, string level, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.BrowsingStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var words = await Db.GetWordsForBrowsingAsync(userId, state.BrowsingStudentId.Value, state.BrowsingFilter ?? "both");
        var filtered = words.Where(w => w.EnglishLevel == level).ToList();

        if (filtered.Count == 0)
        {
            await Bot.SendMessage(chatId, $"ℹ️ No *{level}* words found.",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.BackButton("back_to_menu"),
                cancellationToken: ct);
            return;
        }

        MutateState(userId, s =>
        {
            s.BrowsingMode = "chunks";
            s.BrowsingWords = filtered;
            s.BrowsingOffset = 0;
        });

        await SendBrowsingPageAsync(userId, chatId, ct);
    }

    private async Task HandleBrowseNextAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.BrowsingMode == "chunks")
            state.BrowsingOffset += ChunkSize;
        else
            state.BrowsingGroupIdx += 1;

        SetState(userId, state);
        await SendBrowsingPageAsync(userId, chatId, ct);
    }

    private async Task HandleBrowsePrevAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.BrowsingMode == "chunks")
            state.BrowsingOffset = Math.Max(0, state.BrowsingOffset - ChunkSize);
        else
            state.BrowsingGroupIdx = Math.Max(0, state.BrowsingGroupIdx - 1);

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
        bool hasPrev;

        if (state.BrowsingMode == "chunks")
        {
            var offset = state.BrowsingOffset;
            page = state.BrowsingWords.Skip(offset).Take(ChunkSize).ToList();
            hasMore = offset + ChunkSize < state.BrowsingWords.Count;
            hasPrev = offset > 0;
            header = $"📦 Words {offset + 1}–{offset + page.Count} of {state.BrowsingWords.Count}";
        }
        else
        {
            // "messages" mode — groups by batch
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
            hasPrev = index > 0;
            header = $"💬 Message {index + 1}/{groups.Count} — {page[0].CreatedAt:dd MMM yyyy HH:mm} ({page.Count} words)";
        }

        var teacher = await Db.GetUserAsync(userId);
        var expandByDefault = teacher?.Settings.WordsExpandedByDefault ?? false;
        var entries = page.Select(WordFormatter.ToDisplayEntry).ToList();
        var expanded = InitialExpandedSet(entries.Count, expandByDefault);
        var body = BuildExpandableBody(entries, expanded, expandByDefault);

        await Bot.SendMessage(
            chatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "gexp_", Keyboards.BrowseNavigation(hasPrev, hasMore).InlineKeyboard.Select(r => r.ToArray()).ToArray()),
            cancellationToken: ct);
    }

    private async Task SendAllWordsAsync(long userId, long chatId, IReadOnlyList<Word> words, CancellationToken ct)
    {
        var teacher = await Db.GetUserAsync(userId);
        var expandByDefault = teacher?.Settings.WordsExpandedByDefault ?? false;

        for (var i = 0; i < words.Count; i += ChunkSize)
        {
            var slice = words.Skip(i).Take(ChunkSize).ToList();
            var header = $"📋 Words {i + 1}–{i + slice.Count} of {words.Count}";
            var entries = slice.Select(WordFormatter.ToDisplayEntry).ToList();
            var expanded = InitialExpandedSet(entries.Count, expandByDefault);
            var body = BuildExpandableBody(entries, expanded, expandByDefault);

            await Bot.SendMessage(
                chatId,
                $"{header}\n\n{body}",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "gexp_"),
                cancellationToken: ct);
        }
    }

    private async Task HandleRemoveStudentAsync(long chatId, string data, CancellationToken ct)
    {
        if (!long.TryParse(data["remove_student_".Length..], out var studentId))
        {
            await SendMenuAsync(chatId, "Teacher", ct);
            return;
        }
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
        if (!long.TryParse(data["confirm_remove_".Length..], out var studentId))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }
        var student = await Db.GetUserAsync(studentId);

        await Db.UnlinkTeacherStudentAsync(userId, studentId);
        await Bot.SendMessage(
            chatId,
            $"✅ *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}* has been removed from your student list.",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await SendMenuAsync(chatId, "Teacher", ct);
    }

    private static List<List<Word>> GroupByBatch(List<Word> words) =>
        words
            .GroupBy(word => word.BatchId ?? Guid.Empty)
            .OrderBy(group => group.First().CreatedAt)
            .Select(group => group.ToList())
            .ToList();

    private async Task ShowSearchStudentSelectionAsync(long teacherId, long chatId, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);
        if (students.Count == 0)
        {
            await Bot.SendMessage(chatId, "You have no students yet. Use *Add Student* first.", parseMode: ParseMode.Markdown, cancellationToken: ct);
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
        if (!long.TryParse(data["search_for_".Length..], out var studentId))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }
        var student = await Db.GetUserAsync(studentId);

        SetState(userId, new ConversationState
        {
            State = UserState.AwaitingSearchQuery,
            SelectedStudentId = studentId
        });

        await Bot.SendMessage(
            chatId,
            $"🔍 Searching vocabulary for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}*\n\nType the word or phrase to search:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.BackButton("back_to_menu"),
            cancellationToken: ct);
    }

    // ── Delete Words from Student Vocabulary ───────────────────────────────

    private async Task ShowDeleteWordsStudentSelectionAsync(long teacherId, long chatId, CancellationToken ct)
    {
        var students = await Db.GetStudentsForTeacherAsync(teacherId);
        if (students.Count == 0)
        {
            await Bot.SendMessage(chatId, "You have no students yet. Use *Add Student* first.", parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        await Bot.SendMessage(
            chatId,
            "🗂 *Delete Words* — choose a student:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.StudentList(students, "delwords_student_"),
            cancellationToken: ct);
    }

    private async Task HandleDeleteWordsStudentSelectedAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        if (!long.TryParse(data["delwords_student_".Length..], out var studentId))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }
        var student = await Db.GetUserAsync(studentId);

        MutateState(userId, s => s.DeleteStudentId = studentId);

        await Bot.SendMessage(
            chatId,
            $"🗂 Delete words for *{WordFormatter.EscapeMarkdown(student?.DisplayName ?? studentId.ToString())}*\n\nHow do you want to find words to delete?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.DeleteWordsMode(),
            cancellationToken: ct);
    }

    private async Task HandleDeleteByLevelPreviewAsync(long userId, long chatId, string level, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.DeleteStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var words = await Db.GetWordsByLevelAsync(state.DeleteStudentId.Value, level, top: 1000);
        var studentName = (await Db.GetUserAsync(state.DeleteStudentId.Value))?.DisplayName ?? state.DeleteStudentId.ToString()!;

        if (words.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                $"ℹ️ No *{level}* words found for *{WordFormatter.EscapeMarkdown(studentName)}*.",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.BackButton("menu_delete_words"),
                cancellationToken: ct);
            return;
        }

        MutateState(userId, s => { s.DeleteWords = words; s.DeleteLevel = level; s.DeleteMode = null; });

        var sb = new StringBuilder();
        sb.AppendLine($"📋 *{level}* words for *{WordFormatter.EscapeMarkdown(studentName)}* — {words.Count} total:\n");
        for (var i = 0; i < words.Count; i++)
            sb.AppendLine($"{i + 1}\\. {WordFormatter.EscapeMarkdown(words[i].OriginalWord)} — {WordFormatter.EscapeMarkdown(words[i].Translation)}");

        // Telegram message limit is 4096 chars; split if needed
        var text = sb.ToString();
        if (text.Length > 3900)
        {
            // Send word list as plain text first, then action prompt
            var listSb = new StringBuilder();
            listSb.AppendLine($"📋 *{level}* words for *{WordFormatter.EscapeMarkdown(studentName)}* — {words.Count} total:\n");
            for (var i = 0; i < words.Count; i++)
                listSb.AppendLine($"{i + 1}. {words[i].OriginalWord} — {words[i].Translation}");

            foreach (var chunk in ChunkText(listSb.ToString(), 3900))
                await Bot.SendMessage(chatId, chunk, parseMode: ParseMode.Markdown, cancellationToken: ct);

            await Bot.SendMessage(chatId, "What do you want to do?",
                replyMarkup: Keyboards.DeleteWordsActionButtons(words.Count), cancellationToken: ct);
        }
        else
        {
            await Bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.DeleteWordsActionButtons(words.Count), cancellationToken: ct);
        }
    }

    private async Task HandleDeleteConfirmAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.DeleteStudentId is null || state.DeleteWords.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var studentName = (await Db.GetUserAsync(state.DeleteStudentId.Value))?.DisplayName ?? state.DeleteStudentId.ToString()!;
        var ids = state.DeleteWords.Select(w => w.Id);
        await Db.DeleteWordsByIdsAsync(ids, state.DeleteStudentId.Value);
        var count = state.DeleteWords.Count;
        var level = state.DeleteLevel ?? "";
        ResetState(userId);

        await Bot.SendMessage(
            chatId,
            $"✅ Deleted *{count}* {(string.IsNullOrEmpty(level) ? "" : $"*{level}* ")}word(s) from *{WordFormatter.EscapeMarkdown(studentName)}*'s vocabulary.",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await SendMenuAsync(chatId, "Teacher", ct);
    }

    public async Task HandleWordDeleteInputAsync(long userId, long chatId, string input, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.DeleteStudentId is null || state.DeleteWords.Count == 0)
        {
            ResetState(userId);
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var indices = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : -1)
            .Where(n => n >= 1 && n <= state.DeleteWords.Count)
            .Select(n => n - 1)
            .Distinct()
            .ToHashSet();

        if (indices.Count == 0)
        {
            await Bot.SendMessage(chatId, "⚠️ No valid numbers found. Please try again:", replyMarkup: Keyboards.BackButton("back_to_menu"), cancellationToken: ct);
            return;
        }

        List<Word> toDelete;
        if (state.DeleteMode == "keep")
        {
            // Delete everything NOT in the keep set
            toDelete = state.DeleteWords.Where((_, i) => !indices.Contains(i)).ToList();
        }
        else
        {
            // Delete selected words
            toDelete = state.DeleteWords.Where((_, i) => indices.Contains(i)).ToList();
        }

        if (toDelete.Count == 0)
        {
            await Bot.SendMessage(chatId, "ℹ️ Nothing to delete based on your selection.", replyMarkup: Keyboards.BackButton("back_to_menu"), cancellationToken: ct);
            return;
        }

        var studentName = (await Db.GetUserAsync(state.DeleteStudentId.Value))?.DisplayName ?? state.DeleteStudentId.ToString()!;
        await Db.DeleteWordsByIdsAsync(toDelete.Select(w => w.Id), state.DeleteStudentId.Value);
        var level = state.DeleteLevel ?? "";
        ResetState(userId);

        await Bot.SendMessage(
            chatId,
            $"✅ Deleted *{toDelete.Count}* {(string.IsNullOrEmpty(level) ? "" : $"*{level}* ")}word(s) from *{WordFormatter.EscapeMarkdown(studentName)}*'s vocabulary.",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await SendMenuAsync(chatId, "Teacher", ct);
    }

    public async Task HandleWordDeleteSearchInputAsync(long userId, long chatId, string query, CancellationToken ct)
    {
        // Search-based deletion removed; redirect to menu
        ResetState(userId);
        await GoMenuAsync(userId, chatId, ct);
    }

    private static IEnumerable<string> ChunkText(string text, int maxLen)
    {
        for (var i = 0; i < text.Length; i += maxLen)
            yield return text.Substring(i, Math.Min(maxLen, text.Length - i));
    }
}
