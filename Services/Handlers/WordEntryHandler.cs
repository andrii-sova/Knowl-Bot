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

    public Task HandleCallbackAsync(string data, long userId, long chatId, CancellationToken ct)
    {
        if (data.StartsWith("wentry_") && int.TryParse(data["wentry_".Length..], out var idx))
            return HandleWordExpandAsync(userId, chatId, idx, ct);

        return data switch
        {
            "topic_auto" => HandleTopicAutoAsync(userId, chatId, ct),
            "topic_specify" => HandleTopicSpecifyAsync(userId, chatId, ct),
            "topic_skip" => HandleTopicSkipAsync(userId, chatId, ct),
            _ => Task.CompletedTask
        };
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
        var text = $"{state.ActiveWordHeader}\n\n{body}\n\n🏷️ Add a topic to these words?";

        try
        {
            await Bot.EditMessageText(
                chatId,
                state.ActiveWordMessageId,
                text,
                replyMarkup: Keyboards.ExpandableWordKeyboard(state.ActiveWordLines.Count, state.ExpandedWordIndices, "wentry_", Keyboards.TopicChoiceActionRows()),
                cancellationToken: ct);
        }
        catch { }
    }

    public async Task HandleWordsInputAsync(long addedById, long forStudentId, string inputText, long chatId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            await Bot.SendMessage(chatId, "❌ Please send at least one word or phrase.", cancellationToken: ct);
            return;
        }

        var notice = await Bot.SendMessage(chatId, "⏳ Enriching words with AI…", cancellationToken: ct);
        List<PendingWordEntry> entries;
        try
        {
            entries = await OpenAi.EnrichWordsAsync(inputText);
        }
        catch (Exception)
        {
            try { await Bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct); } catch { }
            await Bot.SendMessage(chatId, "❌ AI service error. Please try again or send fewer words at once.", cancellationToken: ct);
            return;
        }

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

        var displayEntries = entries.Select(WordFormatter.ToDisplayEntry).ToList();
        var user = await Db.GetUserAsync(addedById);
        var expandByDefault = user?.Settings.WordsExpandedByDefault ?? false;
        var expanded = InitialExpandedSet(displayEntries.Count, expandByDefault);
        var body = BuildExpandableBody(displayEntries, expanded, expandByDefault);
        var msg = await Bot.SendMessage(
            chatId,
            $"📝 {entries.Count} word(s) enriched:\n\n{body}\n\n🏷️ Add a topic to these words?",
            replyMarkup: Keyboards.ExpandableWordKeyboard(displayEntries.Count, expanded, "wentry_", Keyboards.TopicChoiceActionRows()),
            cancellationToken: ct);

        MutateState(addedById, s =>
        {
            s.ActiveWordLines = displayEntries;
            s.ActiveWordHeader = $"📝 {entries.Count} word(s) enriched:";
            s.ActiveWordContext = "entry";
            s.ExpandedWordIndices = expanded;
            s.WordsExpandedByDefault = expandByDefault;
            s.ActiveWordMessageId = msg.MessageId;
        });
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

            try
            {
                await SendExpandablePendingNotificationAsync(state.PendingForStudentId.Value, state.PendingForStudentId.Value, state.PendingWords, header, ct);
            }
            catch
            {
            }
        }

        ResetState(userId);
        await GoMenuAsync(userId, chatId, ct);
    }
}
