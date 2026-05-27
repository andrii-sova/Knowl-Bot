using FluentAssertions;
using NSubstitute;
using Telegram.Bot;
using VocabifyBot.Interfaces;
using VocabifyBot.Models;
using VocabifyBot.Services;
using VocabifyBot.Services.Handlers;
using Xunit;
using DbUser = VocabifyBot.Models.User;
using TgUser = Telegram.Bot.Types.User;

namespace VocabifyBot.Tests;

public class RegistrationHandlerTests
{
    private readonly ITelegramBotClient _bot = Substitute.For<ITelegramBotClient>();
    private readonly IDatabaseService _db = Substitute.For<IDatabaseService>();
    private readonly ConversationStateManager _states = new();

    private const long UserId = 1;
    private const long ChatId = 100;

    private RegistrationHandler BuildSut(params string[] allowedTeachers)
    {
        var set = new HashSet<string>(allowedTeachers, StringComparer.OrdinalIgnoreCase);
        _db.GetUserAsync(Arg.Any<long>())
            .Returns(new DbUser { TelegramId = UserId, Role = "Teacher", IsActivated = true });
        return new RegistrationHandler(_bot, _db, _states, set);
    }

    [Fact]
    public async Task HandleCallback_RoleTeacher_AllowedUser_UpsertAsTeacher()
    {
        var sut = BuildSut("testuser");
        var from = new TgUser { Id = UserId, Username = "testuser" };
        _db.UpsertUserAsync(Arg.Any<DbUser>()).Returns(Task.CompletedTask);

        await sut.HandleCallbackAsync("role_teacher", from, ChatId, CancellationToken.None);

        await _db.Received(1).UpsertUserAsync(Arg.Is<DbUser>(u =>
            u.TelegramId == UserId && u.Role == "Teacher"));
    }

    [Fact]
    public async Task HandleCallback_RoleTeacher_NotAllowedUser_DoesNotUpsert()
    {
        var sut = BuildSut("otheruser");
        var from = new TgUser { Id = UserId, Username = "testuser" };

        await sut.HandleCallbackAsync("role_teacher", from, ChatId, CancellationToken.None);

        await _db.DidNotReceive().UpsertUserAsync(Arg.Any<DbUser>());
    }

    [Fact]
    public async Task HandleCallback_RoleStudent_UpsertAsStudent()
    {
        var sut = BuildSut();
        var from = new TgUser { Id = UserId, Username = "studentuser" };
        _db.UpsertUserAsync(Arg.Any<DbUser>()).Returns(Task.CompletedTask);
        _db.ClaimPendingInvitationsAsync(Arg.Any<long>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        _db.GetUserAsync(UserId).Returns(new DbUser { TelegramId = UserId, Role = "Student" });

        await sut.HandleCallbackAsync("role_student", from, ChatId, CancellationToken.None);

        await _db.Received(1).UpsertUserAsync(Arg.Is<DbUser>(u =>
            u.TelegramId == UserId && u.Role == "Student"));
    }

    [Fact]
    public async Task HandleCallback_RoleStudent_WithUsername_ClaimsPendingInvitations()
    {
        var sut = BuildSut();
        var from = new TgUser { Id = UserId, Username = "studentuser" };
        _db.UpsertUserAsync(Arg.Any<DbUser>()).Returns(Task.CompletedTask);
        _db.ClaimPendingInvitationsAsync(Arg.Any<long>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        _db.GetUserAsync(UserId).Returns(new DbUser { TelegramId = UserId, Role = "Student" });

        await sut.HandleCallbackAsync("role_student", from, ChatId, CancellationToken.None);

        await _db.Received(1).ClaimPendingInvitationsAsync(UserId, "studentuser");
    }

    [Fact]
    public async Task HandleDisplayNameInput_ValidName_UpdatesAndResetsState()
    {
        var sut = BuildSut();
        _db.UpdateDisplayNameAsync(Arg.Any<long>(), Arg.Any<string?>()).Returns(Task.CompletedTask);

        await sut.HandleDisplayNameInputAsync(UserId, ChatId, "John Doe", CancellationToken.None);

        await _db.Received(1).UpdateDisplayNameAsync(UserId, "John Doe");
        _states.Get(UserId).State.Should().Be(UserState.None);
    }

    [Fact]
    public async Task HandleDisplayNameInput_TooLong_DoesNotUpdate()
    {
        var sut = BuildSut();
        var longName = new string('A', 61);

        await sut.HandleDisplayNameInputAsync(UserId, ChatId, longName, CancellationToken.None);

        await _db.DidNotReceive().UpdateDisplayNameAsync(Arg.Any<long>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task HandleStudentUsernameInput_ExistingStudent_Links()
    {
        var sut = BuildSut("teacher");
        var student = new DbUser { TelegramId = 99, Role = "Student", FirstName = "Alice", Username = "alice" };
        _db.GetUserByUsernameAsync("alice").Returns(student);
        _db.LinkTeacherStudentAsync(Arg.Any<long>(), Arg.Any<long>()).Returns(Task.CompletedTask);
        _db.GetUserAsync(UserId).Returns(new DbUser { TelegramId = UserId, Role = "Teacher", IsActivated = true });

        await sut.HandleStudentUsernameInputAsync(UserId, ChatId, "@alice", CancellationToken.None);

        await _db.Received(1).LinkTeacherStudentAsync(UserId, 99);
        _states.Get(UserId).State.Should().Be(UserState.None);
    }

    [Fact]
    public async Task HandleStudentUsernameInput_UnknownUser_AddsPendingInvitation()
    {
        var sut = BuildSut("teacher");
        _db.GetUserByUsernameAsync(Arg.Any<string>()).Returns((DbUser?)null);
        _db.AddPendingInvitationAsync(Arg.Any<long>(), Arg.Any<string>()).Returns(Task.CompletedTask);
        _db.GetUserAsync(UserId).Returns(new DbUser { TelegramId = UserId, Role = "Teacher", IsActivated = true });

        await sut.HandleStudentUsernameInputAsync(UserId, ChatId, "newstudent", CancellationToken.None);

        await _db.Received(1).AddPendingInvitationAsync(UserId, "newstudent");
    }
}


