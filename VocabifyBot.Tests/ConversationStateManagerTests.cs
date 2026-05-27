using FluentAssertions;
using VocabifyBot.Models;
using VocabifyBot.Services;
using Xunit;

namespace VocabifyBot.Tests;

public class ConversationStateManagerTests
{
    private readonly ConversationStateManager _sut = new();

    [Fact]
    public void Get_UnknownUser_ReturnsDefaultState()
    {
        var state = _sut.Get(999);

        state.State.Should().Be(UserState.None);
        state.QuizWords.Should().BeEmpty();
    }

    [Fact]
    public void Set_ThenGet_ReturnsStoredState()
    {
        var state = new ConversationState { State = UserState.AwaitingDisplayName };
        _sut.Set(1, state);

        _sut.Get(1).State.Should().Be(UserState.AwaitingDisplayName);
    }

    [Fact]
    public void Reset_ClearsStoredState()
    {
        _sut.Set(1, new ConversationState { State = UserState.AwaitingWordsForStudent });
        _sut.Reset(1);

        _sut.Get(1).State.Should().Be(UserState.None);
    }

    [Fact]
    public void Mutate_ChangesPropertyOnExistingState()
    {
        _sut.Set(1, new ConversationState { QuizAmount = 5 });
        _sut.Mutate(1, s => s.QuizAmount = 10);

        _sut.Get(1).QuizAmount.Should().Be(10);
    }

    [Fact]
    public void Mutate_OnUnknownUser_CreatesAndPersistsState()
    {
        _sut.Mutate(42, s => s.State = UserState.AwaitingSearchQuery);

        _sut.Get(42).State.Should().Be(UserState.AwaitingSearchQuery);
    }

    [Fact]
    public void Mutate_MultipleUsers_AreIsolated()
    {
        _sut.Mutate(1, s => s.State = UserState.AwaitingDisplayName);
        _sut.Mutate(2, s => s.State = UserState.AwaitingPersonalWords);

        _sut.Get(1).State.Should().Be(UserState.AwaitingDisplayName);
        _sut.Get(2).State.Should().Be(UserState.AwaitingPersonalWords);
    }

    [Fact]
    public void DeleteMode_SetAndRetrieved()
    {
        _sut.Mutate(1, s =>
        {
            s.DeleteMode = "keep";
            s.DeleteLevel = "B2";
        });

        var result = _sut.Get(1);
        result.DeleteMode.Should().Be("keep");
        result.DeleteLevel.Should().Be("B2");
    }

    [Fact]
    public void BrowsingState_SetAndRetrieved()
    {
        var words = new List<Word>
        {
            new() { OriginalWord = "apple", Translation = "яблуко" }
        };

        _sut.Mutate(1, s =>
        {
            s.BrowsingWords = words;
            s.BrowsingOffset = 20;
            s.BrowsingMode = "level";
        });

        var result = _sut.Get(1);
        result.BrowsingWords.Should().HaveCount(1);
        result.BrowsingOffset.Should().Be(20);
        result.BrowsingMode.Should().Be("level");
    }
}
