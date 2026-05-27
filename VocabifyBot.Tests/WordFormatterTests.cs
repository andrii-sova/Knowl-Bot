using FluentAssertions;
using VocabifyBot.Models;
using VocabifyBot.UI;
using Xunit;

namespace VocabifyBot.Tests;

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
    [InlineData("[link]", @"\[link]")]
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
        result.Length.Should().BeLessOrEqualTo(8); // 7 chars + ellipsis
    }

    [Fact]
    public void Truncate_NullOrEmpty_ReturnsAsIs()
    {
        WordFormatter.Truncate("", 5).Should().Be("");
        WordFormatter.Truncate(null!, 5).Should().BeNull();
    }

    // ── ShortUkr ─────────────────────────────────────────────────────────────

    [Fact]
    public void ShortUkr_WithDash_ReturnsSuffix()
    {
        var result = WordFormatter.ShortUkr("put off — відбити бажання [пут оф]");
        result.Should().Be("відбити бажання");
    }

    [Fact]
    public void ShortUkr_NoDash_StripsBracketsAndParens()
    {
        var result = WordFormatter.ShortUkr("відбити [транскр] (коментар)");
        result.Should().Be("відбити");
    }

    [Fact]
    public void ShortUkr_PlainWord_ReturnsWord()
    {
        WordFormatter.ShortUkr("яблуко").Should().Be("яблуко");
    }

    // ── QuizQuestion / QuizAnswer ────────────────────────────────────────────

    [Fact]
    public void QuizQuestion_EuDirection_ReturnsEnglish()
    {
        var word = new Word { OriginalWord = "apple", Translation = "яблуко" };
        WordFormatter.QuizQuestion(word, "eu").Should().Be("apple");
    }

    [Fact]
    public void QuizQuestion_UeDirection_ReturnsUkrainian()
    {
        var word = new Word { OriginalWord = "apple", Translation = "яблуко" };
        WordFormatter.QuizQuestion(word, "ue").Should().Be("яблуко");
    }

    [Fact]
    public void QuizAnswer_EuDirection_ReturnsShortUkr()
    {
        var word = new Word { OriginalWord = "apple", Translation = "яблуко" };
        WordFormatter.QuizAnswer(word, "eu").Should().Be("яблуко");
    }

    [Fact]
    public void QuizAnswer_UeDirection_ReturnsEnglish()
    {
        var word = new Word { OriginalWord = "apple", Translation = "яблуко" };
        WordFormatter.QuizAnswer(word, "ue").Should().Be("apple");
    }

    // ── FormatWordLine ───────────────────────────────────────────────────────

    [Fact]
    public void FormatWordLine_WithLevelAndTopic_IncludesBoth()
    {
        var word = new Word
        {
            OriginalWord = "apple",
            Translation = "яблуко",
            EnglishLevel = "A1",
            Topic = "Food"
        };

        var result = WordFormatter.FormatWordLine(word);
        result.Should().Contain("A1");
        result.Should().Contain("Food");
        result.Should().Contain("яблуко");
    }

    [Fact]
    public void FormatWordLine_NoLevelNoTopic_ReturnsTranslationOnly()
    {
        var word = new Word { OriginalWord = "apple", Translation = "яблуко" };
        WordFormatter.FormatWordLine(word).Should().Be("яблуко");
    }

    // ── BuildPendingWords ────────────────────────────────────────────────────

    [Fact]
    public void BuildPendingWords_MatchingLines_ParsesCorrectly()
    {
        var result = WordFormatter.BuildPendingWords(
            "apple\ncat",
            "[A1] яблуко\n[A2] кіт");

        result.Should().HaveCount(2);
        result[0].Original.Should().Be("apple");
        result[0].Translation.Should().Be("яблуко");
        result[0].EnglishLevel.Should().Be("A1");
        result[1].Original.Should().Be("cat");
        result[1].EnglishLevel.Should().Be("A2");
    }

    [Fact]
    public void BuildPendingWords_NoLevelPrefix_EnglishLevelIsNull()
    {
        var result = WordFormatter.BuildPendingWords("apple", "яблуко");

        result.Should().HaveCount(1);
        result[0].EnglishLevel.Should().BeNull();
        result[0].Translation.Should().Be("яблуко");
    }

    [Fact]
    public void BuildPendingWords_MoreInputsThanTranslations_UsesOriginalAsFallback()
    {
        var result = WordFormatter.BuildPendingWords("apple\ncat", "яблуко");

        result.Should().HaveCount(2);
        result[1].Translation.Should().Be("cat");
    }

    // ── BuildPendingWordsFromGeneration ──────────────────────────────────────

    [Fact]
    public void BuildPendingWordsFromGeneration_ParsesLevelAndOriginal()
    {
        const string text = "[B2] put you off — відбити бажання [пут ю оф] (phrasal verb)";
        var result = WordFormatter.BuildPendingWordsFromGeneration(text);

        result.Should().HaveCount(1);
        result[0].EnglishLevel.Should().Be("B2");
        result[0].Original.Should().Be("put you off");
        result[0].Translation.Should().Be("put you off — відбити бажання [пут ю оф] (phrasal verb)");
    }

    [Fact]
    public void BuildPendingWordsFromGeneration_SkipsLinesWithoutPrefix()
    {
        const string text = "some header\n[A1] apple — яблуко";
        var result = WordFormatter.BuildPendingWordsFromGeneration(text);

        result.Should().HaveCount(1);
        result[0].EnglishLevel.Should().Be("A1");
    }

    // ── FormatPendingLine ────────────────────────────────────────────────────

    [Fact]
    public void FormatPendingLine_WithLevel_IncludesLevelPrefix()
    {
        var entry = new PendingWordEntry { Translation = "яблуко", EnglishLevel = "A1" };
        WordFormatter.FormatPendingLine(entry).Should().Contain("A1").And.Contain("яблуко");
    }

    [Fact]
    public void FormatPendingLine_WithoutLevel_JustTranslation()
    {
        var entry = new PendingWordEntry { Translation = "яблуко" };
        WordFormatter.FormatPendingLine(entry).Should().Be("яблуко");
    }
}
