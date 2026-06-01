using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.UI;

namespace KnowlBot.Services.Handlers;

public sealed class QuizHandler(ITelegramBotClient bot, IDatabaseService db, ConversationStateManager states)
    : HandlerBase(bot, db, states)
{
    public async Task HandleCallbackAsync(string data, long userId, long chatId, CancellationToken ct)
    {
        switch (data)
        {
            case "menu_quiz":
                ResetState(userId);
                await SendQuizDirectionAsync(chatId, ct);
                return;
            case "menu_mistakes":
                ResetState(userId);
                MutateState(userId, state => state.IsMistakesQuiz = true);
                await SendMistakesDirectionAsync(chatId, ct);
                return;
            case "quiz_amt_custom":
                await PromptQuizCustomAmountAsync(userId, chatId, ct);
                return;
            case "quiz_cancel":
                ResetState(userId);
                await GoMenuAsync(userId, chatId, ct);
                return;
        }

        if (data.StartsWith("quiz_dir_"))
        {
            MutateState(userId, state => state.QuizDirection = data["quiz_dir_".Length..]);
            await SendQuizAmountAsync(chatId, ct);
            return;
        }

        if (data.StartsWith("quiz_amt_"))
        {
            if (int.TryParse(data["quiz_amt_".Length..], out var amount))
            {
                MutateState(userId, state => state.QuizAmount = amount);
                if (GetState(userId).IsMistakesQuiz)
                {
                    await StartMistakesQuizAsync(userId, chatId, ct);
                }
                else
                {
                    await SendQuizLevelAsync(chatId, ct);
                }
            }

            return;
        }

        if (data.StartsWith("quiz_lvl_"))
        {
            MutateState(userId, state => state.QuizLevel = data["quiz_lvl_".Length..] == "any" ? null : data["quiz_lvl_".Length..]);
            await SendQuizTopicAsync(chatId, await Db.GetTopicsForStudentAsync(userId), ct);
            return;
        }

        if (data.StartsWith("quiz_top_"))
        {
            await HandleQuizTopicAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("quiz_ans_"))
        {
            await HandleQuizAnswerCallbackAsync(userId, chatId, data, ct);
            return;
        }

        if (data.StartsWith("quiz_next_"))
        {
            if (int.TryParse(data["quiz_next_".Length..], out var nextIndex))
            {
                MutateState(userId, state => state.QuizIndex = nextIndex);
                await SendQuizQuestionAsync(userId, chatId, ct);
            }
        }
    }

    public async Task HandleQuizCustomAmountInputAsync(long userId, long chatId, string text, CancellationToken ct)
    {
        if (!int.TryParse(text.Trim(), out var amount) || amount < 2 || amount > 100)
        {
            await Bot.SendMessage(chatId, "❌ Please enter a number between 2 and 100:", cancellationToken: ct);
            return;
        }

        var state = GetState(userId);
        state.State = UserState.None;
        state.QuizAmount = amount;
        SetState(userId, state);

        if (state.IsMistakesQuiz)
        {
            await StartMistakesQuizAsync(userId, chatId, ct);
        }
        else
        {
            await SendQuizLevelAsync(chatId, ct);
        }
    }

    private Task SendQuizDirectionAsync(long chatId, CancellationToken ct) =>
        Bot.SendMessage(
            chatId,
            "🧩 *Quiz*\n\nChoose quiz direction:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.QuizDirectionSelection(),
            cancellationToken: ct);

    private Task SendMistakesDirectionAsync(long chatId, CancellationToken ct) =>
        Bot.SendMessage(
            chatId,
            "💪 *Work on Mistakes*\n\nI'll select your most-missed words. Choose direction:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.QuizDirectionSelection(),
            cancellationToken: ct);

    private Task SendQuizAmountAsync(long chatId, CancellationToken ct) =>
        Bot.SendMessage(chatId, "📦 How many words in this session?", replyMarkup: Keyboards.QuizAmountSelection(), cancellationToken: ct);

    private Task SendQuizLevelAsync(long chatId, CancellationToken ct) =>
        Bot.SendMessage(
            chatId,
            "🎯 Filter by CEFR level? _(optional)_",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.CefrLevelButtons("quiz_lvl_"),
            cancellationToken: ct);

    private Task SendQuizTopicAsync(long chatId, IReadOnlyList<string> topics, CancellationToken ct) =>
        Bot.SendMessage(
            chatId,
            "🏷️ Filter by topic? _(optional)_",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.QuizTopicButtons(topics),
            cancellationToken: ct);

    private async Task PromptQuizCustomAmountAsync(long userId, long chatId, CancellationToken ct)
    {
        MutateState(userId, state => state.State = UserState.AwaitingQuizCustomAmount);
        var backTarget = GetState(userId).IsMistakesQuiz ? "menu_mistakes" : "menu_quiz";
        await Bot.SendMessage(chatId, "✏️ Enter the number of words (2–100):", replyMarkup: Keyboards.BackButton(backTarget), cancellationToken: ct);
    }

    private async Task HandleQuizTopicAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var raw = data["quiz_top_".Length..];
        var topics = await Db.GetTopicsForStudentAsync(userId);

        MutateState(userId, state =>
        {
            state.QuizTopic = raw == "any" || !int.TryParse(raw, out var index) || index < 0 || index >= topics.Count
                ? null
                : topics[index];
        });

        await StartQuizAsync(userId, chatId, ct);
    }

    private async Task StartQuizAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var count = state.QuizAmount > 0 ? state.QuizAmount : 5;
        var words = await Db.GetWordsForQuizAsync(userId, state.QuizLevel, state.QuizTopic, count);

        if (words.Count < 2)
        {
            ResetState(userId);
            await Bot.SendMessage(
                chatId,
                "📭 Not enough words match the selected filters (need at least 2). Try different filters or add more vocabulary first.",
                cancellationToken: ct);
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        state.State = UserState.None;
        state.QuizWords = words;
        state.QuizIndex = 0;
        state.QuizScore = 0;
        SetState(userId, state);

        var direction = state.QuizDirection == "ue" ? "Ukr → Eng" : "Eng → Ukr";
        var level = state.QuizLevel is not null ? $" | Level: {state.QuizLevel}" : string.Empty;
        var topic = state.QuizTopic is not null ? $" | Topic: {state.QuizTopic}" : string.Empty;

        await Bot.SendMessage(
            chatId,
            $"🧩 Quiz starting!\n*{direction}* | {words.Count} words{level}{topic}\n\nGood luck! 🍀",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await SendQuizQuestionAsync(userId, chatId, ct);
    }

    private async Task StartMistakesQuizAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var count = state.QuizAmount > 0 ? state.QuizAmount : 10;
        var words = await Db.GetWordsForMistakesAsync(userId, count);

        if (words.Count < 2)
        {
            ResetState(userId);
            await Bot.SendMessage(
                chatId,
                "🎉 No mistakes to practice! You haven't made enough errors yet, or you've already mastered everything. Try the regular quiz first.",
                cancellationToken: ct);
            await GoMenuAsync(userId, chatId, ct);
            return;
        }

        state.State = UserState.None;
        state.QuizWords = words;
        state.QuizIndex = 0;
        state.QuizScore = 0;
        state.IsMistakesQuiz = true;
        SetState(userId, state);

        var direction = state.QuizDirection == "ue" ? "Ukr → Eng" : "Eng → Ukr";
        await Bot.SendMessage(
            chatId,
            $"💪 *Work on Mistakes* | {words.Count} words\n\n*{direction}*\nCorrect answers will cut each word's error count in half. Let's go! 🔥",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await SendQuizQuestionAsync(userId, chatId, ct);
    }

    private async Task SendQuizQuestionAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.QuizIndex >= state.QuizWords.Count)
        {
            await SendQuizResultsAsync(userId, chatId, ct);
            return;
        }

        var correct = state.QuizWords[state.QuizIndex];
        var direction = state.QuizDirection ?? "eu";
        var question = WordFormatter.QuizQuestion(correct, direction);
        var options = BuildOptions(correct, state.QuizWords);

        var message = await Bot.SendMessage(
            chatId,
            $"🧩 *Question {state.QuizIndex + 1}/{state.QuizWords.Count}*\n\n*{WordFormatter.EscapeMarkdown(question)}*\n\nChoose the correct translation:",
            parseMode: ParseMode.Markdown,
            replyMarkup: Keyboards.QuizAnswerGrid(state.QuizIndex, options, direction),
            cancellationToken: ct);

        state.QuizMessageId = message.MessageId;
        SetState(userId, state);
    }

    private async Task HandleQuizAnswerCallbackAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var parts = data["quiz_ans_".Length..].Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var qIdx))
        {
            return;
        }
        var selectedId = parts[1];

        await HandleQuizAnswerAsync(userId, chatId, qIdx, selectedId, ct);
    }

    private async Task HandleQuizAnswerAsync(long userId, long chatId, int qIdx, string selectedId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (qIdx != state.QuizIndex || qIdx < 0 || qIdx >= state.QuizWords.Count)
        {
            return;
        }

        var correct = state.QuizWords[qIdx];
        var direction = state.QuizDirection ?? "eu";
        var isCorrect = selectedId == correct.Id;
        if (isCorrect)
        {
            state.QuizScore++;
        }

        SetState(userId, state);
        _ = Db.RecordQuizAnswerAsync(userId, correct.Id, isCorrect);
        if (isCorrect && state.IsMistakesQuiz)
        {
            _ = Db.ReduceWrongCountAsync(userId, correct.Id);
        }

        var correctLabel = WordFormatter.Truncate(WordFormatter.QuizAnswer(correct, direction), 60);
        var feedback = isCorrect
            ? "✅ *Correct!*"
            : $"❌ Wrong! The answer was:\n_{WordFormatter.EscapeMarkdown(correctLabel)}_";

        var nextIdx = qIdx + 1;
        var buttonText = nextIdx >= state.QuizWords.Count ? "🏁 See Results" : "▶️ Next";

        try
        {
            await Bot.EditMessageText(
                chatId,
                state.QuizMessageId,
                $"🧩 *Question {qIdx + 1}/{state.QuizWords.Count}*\n\n{feedback}",
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.SingleAction(buttonText, $"quiz_next_{nextIdx}"),
                cancellationToken: ct);
        }
        catch
        {
            await Bot.SendMessage(
                chatId,
                feedback,
                parseMode: ParseMode.Markdown,
                replyMarkup: Keyboards.SingleAction(buttonText, $"quiz_next_{nextIdx}"),
                cancellationToken: ct);
        }
    }

    private async Task SendQuizResultsAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var total = state.QuizWords.Count;
        var score = state.QuizScore;
        var percent = total > 0 ? (int)Math.Round(score * 100.0 / total) : 0;
        var isMistakes = state.IsMistakesQuiz;
        var medal = percent switch
        {
            >= 90 => "🥇",
            >= 70 => "🥈",
            >= 50 => "🥉",
            _ => "📚"
        };

        var encouragement = isMistakes
            ? percent switch
            {
                >= 90 => "Fantastic — you've crushed those mistakes! 🎉",
                >= 70 => "Great progress! Keep drilling the tough ones 💪",
                >= 50 => "Good effort! Repeat this session to clear more errors 📖",
                _ => "Don't give up — every attempt reduces your errors! 💡"
            }
            : percent switch
            {
                >= 90 => "Excellent work! 🎉",
                >= 70 => "Good job! Keep it up 💪",
                >= 50 => "Not bad! Practice more 📖",
                _ => "Keep studying — you'll get there! 💡"
            };

        var title = isMistakes ? "💪 *Mistakes Session Complete!*" : "🧩 *Quiz Complete!*";

        ResetState(userId);
        await Bot.SendMessage(
            chatId,
            $"{medal} {title}\n\nScore: *{score}/{total}* ({percent}%)\n\n{encouragement}",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        await GoMenuAsync(userId, chatId, ct);
    }

    private static List<Word> BuildOptions(Word correct, IReadOnlyList<Word> words) =>
        words
            .Where(word => word.Id != correct.Id)
            .OrderBy(_ => Random.Shared.Next())
            .Take(3)
            .Append(correct)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();
}

