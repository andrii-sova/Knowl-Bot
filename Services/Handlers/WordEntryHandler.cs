using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.UI;

namespace KnowlBot.Services.Handlers;

public sealed class WordEntryHandler(
    ITelegramBotClient bot,
    IDatabaseService db,
    ConversationStateManager states,
    IAiService openAi)
    : HandlerBase(bot, db, states)
{
    private IAiService OpenAi { get; } = openAi;

    public Task HandleCallbackAsync(string data, long userId, long chatId, CancellationToken ct) => data switch
    {
        "topic_auto" => HandleTopicAutoAsync(userId, chatId, ct),
        "topic_specify" => HandleTopicSpecifyAsync(userId, chatId, ct),
        "topic_skip" => HandleTopicSkipAsync(userId, chatId, ct),
        _ => Task.CompletedTask
    };

    public async Task HandleWordsInputAsync(long addedById, long forStudentId, string inputText, long chatId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            await Bot.SendMessage(chatId, "❌ Please send at least one word or phrase.", cancellationToken: ct);
            return;
        }

        var notice = await Bot.SendMessage(chatId, "⏳ Enriching words with AI…", cancellationToken: ct);
        var entries = await OpenAi.EnrichWordsAsync(inputText);

        try
        {
            await Bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct);
        }
        catch
        {
        }

        if (entries.Count == 0)
        {
            await Bot.SendMessage(chatId, "❌ AI could not process those words. Please try again.", cancellationToken: ct);
            return;
        }

        SetState(addedById, new ConversationState
        {
            State = UserState.AwaitingTopicChoice,
            SelectedStudentId = addedById == forStudentId ? null : forStudentId,
            PendingWords = entries,
            PendingAddedByUserId = addedById,
            PendingForStudentId = forStudentId
        });

        var lines = entries.Select(WordFormatter.FormatPendingLine).ToList();
        await SendWordListAsync(chatId, lines, ct);
        await Bot.SendMessage(chatId, "🏷️ Add a topic to these words?", replyMarkup: Keyboards.TopicChoice(), cancellationToken: ct);
    }

    public async Task HandleTopicAutoAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.PendingWords.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var notice = await Bot.SendMessage(chatId, "🤖 Detecting topic…", cancellationToken: ct);
        var topic = await OpenAi.DetectTopicAsync(string.Join("\n", state.PendingWords.Select(word => word.Word)));

        try
        {
            await Bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct);
        }
        catch
        {
        }

        await FinalizeWordsAsync(userId, chatId, topic, ct);
    }

    public async Task HandleTopicSpecifyAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.PendingWords.Count == 0)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        state.State = UserState.AwaitingTopicName;
        SetState(userId, state);

        await Bot.SendMessage(
            chatId,
            "🏷️ Enter the topic name (e.g. *Phrasal Verbs*, *Business English*, *Adjectives*):",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }

    public Task HandleTopicSkipAsync(long userId, long chatId, CancellationToken ct) => FinalizeWordsAsync(userId, chatId, null, ct);

    public async Task HandleTopicNameInputAsync(long userId, long chatId, string name, CancellationToken ct)
    {
        if (name.Length > 60)
        {
            await Bot.SendMessage(chatId, "❌ Max 60 characters. Please try again:", cancellationToken: ct);
            return;
        }

        await FinalizeWordsAsync(userId, chatId, name, ct);
    }

    public async Task FinalizeWordsAsync(long userId, long chatId, string? topic, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.PendingWords.Count == 0 || state.PendingAddedByUserId is null || state.PendingForStudentId is null)
        {
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        var batchId = Guid.NewGuid();
        await Db.SaveWordsFromEntriesAsync(
            state.PendingWords,
            state.PendingAddedByUserId.Value,
            state.PendingForStudentId.Value,
            topic,
            batchId);

        var savedMessage = topic is not null
            ? $"✅ Saved! Topic: *{WordFormatter.EscapeMarkdown(topic)}*"
            : "✅ Saved without topic.";

        await Bot.SendMessage(chatId, savedMessage, parseMode: ParseMode.Markdown, cancellationToken: ct);

        if (state.PendingAddedByUserId != state.PendingForStudentId)
        {
            var teacher = await Db.GetUserAsync(state.PendingAddedByUserId.Value);
            var topicLine = topic is not null ? $"\n🏷️ Topic: {topic}" : string.Empty;
            var header = $"📚 New vocabulary from {teacher?.DisplayName ?? "your teacher"}:{topicLine}";
            var wordLines = state.PendingWords.Select(WordFormatter.FormatPendingLine).ToList();

            try
            {
                await SendWordListAsync(state.PendingForStudentId.Value, wordLines, ct, header: header);
            }
            catch
            {
            }
        }

        ResetState(userId);
        await GoMenuAsync(userId, chatId, ct);
    }
}
