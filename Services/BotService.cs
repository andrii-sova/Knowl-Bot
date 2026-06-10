using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.Services.Handlers;

namespace KnowlBot.Services;

public sealed class BotService(
    ITelegramBotClient bot,
    IDatabaseService db,
    ConversationStateManager states,
    RegistrationHandler registration,
    TeacherHandler teacher,
    StudentHandler student,
    WordEntryHandler wordEntry,
    QuizHandler quiz,
    IReadOnlySet<string> allowedTeachers)
{
    public async Task StartAsync(CancellationToken ct)
    {
        await db.InitializeAsync();
        bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] },
            ct);

        var me = await bot.GetMe(ct);
        Console.WriteLine($"✅ Bot @{me.Username} started. Press Ctrl+C to stop.");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { } message)
            {
                await HandleMessageAsync(message, ct);
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(callbackQuery, ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ {FullMessage(ex)}");
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        if (message.From is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        var userId = message.From.Id;
        var chatId = message.Chat.Id;
        var text = message.Text.Trim();
        var state = states.Get(userId);

        if (text.StartsWith("/start") || text == "/menu")
        {
            states.Reset(userId);
            var existingUser = await db.GetUserAsync(userId);
            if (existingUser is null)
            {
                await registration.ShowRoleSelectionAsync(chatId, ct);
                return;
            }

            if (existingUser.Role == "Student" && !string.IsNullOrWhiteSpace(message.From.Username))
            {
                await db.ClaimPendingInvitationsAsync(userId, message.From.Username);
            }

            await registration.ShowMainMenuAsync(chatId, userId, existingUser.Role, ct);
            return;
        }

        switch (state.State)
        {
            case UserState.AwaitingDisplayName:
                await registration.HandleDisplayNameInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingStudentUsername:
                if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, message.From.Username, ct)) return;
                await registration.HandleStudentUsernameInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingWordsForStudent when state.SelectedStudentId is not null:
                if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, message.From.Username, ct)) return;
                await wordEntry.HandleWordsInputAsync(userId, state.SelectedStudentId.Value, text, chatId, ct);
                break;
            case UserState.AwaitingPersonalWords:
                await wordEntry.HandleWordsInputAsync(userId, userId, text, chatId, ct);
                break;
            case UserState.AwaitingTopicName:
                await wordEntry.HandleTopicNameInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingQuizCustomAmount:
                await quiz.HandleQuizCustomAmountInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingSearchQuery:
                var user = await db.GetUserAsync(userId);
                if (user?.Role == "Teacher")
                {
                    await teacher.HandleSearchQueryAsync(userId, chatId, text, ct);
                }
                else
                {
                    await student.HandleSearchQueryAsync(userId, chatId, text, ct);
                }
                break;
            case UserState.AwaitingGenTopic:
                if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, message.From.Username, ct)) return;
                await teacher.HandleGenTopicInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingStudentGenTopic:
                await student.HandleStudentGenTopicInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingWordDeleteInput:
                if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, message.From.Username, ct)) return;
                await teacher.HandleWordDeleteInputAsync(userId, chatId, text, ct);
                break;
            case UserState.AwaitingWordRemoval:
            {
                var removalUser = await db.GetUserAsync(userId);
                if (removalUser?.Role == "Teacher")
                {
                    if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, message.From.Username, ct)) return;
                    await teacher.HandleWordRemovalInputAsync(userId, chatId, text, ct);
                }
                else
                {
                    await student.HandleStudentWordRemovalInputAsync(userId, chatId, text, ct);
                }
                break;
            }
            default:
                var existingUser = await db.GetUserAsync(userId);
                if (existingUser is null)
                {
                    await registration.ShowRoleSelectionAsync(chatId, ct);
                }
                else
                {
                    await registration.ShowMainMenuAsync(chatId, userId, existingUser.Role, ct);
                }
                break;
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Message is null || callbackQuery.From is null || string.IsNullOrWhiteSpace(callbackQuery.Data))
        {
            return;
        }

        var userId = callbackQuery.From.Id;
        var chatId = callbackQuery.Message.Chat.Id;
        var data = callbackQuery.Data;

        await bot.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);

        if (IsRegistrationCallback(data))
        {
            await registration.HandleCallbackAsync(data, callbackQuery.From, chatId, ct);
            return;
        }

        if (data == "menu_search")
        {
            var user = await db.GetUserAsync(userId);
            if (user?.Role == "Teacher")
            {
                if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, callbackQuery.From.Username, ct)) return;
                await teacher.HandleCallbackAsync(data, userId, chatId, ct);
            }
            else if (user?.Role == "Student")
            {
                await student.HandleCallbackAsync(data, userId, chatId, ct);
            }
            else
            {
                await registration.ShowRoleSelectionAsync(chatId, ct);
            }

            return;
        }

        if (IsTeacherCallback(data))
        {
            if (await DenyIfUnauthorizedTeacherAsync(userId, chatId, callbackQuery.From.Username, ct)) return;
            await teacher.HandleCallbackAsync(data, userId, chatId, ct);
            return;
        }

        if (IsWordEntryCallback(data))
        {
            await wordEntry.HandleCallbackAsync(data, userId, chatId, ct);
            return;
        }

        if (IsQuizCallback(data))
        {
            await quiz.HandleCallbackAsync(data, userId, chatId, ct);
            return;
        }

        if (IsStudentCallback(data))
        {
            await student.HandleCallbackAsync(data, userId, chatId, ct);
        }
    }

    private static bool IsRegistrationCallback(string data) =>
        data is "role_teacher" or "role_student" or "menu_set_name" or "menu_settings"
             or "settings_set_name" or "settings_toggle_expand" or "back_to_menu";

    private static bool IsTeacherCallback(string data) =>
        data is "menu_add_student" or "back_from_add_student" or "menu_send_words" or "menu_my_students" or
            "menu_words_sent" or "menu_remove_student" or "type_words" or "back_from_send_words" ||
        data.StartsWith("send_to_") ||
        data.StartsWith("pool_") ||
        data.StartsWith("gen_") ||
        data.StartsWith("gexp_") ||
        data.StartsWith("browse_") ||
        data.StartsWith("wfilter_") ||
        data.StartsWith("wmode_") ||
        data.StartsWith("wtopic_") ||
        data.StartsWith("wlevel_") ||
        data.StartsWith("words_sent_to_") ||
        data.StartsWith("remove_student_") ||
        data.StartsWith("confirm_remove_") ||
        data.StartsWith("search_for_") ||
        data.StartsWith("delwords_") ||
        data is "menu_delete_words" or "delwords_level" or "delwords_pick_delete" or "delwords_pick_keep";

    private static bool IsWordEntryCallback(string data) => data.StartsWith("topic_") || data.StartsWith("wentry_");

    private static bool IsQuizCallback(string data) =>
        data is "menu_quiz" or "menu_mistakes" || data.StartsWith("quiz_");

    private static bool IsStudentCallback(string data) =>
        data is "menu_add_words" or "menu_my_words" or "stype_words" ||
        data.StartsWith("vocab_") ||
        data.StartsWith("sgen_") ||
        data.StartsWith("sfilter_") ||
        data.StartsWith("wexp_");

    private static string FullMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            parts.Add(current.Message);
        }

        return string.Join(" → ", parts);
    }

    private static Task HandleErrorAsync(ITelegramBotClient _, Exception ex, HandleErrorSource __, CancellationToken ___)
    {
        Console.WriteLine($"⚠️ {(ex is ApiRequestException api ? $"Telegram [{api.ErrorCode}]: {api.Message}" : ex.ToString())}");
        return Task.CompletedTask;
    }

    private bool IsTeacherAuthorized(string? username) =>
        !string.IsNullOrEmpty(username) &&
        allowedTeachers.Contains(username.ToLowerInvariant());

    private async Task<bool> DenyIfUnauthorizedTeacherAsync(long userId, long chatId, string? username, CancellationToken ct)
    {
        if (IsTeacherAuthorized(username))
            return false;

        states.Reset(userId);
        await bot.SendMessage(
            chatId,
            "🚫 You are not authorised to perform teacher actions.",
            cancellationToken: ct);
        return true;
    }
}
