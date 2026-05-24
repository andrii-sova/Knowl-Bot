using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using VocabifyBot.Interfaces;
using VocabifyBot.Models;
using VocabifyBot.UI;

namespace VocabifyBot.Services.Handlers;

public sealed class StudentHandler(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states)
    : HandlerBase(bot, db, states)
{
    public async Task HandleCallbackAsync(string data, long userId, long chatId, CancellationToken ct)
    {
        switch (data)
        {
            case "menu_add_words":
                SetState(userId, new ConversationState { State = UserState.AwaitingPersonalWords });
                await Bot.SendMessage(
                    chatId,
                    "📝 Send the words or phrases you want to save (one per line):",
                    replyMarkup: Keyboards.BackButton("back_to_menu"),
                    cancellationToken: ct);
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
}
