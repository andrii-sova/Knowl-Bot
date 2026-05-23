using VocabifyBot.Interfaces;
using VocabifyBot.Models;
using VocabifyBot.UI;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace VocabifyBot.Services.Handlers;

public sealed class StudentHandler : HandlerBase
{
    public StudentHandler(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states)
        : base(bot, db, states)
    {
    }

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
                await SendWordListAsync(chatId, await Db.GetWordsForStudentAsync(userId), "📚 Your vocabulary:", ct);
                break;
            default:
                if (data.StartsWith("vocab_t_"))
                {
                    await ShowWordsByTopicAsync(userId, chatId, data, ct);
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

            await SendWordListAsync(chatId, words, "📚 Your vocabulary:", ct);
            return;
        }

        MutateState(userId, state => state.CachedTopics = topics);
        await Bot.SendMessage(chatId, "📚 Browse vocabulary by topic:", replyMarkup: Keyboards.VocabTopicButtons(topics), cancellationToken: ct);
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
        await SendWordListAsync(chatId, words, $"📚 {WordFormatter.EscapeMarkdown(topic)}:", ct);
    }

    private async Task SendWordListAsync(long chatId, IReadOnlyList<Word> words, string header, CancellationToken ct)
    {
        if (words.Count == 0)
        {
            await Bot.SendMessage(chatId, "📭 No words found.", cancellationToken: ct);
            return;
        }

        const int pageSize = 15;
        var page = words.Take(pageSize).ToList();
        var body = string.Join("\n\n", page.Select(WordFormatter.FormatWordLine));
        var suffix = words.Count > pageSize ? $"\n\n_(Showing {pageSize} of {words.Count})_" : string.Empty;

        await Bot.SendMessage(
            chatId,
            $"{header}\n\n{body}{suffix}",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }
}
