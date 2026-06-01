using FluentAssertions;
using NSubstitute;
using Telegram.Bot;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.Services;
using KnowlBot.Services.Handlers;
using Xunit;
using DbUser = KnowlBot.Models.User;

namespace KnowlBot.Tests;

public class QuizHandlerTests
{
    private readonly ITelegramBotClient _bot = Substitute.For<ITelegramBotClient>();
    private readonly IDatabaseService _db = Substitute.For<IDatabaseService>();
    private readonly ConversationStateManager _states = new();
    private readonly QuizHandler _sut;

    private const long UserId = 1;
    private const long ChatId = 100;

    public QuizHandlerTests()
    {
        _sut = new QuizHandler(_bot, _db, _states);
        _db.GetUserAsync(Arg.Any<long>())
            .Returns(new DbUser { TelegramId = UserId, Role = "Student" });
        _db.GetTopicsForStudentAsync(Arg.Any<long>())
            .Returns(new List<string>());
        _db.RecordQuizAnswerAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(Task.CompletedTask);
        _db.ReduceWrongCountAsync(Arg.Any<long>(), Arg.Any<string>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleCallback_MenuQuiz_ResetsState()
    {
        _states.Set(UserId, new ConversationState { QuizAmount = 10, QuizIndex = 5 });

        await _sut.HandleCallbackAsync("menu_quiz", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(0);
        _states.Get(UserId).QuizIndex.Should().Be(0);
    }

    [Fact]
    public async Task HandleCallback_QuizDirection_StoresDirection()
    {
        await _sut.HandleCallbackAsync("quiz_dir_eu", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizDirection.Should().Be("eu");
    }

    [Fact]
    public async Task HandleCallback_QuizDirection_Ue_StoresUeDirection()
    {
        await _sut.HandleCallbackAsync("quiz_dir_ue", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizDirection.Should().Be("ue");
    }

    [Fact]
    public async Task HandleCallback_QuizAmount_StoresAmount()
    {
        await _sut.HandleCallbackAsync("quiz_amt_10", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(10);
    }

    [Fact]
    public async Task HandleCallback_QuizLevel_StoresLevel()
    {
        await _sut.HandleCallbackAsync("quiz_lvl_B2", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizLevel.Should().Be("B2");
    }

    [Fact]
    public async Task HandleCallback_QuizLevelAny_SetsNullLevel()
    {
        await _sut.HandleCallbackAsync("quiz_lvl_any", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizLevel.Should().BeNull();
    }

    [Fact]
    public async Task HandleCallback_QuizCancel_ResetsState()
    {
        _states.Set(UserId, new ConversationState { QuizAmount = 5, QuizDirection = "eu" });

        await _sut.HandleCallbackAsync("quiz_cancel", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(0);
        _states.Get(UserId).QuizDirection.Should().BeNull();
    }

    [Fact]
    public async Task HandleQuizCustomAmountInput_ValidNumber_StoresAmount()
    {
        await _sut.HandleQuizCustomAmountInputAsync(UserId, ChatId, "15", CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(15);
        _states.Get(UserId).State.Should().Be(UserState.None);
    }

    [Fact]
    public async Task HandleQuizCustomAmountInput_TooLow_DoesNotStoreAmount()
    {
        await _sut.HandleQuizCustomAmountInputAsync(UserId, ChatId, "1", CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(0);
    }

    [Fact]
    public async Task HandleQuizCustomAmountInput_TooHigh_DoesNotStoreAmount()
    {
        await _sut.HandleQuizCustomAmountInputAsync(UserId, ChatId, "101", CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(0);
    }

    [Fact]
    public async Task HandleQuizCustomAmountInput_NonNumeric_DoesNotStoreAmount()
    {
        await _sut.HandleQuizCustomAmountInputAsync(UserId, ChatId, "abc", CancellationToken.None);

        _states.Get(UserId).QuizAmount.Should().Be(0);
    }

    [Fact]
    public async Task HandleCallback_QuizAns_CorrectAnswer_IncrementsScore()
    {
        var words = MakeWords(4);
        var correctWord = words[0];

        _states.Set(UserId, new ConversationState
        {
            QuizWords = words,
            QuizIndex = 0,
            QuizScore = 0,
            QuizMessageId = 99,
            QuizDirection = "eu"
        });

        await _sut.HandleCallbackAsync($"quiz_ans_0_{correctWord.Id}", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizScore.Should().Be(1);
    }

    [Fact]
    public async Task HandleCallback_QuizAns_WrongAnswer_ScoreUnchanged()
    {
        var words = MakeWords(4);

        _states.Set(UserId, new ConversationState
        {
            QuizWords = words,
            QuizIndex = 0,
            QuizScore = 0,
            QuizMessageId = 99,
            QuizDirection = "eu"
        });

        // Pass a different word's ID → wrong answer
        await _sut.HandleCallbackAsync($"quiz_ans_0_{words[1].Id}", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizScore.Should().Be(0);
    }

    [Fact]
    public async Task HandleCallback_QuizAns_CorrectAnswer_RecordsToDb()
    {
        var words = MakeWords(4);
        var correctWord = words[0];

        _states.Set(UserId, new ConversationState
        {
            QuizWords = words,
            QuizIndex = 0,
            QuizScore = 0,
            QuizMessageId = 99,
            QuizDirection = "eu"
        });

        await _sut.HandleCallbackAsync($"quiz_ans_0_{correctWord.Id}", UserId, ChatId, CancellationToken.None);

        // Fire-and-forget, but should have been invoked
        _ = _db.Received().RecordQuizAnswerAsync(UserId, correctWord.Id, true);
    }

    [Fact]
    public async Task HandleCallback_QuizAns_StaleIndex_IsIgnored()
    {
        var words = MakeWords(4);

        _states.Set(UserId, new ConversationState
        {
            QuizWords = words,
            QuizIndex = 2, // current is 2, but callback says 0
            QuizScore = 0,
            QuizMessageId = 99,
        });

        await _sut.HandleCallbackAsync($"quiz_ans_0_{words[0].Id}", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizScore.Should().Be(0);
    }

    [Fact]
    public async Task HandleCallback_QuizAns_MistakesMode_CorrectAnswer_ReducesWrongCount()
    {
        var words = MakeWords(4);
        var correctWord = words[0];

        _states.Set(UserId, new ConversationState
        {
            QuizWords = words,
            QuizIndex = 0,
            QuizScore = 0,
            QuizMessageId = 99,
            QuizDirection = "eu",
            IsMistakesQuiz = true
        });

        await _sut.HandleCallbackAsync($"quiz_ans_0_{correctWord.Id}", UserId, ChatId, CancellationToken.None);

        _ = _db.Received().ReduceWrongCountAsync(UserId, correctWord.Id);
    }

    [Fact]
    public async Task StartQuiz_LessThan2Words_ResetsState()
    {
        _states.Set(UserId, new ConversationState
        {
            QuizDirection = "eu",
            QuizAmount = 5
        });

        _db.GetWordsForQuizAsync(UserId, null, null, 5)
            .Returns(new List<Word> { MakeWords(1)[0] });

        await _sut.HandleCallbackAsync("quiz_top_any", UserId, ChatId, CancellationToken.None);

        _states.Get(UserId).QuizWords.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<Word> MakeWords(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Word
            {
                Id                    = $"id{i:D24}",
                OriginalWord          = $"word{i}",
                MostlyUsedTranslation = $"слово{i}"
            })
            .ToList();
}

