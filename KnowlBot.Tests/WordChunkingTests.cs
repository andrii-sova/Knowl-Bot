using FluentAssertions;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.Services;
using KnowlBot.Services.Handlers;
using Xunit;

namespace KnowlBot.Tests;

/// <summary>
/// Tests for SendWordListAsync chunking behaviour (≤10 words → 1 message, >10 → multiple).
/// Uses a minimal HandlerBase subclass that exposes the protected method.
/// </summary>
public class WordChunkingTests
{
    private readonly ITelegramBotClient _bot  = Substitute.For<ITelegramBotClient>();
    private readonly IDatabaseService   _db   = Substitute.For<IDatabaseService>();
    private readonly ConversationStateManager _states = new();
    private readonly TestHandler _sut;

    private const long ChatId = 100;

    public WordChunkingTests()
    {
        _sut = new TestHandler(_bot, _db, _states);

        // Make every SendMessage call return a non-null Message so MessageId access never NREs
        _bot.SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => System.Threading.Tasks.Task.FromResult(new Message()));
    }

    // ── Message count ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SendWordList_ZeroLines_SendsNoMessages()
    {
        await _sut.ExposeSendWordListAsync(ChatId, [], default);

        MessagesSent().Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task SendWordList_TenOrFewer_SendsOneMessage(int count)
    {
        var lines = Lines(count);

        await _sut.ExposeSendWordListAsync(ChatId, lines, default);

        MessagesSent().Should().Be(1);
    }

    [Theory]
    [InlineData(11, 2)]
    [InlineData(20, 2)]
    [InlineData(21, 3)]
    [InlineData(30, 3)]
    [InlineData(31, 4)]
    public async Task SendWordList_MoreThanTen_SendsMultipleMessages(int wordCount, int expectedMessages)
    {
        var lines = Lines(wordCount);

        await _sut.ExposeSendWordListAsync(ChatId, lines, default);

        MessagesSent().Should().Be(expectedMessages);
    }

    // ── Header placement ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendWordList_WithHeader_SingleChunk_HeaderPrependedToMessage()
    {
        var texts = CaptureTexts();

        await _sut.ExposeSendWordListAsync(ChatId, Lines(5), default, header: "📋 My Header");

        texts.Should().HaveCount(1);
        texts[0].Should().StartWith("📋 My Header\n\n");
    }

    [Fact]
    public async Task SendWordList_WithHeader_MultipleChunks_HeaderOnlyOnFirst()
    {
        var texts = CaptureTexts();

        await _sut.ExposeSendWordListAsync(ChatId, Lines(15), default, header: "📋 My Header");

        texts.Should().HaveCount(2);
        texts[0].Should().StartWith("📋 My Header\n\n");
        texts[1].Should().NotContain("📋 My Header");
    }

    [Fact]
    public async Task SendWordList_WithoutHeader_NoExtraTextPrepended()
    {
        var texts = CaptureTexts();

        await _sut.ExposeSendWordListAsync(ChatId, ["word 1", "word 2"], default);

        texts.Should().HaveCount(1);
        texts[0].Should().Be("word 1\n\nword 2");
    }

    // ── Markup placement ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendWordList_WithMarkup_SingleChunk_MarkupOnThatMessage()
    {
        var markups = CaptureMarkups();
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("OK", "ok"));

        await _sut.ExposeSendWordListAsync(ChatId, Lines(5), default, finalMarkup: keyboard);

        markups.Should().HaveCount(1);
        markups[0].Should().BeSameAs(keyboard);
    }

    [Fact]
    public async Task SendWordList_WithMarkup_MultipleChunks_MarkupOnlyOnLastMessage()
    {
        var markups = CaptureMarkups();
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("OK", "ok"));

        await _sut.ExposeSendWordListAsync(ChatId, Lines(25), default, finalMarkup: keyboard);

        markups.Should().HaveCount(3);
        markups[0].Should().BeNull();
        markups[1].Should().BeNull();
        markups[2].Should().BeSameAs(keyboard);
    }

    [Fact]
    public async Task SendWordList_WithoutMarkup_AllMessagesHaveNullMarkup()
    {
        var markups = CaptureMarkups();

        await _sut.ExposeSendWordListAsync(ChatId, Lines(11), default);

        markups.Should().AllSatisfy(m => m.Should().BeNull());
    }

    // ── WordEntryHandler integration ──────────────────────────────────────────

    [Fact]
    public async Task WordEntryHandler_ElevenWords_SendsTwoPreviewChunks()
    {
        var ai = Substitute.For<IAiService>();
        ai.EnrichWordsAsync(Arg.Any<string>())
          .Returns(MakePendingEntries(11));

        var handler = new WordEntryHandler(_bot, _db, _states, ai);

        await handler.HandleWordsInputAsync(
            addedById: 1L, forStudentId: 1L,
            inputText: "word1 word2",
            chatId: ChatId,
            ct: default);

        // 1 notice + 2 word chunks + 1 topic question = 4 total SendMessage calls
        MessagesSent().Should().Be(4);
    }

    [Fact]
    public async Task WordEntryHandler_TenWords_SendsOnePreviewChunk()
    {
        var ai = Substitute.For<IAiService>();
        ai.EnrichWordsAsync(Arg.Any<string>())
          .Returns(MakePendingEntries(10));

        var handler = new WordEntryHandler(_bot, _db, _states, ai);

        await handler.HandleWordsInputAsync(
            addedById: 1L, forStudentId: 1L,
            inputText: "word1 word2",
            chatId: ChatId,
            ct: default);

        // 1 notice + 1 word chunk + 1 topic question = 3
        MessagesSent().Should().Be(3);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> Lines(int count) =>
        Enumerable.Range(1, count).Select(i => $"line {i}").ToList();

    private static List<PendingWordEntry> MakePendingEntries(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new PendingWordEntry
            {
                Word                    = $"word{i}",
                CefrLevel               = "B1",
                Synonym                 = "syn",
                Transcription           = "trn",
                MostlyUsedTranslation   = $"слово{i}",
                OtherTranslation        = null,
                ExampleUsage            = "example",
                ExampleUsageTranslation = "приклад"
            })
            .ToList();

    private int MessagesSent() =>
        _bot.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "SendRequest"
                     && c.GetArguments()[0] is IRequest<Message>);

    private List<string?> CaptureTexts()
    {
        var list = new List<string?>();
        _bot.SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(callInfo =>
            {
                if (callInfo.Arg<IRequest<Message>>() is SendMessageRequest req)
                    list.Add(req.Text);
                return System.Threading.Tasks.Task.FromResult(new Message());
            });
        return list;
    }

    private List<ReplyMarkup?> CaptureMarkups()
    {
        var list = new List<ReplyMarkup?>();
        _bot.SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(callInfo =>
            {
                if (callInfo.Arg<IRequest<Message>>() is SendMessageRequest req)
                    list.Add(req.ReplyMarkup);
                return System.Threading.Tasks.Task.FromResult(new Message());
            });
        return list;
    }

    private sealed class TestHandler(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states)
        : HandlerBase(bot, db, states)
    {
        public Task ExposeSendWordListAsync(
            long chatId,
            IReadOnlyList<string> lines,
            System.Threading.CancellationToken ct,
            string? header = null,
            ReplyMarkup? finalMarkup = null,
            int chunkSize = 10)
            => SendWordListAsync(chatId, lines, ct, header, finalMarkup, chunkSize);
    }
}
