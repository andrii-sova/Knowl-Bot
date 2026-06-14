using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Enums;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.UI;

namespace KnowlBot.Services.Handlers;

public abstract class HandlerBase(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states)
{
    protected ITelegramBotClient Bot { get; } = bot;
    protected IDatabaseService Db { get; } = db;
    protected ConversationStateManager States { get; } = states;

    protected ConversationState GetState(long userId) => States.Get(userId);

    protected void SetState(long userId, ConversationState state) => States.Set(userId, state);

    protected void ResetState(long userId) => States.Reset(userId);

    protected void MutateState(long userId, Action<ConversationState> mutate) => States.Mutate(userId, mutate);

    protected async Task SendMenuAsync(long chatId, long userId, string role, CancellationToken ct)
    {
        if (role == "Student")
        {
            var student = await Db.GetUserAsync(userId);
            if (student is not null && !student.IsActivated)
            {
                await Bot.SendMessage(
                    chatId,
                    "🔒 You haven't been added to a teacher's group yet.\n\nPlease ask your teacher to add you by your Telegram username.",
                    cancellationToken: ct);
                return;
            }
        }

        await Bot.SendMessage(
            chatId,
            role == "Teacher" ? "👨‍🏫 Teacher Menu" : "📚 Student Menu",
            replyMarkup: GetMenuMarkup(role),
            cancellationToken: ct);
    }

    protected Task SendMenuAsync(long chatId, string role, CancellationToken ct) =>
        Bot.SendMessage(
            chatId,
            role == "Teacher" ? "👨‍🏫 Teacher Menu" : "📚 Student Menu",
            replyMarkup: GetMenuMarkup(role),
            cancellationToken: ct);

    protected async Task GoMenuAsync(long userId, long chatId, CancellationToken ct)
    {
        var user = await Db.GetUserAsync(userId);
        await SendMenuAsync(chatId, userId, user?.Role ?? "Student", ct);
    }

    /// <summary>
    /// Sends a list of formatted word lines in chunks of <paramref name="chunkSize"/>.
    /// The optional <paramref name="header"/> is prepended to the first message only.
    /// The optional <paramref name="finalMarkup"/> is attached to the last message only.
    /// </summary>
    protected async Task SendWordListAsync(
        long chatId,
        IReadOnlyList<string> lines,
        CancellationToken ct,
        string? header = null,
        Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? finalMarkup = null,
        int chunkSize = 10)
    {
        var chunks = lines.Chunk(chunkSize).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            var isFirst = i == 0;
            var isLast  = i == chunks.Count - 1;
            var body    = string.Join("\n\n", chunks[i]);
            var text    = isFirst && header is not null ? $"{header}\n\n{body}" : body;
            await Bot.SendMessage(
                chatId,
                text,
                replyMarkup: isLast ? finalMarkup : null,
                cancellationToken: ct);
        }
    }

    private static InlineKeyboardMarkup GetMenuMarkup(string role) =>
        role == "Teacher" ? Keyboards.TeacherMenu() : Keyboards.StudentMenu();

    protected static string BuildExpandableBody(IReadOnlyList<WordDisplayEntry> lines, IReadOnlySet<int> expanded, bool expandedByDefault = false) =>
        string.Join("\n\n", lines.Select((e, i) =>
        {
            var isExpanded = expanded.Contains(i);
            // Only show icon when state differs from default
            var icon = (isExpanded, expandedByDefault) switch
            {
                (true,  false) => "✅ ",  // expanded, but default is collapsed → highlight
                (false, true)  => "⬜ ",  // collapsed, but default is expanded → highlight
                _              => ""      // matches default → no icon
            };
            return $"{i + 1}. {icon}{(isExpanded ? e.FullLine : e.CompactLine)}";
        }));

    protected static HashSet<int> InitialExpandedSet(int count, bool expandByDefault) =>
        expandByDefault ? new HashSet<int>(Enumerable.Range(0, count)) : new HashSet<int>();

    protected async Task SendExpandableWordNotificationAsync(
        long recipientUserId,
        long recipientChatId,
        IReadOnlyList<Word> words,
        string header,
        CancellationToken ct)
    {
        var recipient = await Db.GetUserAsync(recipientUserId);
        var expandByDefault = recipient?.Settings.WordsExpandedByDefault ?? false;
        var entries = words.Select(WordFormatter.ToDisplayEntry).ToList();
        var expanded = InitialExpandedSet(entries.Count, expandByDefault);
        var body = BuildExpandableBody(entries, expanded, expandByDefault);

        await Bot.SendMessage(
            recipientChatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(entries.Count, expanded, "wexp_"),
            cancellationToken: ct);
    }

    protected async Task SendExpandablePendingNotificationAsync(
        long recipientUserId,
        long recipientChatId,
        IReadOnlyList<PendingWordEntry> entries,
        string header,
        CancellationToken ct)
    {
        var recipient = await Db.GetUserAsync(recipientUserId);
        var expandByDefault = recipient?.Settings.WordsExpandedByDefault ?? false;
        var displayEntries = entries.Select(WordFormatter.ToDisplayEntry).ToList();
        var expanded = InitialExpandedSet(displayEntries.Count, expandByDefault);
        var body = BuildExpandableBody(displayEntries, expanded, expandByDefault);

        await Bot.SendMessage(
            recipientChatId,
            $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.ExpandableWordKeyboard(displayEntries.Count, expanded, "wexp_"),
            cancellationToken: ct);
    }
}
