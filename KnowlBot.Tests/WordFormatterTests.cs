using FluentAssertions;
using KnowlBot.Models;
using KnowlBot.UI;
using Xunit;

namespace KnowlBot.Tests;

public class WordFormatterTests
{
    // ── EscapeMarkdown ───────────────────────────────────────────────────────

    [Fact]
    public void EscapeMarkdown_EmptyString_ReturnsEmpty()
    {
        WordFormatter.EscapeMarkdown("").Should().Be("");
    }

    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("it_works", @"it\_works")]
    [InlineData("*bold*", @"\*bold\*")]
    [InlineData("`code`", @"\`code\`")]
    [InlineData("[link]", @"\[link\]")]
    [InlineData("(paren)", @"\(paren\)")]
    [InlineData("a-b", @"a\-b")]
    [InlineData("end.", @"end\.")]
    public void EscapeMarkdown_EscapesSpecialChars(string input, string expected)
    {
        WordFormatter.EscapeMarkdown(input).Should().Be(expected);
    }

    // ── Truncate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_ShortString_ReturnsUnchanged()
    {
        WordFormatter.Truncate("hello", 10).Should().Be("hello");
    }

    [Fact]
    public void Truncate_ExactLength_ReturnsUnchanged()
    {
        WordFormatter.Truncate("hello", 5).Should().Be("hello");
    }

    [Fact]
    public void Truncate_LongString_AppendEllipsis()
    {
        var result = WordFormatter.Truncate("hello world", 7);
        result.Should().EndWith("…");
        result.Length.Should().BeLessOrEqualTo(8);
    }

    [Fact]
    public void Truncate_NullOrEmpty_ReturnsAsIs()
    {
        WordFormatter.Truncate("", 5).Should().Be("");
        WordFormatter.Truncate(null!, 5).Should().BeNull();
    }

    // ── QuizQuestion / QuizAnswer ────────────────────────────────────────────

    [Fact]
    public void QuizQuestion_EuDirection_ReturnsEnglish()
    {
        var word = new Word { OriginalWord = "apple", MostlyUsedTranslation = "яблуко" };
        WordFormatter.QuizQuestion(word, "eu").Should().Be("apple");
    }

    [Fact]
    public void QuizQuestion_UeDirection_ReturnsUkrainian()
    {
        var word = new Word { OriginalWord = "apple", MostlyUsedTranslation = "яблуко" };
        WordFormatter.QuizQuestion(word, "ue").Should().Be("яблуко");
    }

    [Fact]
    public void QuizAnswer_EuDirection_ReturnsMostlyUsedTranslation()
    {
        var word = new Word { OriginalWord = "apple", MostlyUsedTranslation = "яблуко" };
        WordFormatter.QuizAnswer(word, "eu").Should().Be("яблуко");
    }

    [Fact]
    public void QuizAnswer_UeDirection_ReturnsEnglish()
    {
        var word = new Word { OriginalWord = "apple", MostlyUsedTranslation = "яблуко" };
        WordFormatter.QuizAnswer(word, "ue").Should().Be("apple");
    }

    // ── FormatWordLine ───────────────────────────────────────────────────────

    [Fact]
    public void FormatWordLine_AllFields_FormatsCorrectly()
    {
        var word = new Word
        {
            OriginalWord            = "diminish",
            CefrLevel               = "B2",
            Transcription           = "dɪˈmɪnɪʃ",
            Synonym                 = "decrease",
            MostlyUsedTranslation   = "зменшувати",
            OtherTranslation        = "применшувати",
            ExampleUsage            = "Her enthusiasm did not diminish",
            ExampleUsageTranslation = "Її ентузіазм не зменшився"
        };

        var result = WordFormatter.FormatWordLine(word);
        result.Should().Contain("B2");
        result.Should().Contain("diminish");
        result.Should().Contain("dɪˈmɪnɪʃ");
        result.Should().Contain("decrease");
        result.Should().Contain("зменшувати");
        result.Should().Contain("применшувати");
        result.Should().Contain("Her enthusiasm did not diminish");
    }

    [Fact]
    public void FormatWordLine_NoOptionalFields_SkipsThem()
    {
        var word = new Word
        {
            OriginalWord          = "apple",
            CefrLevel             = "A1",
            MostlyUsedTranslation = "яблуко"
        };

        var result = WordFormatter.FormatWordLine(word);
        result.Should().Contain("A1");
        result.Should().Contain("apple");
        result.Should().Contain("яблуко");
        result.Should().NotContain("(");  // no synonym or example in parens
    }

    // ── FormatPendingLine ────────────────────────────────────────────────────

    [Fact]
    public void FormatPendingLine_WithAllFields_FormatsCorrectly()
    {
        var entry = new PendingWordEntry
        {
            Word                    = "diminish",
            CefrLevel               = "B2",
            Transcription           = "dɪˈmɪnɪʃ",
            Synonym                 = "decrease",
            MostlyUsedTranslation   = "зменшувати",
            OtherTranslation        = "применшувати",
            ExampleUsage            = "Her enthusiasm did not diminish",
            ExampleUsageTranslation = "Її ентузіазм не зменшився"
        };

        var result = WordFormatter.FormatPendingLine(entry);
        result.Should().Contain("B2");
        result.Should().Contain("diminish");
        result.Should().Contain("зменшувати");
    }

    [Fact]
    public void FormatPendingLine_WithoutLevel_NoLevelPrefix()
    {
        var entry = new PendingWordEntry
        {
            Word                  = "apple",
            MostlyUsedTranslation = "яблуко"
        };

        var result = WordFormatter.FormatPendingLine(entry);
        result.Should().Contain("apple");
        result.Should().Contain("яблуко");
        result.Should().NotContain("[A");
    }
}
