using FluentAssertions;
using KnowlBot.Models;
using Xunit;

namespace KnowlBot.Tests;

public class ModelTests
{
    // ── ConversationState defaults ────────────────────────────────────────────

    [Fact]
    public void ConversationState_DefaultState_IsNone()
    {
        var state = new ConversationState();
        state.State.Should().Be(UserState.None);
    }

    [Fact]
    public void ConversationState_DefaultLists_AreEmpty()
    {
        var state = new ConversationState();
        state.PendingWords.Should().BeEmpty();
        state.QuizWords.Should().BeEmpty();
        state.VocabWords.Should().BeEmpty();
        state.BrowsingWords.Should().BeEmpty();
        state.DeleteWords.Should().BeEmpty();
        state.CachedTopics.Should().BeEmpty();
        state.GenPreview.Should().BeEmpty();
    }

    [Fact]
    public void ConversationState_DeleteMode_SetAndRead()
    {
        var state = new ConversationState { DeleteMode = "selected" };
        state.DeleteMode.Should().Be("selected");
    }

    // ── Word model ────────────────────────────────────────────────────────────

    [Fact]
    public void Word_DefaultId_IsGeneratedObjectId()
    {
        var word = new Word();
        word.Id.Should().NotBeNullOrEmpty();
        word.Id.Should().HaveLength(24); // ObjectId hex string length
    }

    [Fact]
    public void Word_Properties_SetCorrectly()
    {
        var word = new Word
        {
            OriginalWord          = "apple",
            CefrLevel             = "A1",
            MostlyUsedTranslation = "яблуко",
            Synonym               = "fruit"
        };

        word.OriginalWord.Should().Be("apple");
        word.CefrLevel.Should().Be("A1");
        word.MostlyUsedTranslation.Should().Be("яблуко");
        word.Synonym.Should().Be("fruit");
    }

    // ── PendingWordEntry record ────────────────────────────────────────────────

    [Fact]
    public void PendingWordEntry_ValueEquality()
    {
        var a = new PendingWordEntry { Word = "apple", MostlyUsedTranslation = "яблуко", CefrLevel = "A1" };
        var b = new PendingWordEntry { Word = "apple", MostlyUsedTranslation = "яблуко", CefrLevel = "A1" };

        a.Should().Be(b);
    }

    [Fact]
    public void PendingWordEntry_DifferentValues_NotEqual()
    {
        var a = new PendingWordEntry { Word = "apple", MostlyUsedTranslation = "яблуко" };
        var b = new PendingWordEntry { Word = "cat",   MostlyUsedTranslation = "кіт" };

        a.Should().NotBe(b);
    }

    // ── UserState enum ─────────────────────────────────────────────────────────

    [Fact]
    public void UserState_ContainsExpectedValues()
    {
        var values = Enum.GetNames<UserState>();
        values.Should().Contain("None");
        values.Should().Contain("AwaitingDisplayName");
        values.Should().Contain("AwaitingWordsForStudent");
        values.Should().Contain("AwaitingQuizCustomAmount");
        values.Should().Contain("AwaitingWordDeleteInput");
        values.Should().Contain("AwaitingWordRemoval");
    }
}
