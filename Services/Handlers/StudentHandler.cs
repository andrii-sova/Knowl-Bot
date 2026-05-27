using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using VocabifyBot.Interfaces;
using VocabifyBot.Models;
using VocabifyBot.UI;

namespace VocabifyBot.Services.Handlers;

public sealed class StudentHandler(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states, IOpenAiService openAi)
    : HandlerBase(bot, db, states)
{
    public async Task HandleCallbackAsync(string data, long userId, long chatId, CancellationToken ct)
    {
        switch (data)
        {
            case "menu_add_words":
                await Bot.SendMessage(
                    chatId,
                    "📝 How would you like to add words?",
                    replyMarkup: Keyboards.StudentAddWordsChoice(),
                    cancellationToken: ct);
                break;
            case "stype_words":
                SetState(userId, new ConversationState { State = UserState.AwaitingPersonalWords });
                await Bot.SendMessage(
                    chatId,
                    "📝 Send the words or phrases you want to save (one per line):",
                    replyMarkup: Keyboards.BackButton("back_to_menu"),
                    cancellationToken: ct);
                break;
            case "sgen_start":
                await StartStudentGenAsync(userId, chatId, ct);
                break;
            case "sgen_topic_skip":
                MutateState(userId, s => { s.State = UserState.None; s.GenTopic = null; });
                await GenerateStudentPreviewAsync(userId, chatId, ct);
                break;
            case "sgen_retry":
                await GenerateStudentPreviewAsync(userId, chatId, ct);
                break;
            case "sgen_confirm":
                await ConfirmStudentGenAsync(userId, chatId, ct);
                break;
            case "sgen_cancel":
                MutateState(userId, s => { s.GenLevel = null; s.GenCount = 0; s.GenTopic = null; s.GenPreview = new(); s.State = UserState.None; });
                await GoMenuAsync(userId, chatId, ct);
                break;
            case "sgen_remove":
                await HandleStudentGenRemoveAsync(userId, chatId, ct);
                break;
            case "menu_my_words":
                await ShowMyWordsAsync(userId, chatId, ct);
                break;
            case "menu_search":
                SetState(userId, new ConversationState { State = UserState.AwaitingSearchQuery });
                await Bot.SendMessage(
                    chatId,
                    "🔍 Type the word or phrase to search in your vocabulary:",
                    replyMarkup: Keyboards.BackButton("back_to_menu"),
                    cancellationToken: ct);
                break;
            case "vocab_all":
                await SendWordListAsync(userId, chatId, await Db.GetWordsForStudentAsync(userId), "📚 Your vocabulary:", ct);
                break;
            case "vocab_level":
                await Bot.SendMessage(chatId, "🔤 Select a CEFR level:", replyMarkup: Keyboards.VocabLevelButtons(), cancellationToken: ct);
                break;
            default:
                if (data.StartsWith("vocab_t_"))
                {
                    await ShowWordsByTopicAsync(userId, chatId, data, ct);
                }
                else if (data.StartsWith("vocab_lvl_"))
                {
                    await ShowWordsByLevelAsync(userId, chatId, data["vocab_lvl_".Length..], ct);
                }
                else if (data == "vocab_page_next")
                {
                    await HandleVocabPageAsync(userId, chatId, +1, ct);
                }
                else if (data == "vocab_page_prev")
                {
                    await HandleVocabPageAsync(userId, chatId, -1, ct);
                }
                else if (data.StartsWith("sgen_level_"))
                {
                    await HandleStudentGenLevelAsync(userId, chatId, data["sgen_level_".Length..], ct);
                }
                else if (data.StartsWith("sgen_count_"))
                {
                    await HandleStudentGenCountAsync(userId, chatId, data["sgen_count_".Length..], ct);
                }
                break;
        }
    }

    public async Task HandleSearchQueryAsync(long userId, long chatId, string query, CancellationToken ct)
    {
        var results = await Db.SearchWordsAsync(userId, query);
        ResetState(userId);

        if (results.Count == 0)
        {
            await Bot.SendMessage(
                chatId,
                $"🔍 No results found for *{WordFormatter.EscapeMarkdown(query)}*.",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.SearchResultNavigation(),
                cancellationToken: ct);
            return;
        }

        var body = string.Join("\n\n", results.Select(WordFormatter.FormatWordLine));
        await Bot.SendMessage(
            chatId,
            $"🔍 *{results.Count}* result(s) for *{WordFormatter.EscapeMarkdown(query)}*:\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.SearchResultNavigation(),
            cancellationToken: ct);
    }

    private async Task ShowMyWordsAsync(long userId, long chatId, CancellationToken ct)
    {
        var topics = await Db.GetTopicsForStudentAsync(userId);
        if (topics.Count == 0)
        {
            var words = await Db.GetWordsForStudentAsync(userId);
            if (words.Count == 0)
            {
                await Bot.SendMessage(chatId, "Your vocabulary is empty. 📭", cancellationToken: ct);
                return;
            }

            await Bot.SendMessage(
                chatId,
                "📚 Browse your vocabulary:",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🔤 By Level", "vocab_level") },
                    new[] { InlineKeyboardButton.WithCallbackData("📋 All Words", "vocab_all") }
                }),
                cancellationToken: ct);
            return;
        }

        MutateState(userId, state => state.CachedTopics = topics);
        await Bot.SendMessage(chatId, "📚 Browse vocabulary by topic:", replyMarkup: Keyboards.VocabTopicButtons(topics), cancellationToken: ct);
    }

    private async Task ShowWordsByLevelAsync(long userId, long chatId, string level, CancellationToken ct)
    {
        var words = await Db.GetWordsByLevelAsync(userId, level);
        await SendWordListAsync(userId, chatId, words, $"🔤 *{WordFormatter.EscapeMarkdown(level)}* words:", ct);
    }

    private async Task ShowWordsByTopicAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var state = GetState(userId);
        if (!int.TryParse(data["vocab_t_".Length..], out var index) || index < 0 || index >= state.CachedTopics.Count)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var topic = state.CachedTopics[index];
        var words = await Db.GetWordsByTopicAsync(userId, topic);
        await SendWordListAsync(userId, chatId, words, $"📚 {WordFormatter.EscapeMarkdown(topic)}:", ct);
    }

    private async Task SendWordListAsync(long userId, long chatId, IReadOnlyList<Word> words, string header, CancellationToken ct)
    {
        if (words.Count == 0)
        {
            await Bot.SendMessage(chatId, "📭 No words found.", cancellationToken: ct);
            return;
        }

        MutateState(userId, state =>
        {
            state.VocabWords = words.ToList();
            state.VocabPage = 0;
            state.VocabHeader = header;
        });

        await SendVocabPageAsync(userId, chatId, ct);
    }

    private async Task SendVocabPageAsync(long userId, long chatId, CancellationToken ct)
    {
        const int pageSize = 15;
        var state = GetState(userId);
        var words = state.VocabWords;
        var page = state.VocabPage;
        var totalPages = (int)Math.Ceiling(words.Count / (double)pageSize);

        var slice = words.Skip(page * pageSize).Take(pageSize).ToList();
        var body = string.Join("\n\n", slice.Select(WordFormatter.FormatWordLine));
        var pageInfo = totalPages > 1
            ? $"\n\n_Page {page + 1}/{totalPages} · {words.Count} words_"
            : $"\n\n_{words.Count} word{(words.Count == 1 ? "" : "s")}_";

        await Bot.SendMessage(
            chatId,
            $"{state.VocabHeader}\n\n{body}{pageInfo}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.VocabPageNavigation(page, totalPages),
            cancellationToken: ct);
    }

    private async Task HandleVocabPageAsync(long userId, long chatId, int delta, CancellationToken ct)
    {
        const int pageSize = 15;
        var state = GetState(userId);
        if (state.VocabWords.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var totalPages = (int)Math.Ceiling(state.VocabWords.Count / (double)pageSize);
        MutateState(userId, s => s.VocabPage = Math.Clamp(state.VocabPage + delta, 0, totalPages - 1));
        await SendVocabPageAsync(userId, chatId, ct);
    }

    // ── Student Generate by Level ────────────────────────────────────────────

    private async Task StartStudentGenAsync(long userId, long chatId, CancellationToken ct)
    {
        MutateState(userId, s => { s.State = UserState.None; s.GenLevel = null; s.GenCount = 0; s.GenTopic = null; s.GenPreview = new(); });
        await Bot.SendMessage(
            chatId,
            "🤖 *Generate by Level*\n\nSelect the CEFR level:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.CefrLevelButtons("sgen_level_"),
            cancellationToken: ct);
    }

    private async Task HandleStudentGenLevelAsync(long userId, long chatId, string level, CancellationToken ct)
    {
        MutateState(userId, s => { s.GenLevel = level == "any" ? null : level; s.GenCount = 0; s.GenTopic = null; s.GenPreview = new(); });
        await Bot.SendMessage(
            chatId,
            $"📦 How many *{level}* words would you like to generate?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.SGenCountButtons(),
            cancellationToken: ct);
    }

    private async Task HandleStudentGenCountAsync(long userId, long chatId, string raw, CancellationToken ct)
    {
        if (!int.TryParse(raw, out var count)) return;
        MutateState(userId, s => { s.State = UserState.AwaitingStudentGenTopic; s.GenCount = count; s.GenTopic = null; s.GenPreview = new(); });
        var level = GetState(userId).GenLevel ?? "any";
        await Bot.SendMessage(
            chatId,
            $"🏷️ Enter a topic for the *{level}* words _(optional)_.\n\n_e.g. Business English, Travel, Phrasal Verbs_",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.SGenTopicPromptButtons(),
            cancellationToken: ct);
    }

    public async Task HandleStudentGenTopicInputAsync(long userId, long chatId, string topic, CancellationToken ct)
    {
        MutateState(userId, s => { s.State = UserState.None; s.GenTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim(); });
        await GenerateStudentPreviewAsync(userId, chatId, ct);
    }

    private async Task GenerateStudentPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (string.IsNullOrWhiteSpace(state.GenLevel) || state.GenCount <= 0)
        {
            await StartStudentGenAsync(userId, chatId, ct);
            return;
        }

        var notice = await Bot.SendMessage(chatId, "⏳ Generating…", cancellationToken: ct);
        try
        {
            var existingWords = await Db.GetAllWordOriginalsAsync(userId);
            var generated = await openAi.GenerateWordsByLevelAsync(state.GenLevel, state.GenCount, state.GenTopic, existingWords);
            var preview = WordFormatter.BuildPendingWordsFromGeneration(generated);

            MutateState(userId, s => { s.State = UserState.None; s.GenPreview = preview; });

            if (preview.Count == 0)
            {
                await Bot.SendMessage(
                    chatId,
                    "⚠️ Couldn't generate words. Try regenerating or change the settings.",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Regenerate", "sgen_retry") },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "sgen_start") }
                    }),
                    cancellationToken: ct);
                return;
            }

            await ShowStudentGenPreviewAsync(userId, chatId, ct);
        }
        finally
        {
            try { await Bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct); } catch { }
        }
    }

    private async Task ShowStudentGenPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var topicNote = state.GenTopic is not null ? $" · 🏷️ _{WordFormatter.EscapeMarkdown(state.GenTopic)}_" : string.Empty;
        var header = $"🤖 *{state.GenPreview.Count} {state.GenLevel} words*{topicNote}:";
        var body = string.Join("\n\n", state.GenPreview.Select(w =>
        {
            var tag = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : string.Empty;
            return $"{tag}{WordFormatter.EscapeMarkdown(w.Translation)}";
        }));

        await Bot.SendMessage(
            chatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.SGenPreviewButtons(),
            cancellationToken: ct);
    }

    private async Task ConfirmStudentGenAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.GenPreview.Count == 0 || string.IsNullOrWhiteSpace(state.GenLevel))
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var batchId = Guid.NewGuid();
        var words = state.GenPreview.Select(w => new Word
        {
            OriginalWord = w.Original,
            Translation = w.Translation,
            EnglishLevel = w.EnglishLevel ?? state.GenLevel,
            Topic = state.GenTopic,
            BatchId = batchId,
            AddedByUserId = userId,
            ForStudentId = userId
        }).ToList();

        await Db.SaveWordsAsync(words);

        MutateState(userId, s => { s.GenLevel = null; s.GenCount = 0; s.GenTopic = null; s.GenPreview = new(); });

        await Bot.SendMessage(
            chatId,
            $"✅ *{words.Count}* words added to your vocabulary!",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await GoMenuAsync(userId, chatId, ct);
    }

    private async Task HandleStudentGenRemoveAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.GenPreview.Count == 0) { await GoMenuAsync(userId, chatId, ct); return; }

        MutateState(userId, s => s.State = UserState.AwaitingWordRemoval);

        var numbered = string.Join("\n", state.GenPreview.Select((w, i) =>
        {
            var tag = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : string.Empty;
            return $"{i + 1}. {tag}{WordFormatter.EscapeMarkdown(w.Translation)}";
        }));

        await Bot.SendMessage(
            chatId,
            $"✂️ *Remove words from preview*\n\n{numbered}\n\n_Enter the numbers to remove, e.g. `2, 4`:_",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.BackButton("sgen_retry"),
            cancellationToken: ct);
    }

    public async Task HandleStudentWordRemovalInputAsync(long userId, long chatId, string input, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.GenPreview.Count == 0) { await GoMenuAsync(userId, chatId, ct); return; }

        var indices = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n - 1 : -1)
            .Where(i => i >= 0 && i < state.GenPreview.Count)
            .ToHashSet();

        if (indices.Count == 0)
        {
            await Bot.SendMessage(chatId, "⚠️ No valid numbers found. Please send numbers like `1, 3`.", parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        var updated = state.GenPreview.Where((_, i) => !indices.Contains(i)).ToList();
        if (updated.Count == 0)
        {
            await Bot.SendMessage(chatId, "⚠️ That would remove all words. Please keep at least one.", cancellationToken: ct);
            return;
        }

        MutateState(userId, s => { s.State = UserState.None; s.GenPreview = updated; });
        await Bot.SendMessage(chatId, $"✅ Removed {indices.Count} word(s). Updated preview:", cancellationToken: ct);
        await ShowStudentGenPreviewAsync(userId, chatId, ct);
    }
}
