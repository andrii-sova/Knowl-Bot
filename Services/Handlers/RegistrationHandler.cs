using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramUser = Telegram.Bot.Types.User;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.UI;

namespace KnowlBot.Services.Handlers;

public sealed class RegistrationHandler(
    ITelegramBotClient bot,
    IDatabaseService db,
    ConversationStateManager states,
    IReadOnlySet<string> allowedTeachers)
    : HandlerBase(bot, db, states)
{
    public Task ShowRoleSelectionAsync(long chatId, CancellationToken ct) =>
        Bot.SendMessage(
            chatId,
            "👋 Welcome to *EnglishBot*!\n\nAre you a teacher or a student?",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.RoleSelection(),
            cancellationToken: ct);

    public Task ShowMainMenuAsync(long chatId, long userId, string role, CancellationToken ct) =>
        SendMenuAsync(chatId, userId, role, ct);

    public Task ShowMainMenuAsync(long chatId, string role, CancellationToken ct) =>
        SendMenuAsync(chatId, role, ct);

    public Task GoToMenuAsync(long userId, long chatId, CancellationToken ct) => GoMenuAsync(userId, chatId, ct);

    public async Task HandleCallbackAsync(string data, TelegramUser from, long chatId, CancellationToken ct)
    {
        switch (data)
        {
            case "role_teacher":
                if (!IsTeacherAllowed(from.Username))
                {
                    await Bot.SendMessage(
                        chatId,
                        "🚫 You are not authorised to register as a teacher.\n\nPlease continue as a student.",
                        replyMarkup: Keyboards.RoleSelection(),
                        cancellationToken: ct);
                    return;
                }
                await RegisterUserAsync(from.Id, from, "Teacher", chatId, ct);
                break;
            case "role_student":
                await RegisterStudentAsync(from.Id, from, chatId, ct);
                break;
            case "menu_settings":
            {
                var user = await Db.GetUserAsync(from.Id);
                await Bot.SendMessage(
                    chatId,
                    "⚙️ *Account Settings*",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: Keyboards.AccountSettingsMenu(user?.Settings.WordsExpandedByDefault ?? false),
                    cancellationToken: ct);
                break;
            }
            case "settings_toggle_expand":
            {
                var user = await Db.GetUserAsync(from.Id);
                var newValue = !(user?.Settings.WordsExpandedByDefault ?? false);
                await Db.UpdateWordsExpandedDefaultAsync(from.Id, newValue);
                await Bot.SendMessage(
                    chatId,
                    "⚙️ *Account Settings*",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: Keyboards.AccountSettingsMenu(newValue),
                    cancellationToken: ct);
                break;
            }
            case "settings_set_name":
            case "menu_set_name":
                await HandleSetNameCallbackAsync(from.Id, chatId, ct);
                break;
            case "back_to_menu":
                ResetState(from.Id);
                await GoMenuAsync(from.Id, chatId, ct);
                break;
        }
    }

    public async Task HandleDisplayNameInputAsync(long userId, long chatId, string name, CancellationToken ct)
    {
        if (name.Length > 60)
        {
            await Bot.SendMessage(chatId, "❌ Max 60 characters. Please try again:", cancellationToken: ct);
            return;
        }

        await Db.UpdateDisplayNameAsync(userId, name);
        ResetState(userId);
        var user = await Db.GetUserAsync(userId);
        await Bot.SendMessage(
            chatId,
            $"✅ Display name set to *{WordFormatter.EscapeMarkdown(name)}*!",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.AccountSettingsMenu(user?.Settings.WordsExpandedByDefault ?? false),
            cancellationToken: ct);
    }

    public async Task HandleStudentUsernameInputAsync(long teacherId, long chatId, string input, CancellationToken ct)
    {
        var username = input.TrimStart('@').Trim();
        var student = await Db.GetUserByUsernameAsync(username);

        ResetState(teacherId);

        if (student is not null && student.Role == "Student")
        {
            await Db.LinkTeacherStudentAsync(teacherId, student.TelegramId);
            await Bot.SendMessage(
                chatId,
                $"✅ *{WordFormatter.EscapeMarkdown(student.DisplayName)}* added to your list!",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        else if (student is not null && student.Role == "Teacher")
        {
            await Bot.SendMessage(
                chatId,
                $"❌ @{WordFormatter.EscapeMarkdown(username)} is registered as a teacher, not a student.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        else
        {
            await Db.AddPendingInvitationAsync(teacherId, username);
            await Bot.SendMessage(
                chatId,
                $"⏳ @{WordFormatter.EscapeMarkdown(username)} hasn't started the bot yet.\nThey are added as *awaiting activation* and will be linked automatically when they join.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }

        await SendMenuAsync(chatId, "Teacher", ct);
    }

    private async Task RegisterUserAsync(long userId, TelegramUser from, string role, long chatId, CancellationToken ct)
    {
        await Db.UpsertUserAsync(new User
        {
            TelegramId = userId,
            Username = from.Username ?? string.Empty,
            FirstName = from.FirstName,
            Role = role
        });

        ResetState(userId);
        await SendMenuAsync(chatId, userId, role, ct);
    }

    private async Task RegisterStudentAsync(long userId, TelegramUser from, long chatId, CancellationToken ct)
    {
        await Db.UpsertUserAsync(new User
        {
            TelegramId = userId,
            Username = from.Username ?? string.Empty,
            FirstName = from.FirstName,
            Role = "Student"
        });

        if (!string.IsNullOrEmpty(from.Username))
        {
            await Db.ClaimPendingInvitationsAsync(userId, from.Username);
        }

        ResetState(userId);
        await SendMenuAsync(chatId, userId, "Student", ct);
    }

    private async Task HandleSetNameCallbackAsync(long userId, long chatId, CancellationToken ct)
    {
        var user = await Db.GetUserAsync(userId);
        SetState(userId, new ConversationState { State = UserState.AwaitingDisplayName });

        await Bot.SendMessage(
            chatId,
            $"✏️ Enter your display name (current: *{WordFormatter.EscapeMarkdown(user?.DisplayName ?? "-")}*):",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.BackButton("back_to_menu"),
            cancellationToken: ct);
    }

    private bool IsTeacherAllowed(string? username) =>
        !string.IsNullOrEmpty(username) &&
        allowedTeachers.Contains(username.ToLowerInvariant());
}
