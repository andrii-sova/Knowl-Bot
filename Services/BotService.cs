using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using VocabifyBot.Models;

namespace VocabifyBot.Services;

public class BotService
{
    private readonly ITelegramBotClient _bot;
    private readonly DatabaseService    _db;
    private readonly OpenAiService      _openAi;

    private readonly Dictionary<long, ConversationState> _states = new();

    public BotService(ITelegramBotClient bot, DatabaseService db, OpenAiService openAi)
    {
        _bot = bot; _db = db; _openAi = openAi;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _db.InitializeAsync();
        _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] }, ct);
        var me = await _bot.GetMe(ct);
        Console.WriteLine($"✅ Bot @{me.Username} started. Press Ctrl+C to stop.");
    }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    private async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { } msg)           await HandleMessageAsync(msg, ct);
            else if (update.CallbackQuery is { } cbq) await HandleCallbackQueryAsync(cbq, ct);
        }
        catch (Exception ex) { Console.WriteLine($"⚠️  {FullMessage(ex)}"); }
    }

    // ── Message handler ───────────────────────────────────────────────────────

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        if (message.From is null || message.Text is null) return;

        var userId = message.From.Id;
        var chatId = message.Chat.Id;
        var text   = message.Text.Trim();
        var state  = GetState(userId);

        if (text.StartsWith("/start") || text == "/menu")
        {
            ResetState(userId);
            var existing = await _db.GetUserAsync(userId);
            if (existing is not null)
            {
                // Claim any pending invitations in case teacher added them before they started
                if (existing.Role == "Student" && !string.IsNullOrEmpty(message.From.Username))
                    await _db.ClaimPendingInvitationsAsync(userId, message.From.Username);
                await SendMainMenuAsync(chatId, existing.Role, ct, userId);
            }
            else
            {
                await SendRoleSelectionAsync(chatId, ct);
            }
            return;
        }

        switch (state.State)
        {
            case UserState.AwaitingDisplayName:
                await HandleDisplayNameInputAsync(userId, chatId, text, ct);         break;
            case UserState.AwaitingStudentUsername:
                await HandleStudentUsernameInputAsync(userId, chatId, text, ct);     break;
            case UserState.AwaitingWordsForStudent:
                await HandleWordsForStudentAsync(userId, chatId, text, state, ct);   break;
            case UserState.AwaitingPersonalWords:
                await HandlePersonalWordsAsync(userId, chatId, text, ct);            break;
            case UserState.AwaitingTopicName:
                await HandleTopicNameInputAsync(userId, chatId, text, state, ct);    break;
            case UserState.AwaitingQuizCustomAmount:
                await HandleQuizCustomAmountAsync(userId, chatId, text, ct);         break;
            case UserState.AwaitingSearchQuery:
                await HandleSearchQueryAsync(userId, chatId, text, ct);              break;
            case UserState.AwaitingGenTopic:
                await HandleGenTopicInputAsync(userId, chatId, text, ct);            break;
            default:
                var user = await _db.GetUserAsync(userId);
                if (user is not null) await SendMainMenuAsync(chatId, user.Role, ct);
                else                  await SendRoleSelectionAsync(chatId, ct);
                break;
        }
    }

    // ── Callback handler ──────────────────────────────────────────────────────

    private async Task HandleCallbackQueryAsync(CallbackQuery cbq, CancellationToken ct)
    {
        if (cbq.Message is null || cbq.From is null || cbq.Data is null) return;

        var userId = cbq.From.Id;
        var chatId = cbq.Message.Chat.Id;
        var data   = cbq.Data;

        await _bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ct);

        // ── Role selection ───────────────────────────────────────────────────
        if      (data == "role_teacher") await RegisterUserAsync(userId, cbq.From, "Teacher", chatId, ct);
        else if (data == "role_student") await RegisterStudentAsync(userId, cbq.From, chatId, ct);

        // ── Teacher menu ─────────────────────────────────────────────────────
        else if (data == "menu_add_student")
        {
            SetState(userId, new ConversationState { State = UserState.AwaitingStudentUsername });
            await _bot.SendMessage(chatId,
                "👤 Enter your student's Telegram username (with or without @):",
                replyMarkup: BackKeyboard("back_from_add_student"), cancellationToken: ct);
        }
        else if (data == "menu_send_words")    await ShowStudentSelectionAsync(userId, chatId, "send", ct);
        else if (data == "menu_my_students")   await ShowMyStudentsAsync(userId, chatId, ct);
        else if (data == "menu_words_sent")    await ShowStudentSelectionAsync(userId, chatId, "words_sent", ct);
        else if (data == "menu_remove_student") await ShowStudentSelectionAsync(userId, chatId, "remove", ct);

        // ── Student menu ─────────────────────────────────────────────────────
        else if (data == "menu_add_words")
        {
            SetState(userId, new ConversationState { State = UserState.AwaitingPersonalWords });
            await _bot.SendMessage(chatId,
                "📝 Send the words or phrases you want to save (one per line):",
                replyMarkup: BackKeyboard("back_to_menu"), cancellationToken: ct);
        }
        else if (data == "menu_my_words") await ShowMyWordsAsync(userId, chatId, ct);

        // ── Search ───────────────────────────────────────────────────────────
        else if (data == "menu_search")   await HandleMenuSearchAsync(userId, chatId, ct);
        else if (data.StartsWith("search_for_")) await HandleSearchForStudentAsync(userId, chatId, data, ct);

        // ── Shared: set display name ─────────────────────────────────────────
        else if (data == "menu_set_name")
        {
            var user = await _db.GetUserAsync(userId);
            SetState(userId, new ConversationState { State = UserState.AwaitingDisplayName });
            await _bot.SendMessage(chatId,
                $"✏️ Enter your display name (current: *{EscapeMd(user?.DisplayName ?? "-")}*):",
                parseMode: ParseMode.Markdown,
                replyMarkup: BackKeyboard("back_to_menu"), cancellationToken: ct);
        }

        // ── Student selected → choose send mode ──────────────────────────────
        else if (data.StartsWith("send_to_"))
        {
            var studentId = long.Parse(data["send_to_".Length..]);
            var student   = await _db.GetUserAsync(studentId);
            SetState(userId, new ConversationState { SelectedStudentId = studentId });
            await _bot.SendMessage(chatId,
                $"📤 Sending vocabulary to *{EscapeMd(student?.DisplayName ?? studentId.ToString())}*\n\nHow would you like to add words?",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("✍️ Type Words",         "type_words") },
                    new[] { InlineKeyboardButton.WithCallbackData("🎯 Assign from Pool",  "pool_start") },
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",               "back_from_send_words") }
                }),
                cancellationToken: ct);
        }

        // ── Type words (manual) ───────────────────────────────────────────────
        else if (data == "type_words")
        {
            var state   = GetState(userId);
            var student = await _db.GetUserAsync(state.SelectedStudentId!.Value);
            SetState(userId, new ConversationState
            {
                State             = UserState.AwaitingWordsForStudent,
                SelectedStudentId = state.SelectedStudentId
            });
            await _bot.SendMessage(chatId,
                $"✏️ Send vocabulary for *{EscapeMd(student?.DisplayName ?? state.SelectedStudentId.ToString()!)}* (one word/phrase per line):",
                parseMode: ParseMode.Markdown,
                replyMarkup: BackKeyboard("back_from_send_words"), cancellationToken: ct);
        }

        // ── Assign from Pool: level selection ────────────────────────────────
        else if (data == "pool_start")
        {
            var state = GetState(userId);
            if (state.SelectedStudentId is null) { await GoMenu(userId, chatId, ct); return; }
            await SendPoolLevelSelectionAsync(chatId, ct);
        }

        // ── Assign from Pool: level chosen → count selection ─────────────────
        else if (data.StartsWith("pool_level_"))
        {
            var level = data["pool_level_".Length..]; // e.g. "B2" or "any"
            var state = GetState(userId);
            state.PoolLevel = level == "any" ? null : level;
            SetState(userId, state);
            await SendPoolCountSelectionAsync(chatId, ct);
        }

        // ── Assign from Pool: count chosen → fetch & preview ─────────────────
        else if (data.StartsWith("pool_count_"))
        {
            var count = int.Parse(data["pool_count_".Length..]);
            var state = GetState(userId);
            state.PoolCount = count;
            SetState(userId, state);
            await FetchAndShowPoolPreviewAsync(userId, chatId, ct);
        }

        // ── Assign from Pool: shuffle → new random batch ──────────────────────
        else if (data == "pool_shuffle")
            await FetchAndShowPoolPreviewAsync(userId, chatId, ct);

        // ── Assign from Pool: confirm → save & notify ────────────────────────
        else if (data == "pool_confirm")
        {
            var state = GetState(userId);
            if (state.PoolPreview.Count == 0 || state.SelectedStudentId is null)
            { await GoMenu(userId, chatId, ct); return; }

            var batchId     = Guid.NewGuid();
            var wordsToSave = state.PoolPreview.Select(w => new VocabifyBot.Models.Word
            {
                OriginalWord  = w.OriginalWord,
                Translation   = w.Translation,
                EnglishLevel  = w.EnglishLevel,
                Topic         = w.Topic,
                BatchId       = batchId,
                AddedByUserId = userId,
                ForStudentId  = state.SelectedStudentId.Value
            });
            await _db.SaveWordsAsync(wordsToSave);

            var teacher = await _db.GetUserAsync(userId);
            var student = await _db.GetUserAsync(state.SelectedStudentId.Value);
            var levelLabel = state.PoolLevel is not null ? $" [{state.PoolLevel}]" : "";
            await _bot.SendMessage(chatId,
                $"✅ {state.PoolPreview.Count} words assigned to *{EscapeMd(student?.DisplayName ?? "")}*!",
                parseMode: ParseMode.Markdown, cancellationToken: ct);

            try
            {
                var body = string.Join("\n\n", state.PoolPreview.Select(w =>
                {
                    var lvl = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : "";
                    return $"{lvl}{w.Translation}";
                }));
                await _bot.SendMessage(state.SelectedStudentId.Value,
                    $"📚 New vocabulary from {teacher?.DisplayName ?? "your teacher"}{levelLabel}:\n\n{body}",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            catch { }

            ResetState(userId);
            await SendMainMenuAsync(chatId, "Teacher", ct);
        }

        // ── Assign from Pool: cancel ──────────────────────────────────────────
        else if (data == "pool_cancel")
        {
            ResetState(userId);
            await SendMainMenuAsync(chatId, "Teacher", ct);
        }

        // ── Generate by Level: start → level selection ────────────────────────
        else if (data == "gen_start")
        {
            var state = GetState(userId);
            if (state.SelectedStudentId is null) { await GoMenu(userId, chatId, ct); return; }
            MutateState(userId, s => { s.GenLevel = null; s.GenTopic = null; s.GenPreview = new(); });
            await _bot.SendMessage(chatId,
                "🤖 *Generate by Level*\n\nSelect the CEFR level for the new words:",
                parseMode: ParseMode.Markdown,
                replyMarkup: BuildCefrKeyboard("gen_level_", "gen_cancel"),
                cancellationToken: ct);
        }

        // ── Generate by Level: level chosen → count selection ─────────────────
        else if (data.StartsWith("gen_level_"))
        {
            var level = data["gen_level_".Length..];
            MutateState(userId, s => s.GenLevel = level);
            await _bot.SendMessage(chatId,
                $"📦 How many *{level}* words would you like to generate?",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("5",  "gen_count_5"),
                        InlineKeyboardButton.WithCallbackData("10", "gen_count_10"),
                        InlineKeyboardButton.WithCallbackData("20", "gen_count_20"),
                        InlineKeyboardButton.WithCallbackData("30", "gen_count_30")
                    },
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "gen_start") }
                }),
                cancellationToken: ct);
        }

        // ── Generate by Level: count chosen → topic prompt ────────────────────
        else if (data.StartsWith("gen_count_"))
        {
            MutateState(userId, s => s.GenCount = int.Parse(data["gen_count_".Length..]));
            var state = GetState(userId);
            SetState(userId, new ConversationState
            {
                State             = UserState.AwaitingGenTopic,
                SelectedStudentId = state.SelectedStudentId,
                GenLevel          = state.GenLevel,
                GenCount          = state.GenCount
            });
            await _bot.SendMessage(chatId,
                $"🏷️ Enter a topic for the *{state.GenLevel}* words _(optional)_:\n\n_e.g. Business English, Travel, Phrasal Verbs_",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("⏭ Skip topic", "gen_topic_skip") },
                    new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",       "gen_start") }
                }),
                cancellationToken: ct);
        }

        // ── Generate by Level: skip topic → generate ──────────────────────────
        else if (data == "gen_topic_skip")
        {
            MutateState(userId, s => { s.GenTopic = null; s.State = UserState.None; });
            await GenerateAndShowPreviewAsync(userId, chatId, ct);
        }

        // ── Generate by Level: regenerate (same params) ───────────────────────
        else if (data == "gen_retry")
            await GenerateAndShowPreviewAsync(userId, chatId, ct);

        // ── Generate by Level: confirm → save ────────────────────────────────
        else if (data == "gen_confirm")
            await ConfirmGenPreviewAsync(userId, chatId, ct);

        // ── Generate by Level: cancel ─────────────────────────────────────────
        else if (data == "gen_cancel")
        {
            ResetState(userId);
            await SendMainMenuAsync(chatId, "Teacher", ct);
        }

        // ── Student selected → view words (step 1: filter) ───────────────────
        else if (data.StartsWith("words_sent_to_"))
        {
            var studentId = long.Parse(data["words_sent_to_".Length..]);
            var s         = GetState(userId);
            s.BrowsingStudentId = studentId;
            SetState(userId, s);
            var student = await _db.GetUserAsync(studentId);
            await SendWordFilterSelectionAsync(chatId, student?.DisplayName ?? studentId.ToString(), ct);
        }

        // ── Step 2: filter chosen → pick display mode ─────────────────────────
        else if (data.StartsWith("wfilter_"))
        {
            var filter  = data["wfilter_".Length..]; // "teacher" | "student" | "both"
            var s       = GetState(userId);
            s.BrowsingFilter = filter;
            SetState(userId, s);
            await SendWordModeSelectionAsync(chatId, ct);
        }

        // ── Step 3: mode chosen → load & display ─────────────────────────────
        else if (data.StartsWith("wmode_"))
        {
            var mode  = data["wmode_".Length..]; // "topic" | "chunks" | "messages" | "all"
            var s     = GetState(userId);
            if (s.BrowsingStudentId is null) { await GoMenu(userId, chatId, ct); return; }

            var words = await _db.GetWordsForBrowsingAsync(userId, s.BrowsingStudentId.Value, s.BrowsingFilter ?? "both");
            if (words.Count == 0)
            {
                await _bot.SendMessage(chatId, "📭 No words found for the selected filter.", cancellationToken: ct);
                await SendMainMenuAsync(chatId, "Teacher", ct);
                return;
            }

            s.BrowsingMode     = mode;
            s.BrowsingWords    = words;
            s.BrowsingOffset   = 0;
            s.BrowsingGroupIdx = 0;
            s.BrowsingGroups   = mode == "topic"    ? GroupByTopic(words)   :
                                  mode == "messages" ? GroupByBatch(words)   :
                                  new List<List<VocabifyBot.Models.Word>>();
            SetState(userId, s);
            await SendBrowsingPageAsync(userId, chatId, ct);
        }

        // ── Navigation ────────────────────────────────────────────────────────
        else if (data == "browse_next")
        {
            var s = GetState(userId);
            if (s.BrowsingMode == "chunks")        s.BrowsingOffset   += ChunkSize;
            else                                    s.BrowsingGroupIdx += 1;
            SetState(userId, s);
            await SendBrowsingPageAsync(userId, chatId, ct);
        }
        else if (data == "browse_cancel")
        {
            var s = GetState(userId);
            s.BrowsingStudentId = null; s.BrowsingFilter = null; s.BrowsingMode = null;
            s.BrowsingWords = new(); s.BrowsingGroups = new(); s.BrowsingOffset = 0; s.BrowsingGroupIdx = 0;
            SetState(userId, s);
            await SendMainMenuAsync(chatId, "Teacher", ct);
        }

        // ── Student selected → remove ─────────────────────────────────────────
        else if (data.StartsWith("remove_student_"))
        {
            var studentId = long.Parse(data["remove_student_".Length..]);
            var student   = await _db.GetUserAsync(studentId);
            await _bot.SendMessage(chatId,
                $"⚠️ Remove *{EscapeMd(student?.DisplayName ?? studentId.ToString())}* from your student list?",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✅ Remove",  $"confirm_remove_{studentId}"),
                        InlineKeyboardButton.WithCallbackData("❌ Cancel",        "back_to_menu")
                    }
                }),
                cancellationToken: ct);
        }
        else if (data.StartsWith("confirm_remove_"))
        {
            var studentId = long.Parse(data["confirm_remove_".Length..]);
            var student   = await _db.GetUserAsync(studentId);
            await _db.UnlinkTeacherStudentAsync(userId, studentId);
            await _bot.SendMessage(chatId,
                $"✅ *{EscapeMd(student?.DisplayName ?? studentId.ToString())}* has been removed from your student list.",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            await SendMainMenuAsync(chatId, "Teacher", ct);
        }

        // ── Topic callbacks ───────────────────────────────────────────────────
        else if (data == "topic_auto")
        {
            var state = GetState(userId);
            if (state.PendingWords.Count == 0) { await GoMenu(userId, chatId, ct); return; }
            var notice = await _bot.SendMessage(chatId, "🤖 Detecting topic…", cancellationToken: ct);
            var allWords = string.Join("\n", state.PendingWords.Select(w => w.Original));
            var topic = await _openAi.DetectTopicAsync(allWords);
            await _bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct);
            await FinalizeWordsAsync(userId, chatId, topic, ct);
        }
        else if (data == "topic_specify")
        {
            var state = GetState(userId);
            SetState(userId, new ConversationState
            {
                State                = UserState.AwaitingTopicName,
                PendingWords         = state.PendingWords,
                PendingAddedByUserId = state.PendingAddedByUserId,
                PendingForStudentId  = state.PendingForStudentId,
                PendingTranslationText = state.PendingTranslationText
            });
            await _bot.SendMessage(chatId,
                "🏷️ Enter the topic name (e.g. *Phrasal Verbs*, *Business English*, *Adjectives*):",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        else if (data == "topic_skip") await FinalizeWordsAsync(userId, chatId, null, ct);

        // ── Vocabulary browsing ───────────────────────────────────────────────
        else if (data == "vocab_all")
        {
            var words = await _db.GetWordsForStudentAsync(userId);
            await SendWordListAsync(chatId, words, "📚 All vocabulary:", ct);
        }
        else if (data == "vocab_level")
        {
            string[] cefrLevels = ["A0", "A1", "A2", "B1", "B2", "C1", "C2"];
            await _bot.SendMessage(chatId, "🔤 Select a CEFR level:",
                replyMarkup: new InlineKeyboardMarkup(
                    cefrLevels
                        .Select(l => InlineKeyboardButton.WithCallbackData(l, $"vocab_lvl_{l}"))
                        .ToArray()
                        .Chunk(4)
                        .Select(row => row)
                        .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "menu_my_words") })
                        .ToArray()),
                cancellationToken: ct);
        }
        else if (data.StartsWith("vocab_lvl_"))
        {
            var level = data["vocab_lvl_".Length..];
            var words = await _db.GetWordsByLevelAsync(userId, level);
            await SendWordListAsync(chatId, words, $"🔤 *{level}* words:", ct);
        }
        else if (data.StartsWith("vocab_t_"))
        {
            var state = GetState(userId);
            if (int.TryParse(data["vocab_t_".Length..], out var idx) && idx < state.CachedTopics.Count)
            {
                var topic = state.CachedTopics[idx];
                var words = await _db.GetWordsByTopicAsync(userId, topic);
                await SendWordListAsync(chatId, words, $"📚 {topic}:", ct);
            }
        }

        // ── Quiz setup ────────────────────────────────────────────────────────
        else if (data == "menu_quiz")
        {
            MutateState(userId, s => s.IsMistakesQuiz = false);
            await SendQuizDirectionAsync(chatId, ct);
        }

        else if (data == "menu_mistakes")
        {
            MutateState(userId, s => s.IsMistakesQuiz = true);
            await _bot.SendMessage(chatId,
                "💪 *Work on Mistakes*\n\nI'll select your most-missed words. Choose direction:",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Eng → Ukr", "quiz_dir_eu"),
                        InlineKeyboardButton.WithCallbackData("Ukr → Eng", "quiz_dir_ue")
                    },
                    new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "quiz_cancel") }
                }),
                cancellationToken: ct);
        }

        else if (data.StartsWith("quiz_dir_"))
        {
            var dir   = data["quiz_dir_".Length..]; // "eu" | "ue"
            var state = GetState(userId);
            state.QuizDirection = dir;
            SetState(userId, state);
            await SendQuizAmountAsync(chatId, ct);
        }
        else if (data.StartsWith("quiz_amt_"))
        {
            var amt   = int.Parse(data["quiz_amt_".Length..]);
            var state = GetState(userId);
            state.QuizAmount = amt;
            SetState(userId, state);
            if (state.IsMistakesQuiz)
                await StartMistakesQuizAsync(userId, chatId, ct);
            else
                await SendQuizLevelAsync(chatId, ct);
        }
        else if (data == "quiz_amt_custom")
        {
            var state = GetState(userId);
            state.State = UserState.AwaitingQuizCustomAmount;
            SetState(userId, state);
            await _bot.SendMessage(chatId, "✏️ Enter the number of words (2–100):",
                replyMarkup: BackKeyboard(state.IsMistakesQuiz ? "menu_mistakes" : "menu_quiz"), cancellationToken: ct);
        }
        else if (data.StartsWith("quiz_lvl_"))
        {
            var lvl   = data["quiz_lvl_".Length..]; // "A1" etc or "any"
            var state = GetState(userId);
            state.QuizLevel = lvl == "any" ? null : lvl;
            SetState(userId, state);
            var topics = await _db.GetTopicsForStudentAsync(userId);
            await SendQuizTopicAsync(chatId, topics, ct);
        }
        else if (data.StartsWith("quiz_top_"))
        {
            var raw   = data["quiz_top_".Length..]; // index or "any"
            var state = GetState(userId);
            if (raw == "any")
            {
                state.QuizTopic = null;
            }
            else
            {
                var topics = await _db.GetTopicsForStudentAsync(userId);
                if (int.TryParse(raw, out var idx) && idx < topics.Count)
                    state.QuizTopic = topics[idx];
            }
            SetState(userId, state);
            await StartQuizAsync(userId, chatId, ct);
        }

        // ── Quiz active ───────────────────────────────────────────────────────
        else if (data.StartsWith("quiz_ans_"))
        {
            // quiz_ans_{qIdx}_{selectedWordId}
            var parts       = data["quiz_ans_".Length..].Split('_');
            var qIdx        = int.Parse(parts[0]);
            var selectedId  = int.Parse(parts[1]);
            await HandleQuizAnswerAsync(userId, chatId, qIdx, selectedId, ct);
        }
        else if (data.StartsWith("quiz_next_"))
        {
            var nextIdx = int.Parse(data["quiz_next_".Length..]);
            var state   = GetState(userId);
            state.QuizIndex = nextIdx;
            SetState(userId, state);
            await SendQuizQuestionAsync(userId, chatId, ct);
        }
        else if (data == "quiz_cancel")
        {
            ResetState(userId);
            await GoMenu(userId, chatId, ct);
        }

        // ── Back navigation ───────────────────────────────────────────────────
        else if (data == "back_from_send_words")
        {
            ResetState(userId);
            await ShowStudentSelectionAsync(userId, chatId, "send", ct);
        }
        else if (data == "back_from_add_student")
        {
            ResetState(userId);
            await SendMainMenuAsync(chatId, "Teacher", ct);
        }
        else if (data == "back_to_menu") await GoMenu(userId, chatId, ct);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    private async Task RegisterUserAsync(long userId, Telegram.Bot.Types.User from, string role, long chatId, CancellationToken ct)
    {
        await _db.UpsertUserAsync(new VocabifyBot.Models.User
        {
            TelegramId = userId,
            Username   = from.Username ?? "",
            FirstName  = from.FirstName,
            Role       = role
        });
        ResetState(userId);
        await SendMainMenuAsync(chatId, role, ct);
    }

    private async Task RegisterStudentAsync(long userId, Telegram.Bot.Types.User from, long chatId, CancellationToken ct)
    {
        await _db.UpsertUserAsync(new VocabifyBot.Models.User
        {
            TelegramId = userId,
            Username   = from.Username ?? "",
            FirstName  = from.FirstName,
            Role       = "Student"
        });

        // Students can only be added by a teacher — go straight to menu
        ResetState(userId);
        await SendMainMenuAsync(chatId, "Student", ct);
    }

    // ── Text input handlers ───────────────────────────────────────────────────

    private async Task HandleDisplayNameInputAsync(long userId, long chatId, string name, CancellationToken ct)
    {
        if (name.Length > 60)
        {
            await _bot.SendMessage(chatId, "❌ Max 60 characters. Please try again:", cancellationToken: ct);
            return;
        }
        await _db.UpdateDisplayNameAsync(userId, name);
        ResetState(userId);
        await _bot.SendMessage(chatId, $"✅ Display name set to *{EscapeMd(name)}*!",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
        var user = await _db.GetUserAsync(userId);
        await SendMainMenuAsync(chatId, user!.Role, ct);
    }

    private async Task HandleStudentUsernameInputAsync(long teacherId, long chatId, string input, CancellationToken ct)
    {
        var student = await _db.GetUserByUsernameAsync(input);
        if (student is null || student.Role != "Student")
        {
            await _bot.SendMessage(chatId,
                $"❌ Student @{input.TrimStart('@')} not found. Make sure they've started the bot and registered as a student.\n\nPlease try again:",
                cancellationToken: ct);
            return;
        }
        await _db.LinkTeacherStudentAsync(teacherId, student.TelegramId);
        ResetState(teacherId);
        await _bot.SendMessage(chatId, $"✅ *{EscapeMd(student.DisplayName)}* added to your list!",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
        await SendMainMenuAsync(chatId, "Teacher", ct);
    }

    private async Task HandleWordsForStudentAsync(long teacherId, long chatId, string text,
        ConversationState state, CancellationToken ct)
    {
        if (state.SelectedStudentId is null) { ResetState(teacherId); return; }

        var notice      = await _bot.SendMessage(chatId, "⏳ Translating with AI…", cancellationToken: ct);
        var translation = await _openAi.TranslateWordsAsync(text);
        await _bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct);

        SetState(teacherId, new ConversationState
        {
            State                  = UserState.AwaitingTopicChoice,
            PendingWords           = BuildPendingWords(text, translation),
            PendingAddedByUserId   = teacherId,
            PendingForStudentId    = state.SelectedStudentId,
            PendingTranslationText = translation
        });

        await _bot.SendMessage(chatId, translation, cancellationToken: ct);
        await SendTopicChoiceAsync(chatId, ct);
    }

    private async Task HandlePersonalWordsAsync(long studentId, long chatId, string text, CancellationToken ct)
    {
        var notice      = await _bot.SendMessage(chatId, "⏳ Translating with AI…", cancellationToken: ct);
        var translation = await _openAi.TranslateWordsAsync(text);
        await _bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct);

        SetState(studentId, new ConversationState
        {
            State                  = UserState.AwaitingTopicChoice,
            PendingWords           = BuildPendingWords(text, translation),
            PendingAddedByUserId   = studentId,
            PendingForStudentId    = studentId,
            PendingTranslationText = translation
        });

        await _bot.SendMessage(chatId, translation, cancellationToken: ct);
        await SendTopicChoiceAsync(chatId, ct);
    }

    private async Task HandleTopicNameInputAsync(long userId, long chatId, string name,
        ConversationState state, CancellationToken ct)
    {
        if (name.Length > 60)
        {
            await _bot.SendMessage(chatId, "❌ Max 60 characters. Please try again:", cancellationToken: ct);
            return;
        }
        await FinalizeWordsAsync(userId, chatId, name, ct);
    }

    // ── Word finalization ─────────────────────────────────────────────────────

    private async Task FinalizeWordsAsync(long userId, long chatId, string? topic, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.PendingWords.Count == 0) { await GoMenu(userId, chatId, ct); return; }

        var batchId = Guid.NewGuid();
        var wordsToSave = state.PendingWords.Select(w => new VocabifyBot.Models.Word
        {
            OriginalWord  = w.Original,
            Translation   = w.Translation,
            Topic         = topic,
            EnglishLevel  = w.EnglishLevel,
            BatchId       = batchId,
            AddedByUserId = state.PendingAddedByUserId!.Value,
            ForStudentId  = state.PendingForStudentId!.Value
        });
        await _db.SaveWordsAsync(wordsToSave);

        var savedMsg = topic is not null
            ? $"✅ Saved! Topic: *{EscapeMd(topic)}*"
            : "✅ Saved without topic.";
        await _bot.SendMessage(chatId, savedMsg, parseMode: ParseMode.Markdown, cancellationToken: ct);

        // Forward to student if teacher sent the words
        if (state.PendingAddedByUserId != state.PendingForStudentId)
        {
            var teacher  = await _db.GetUserAsync(state.PendingAddedByUserId!.Value);
            var topicLine = topic is not null ? $"🏷️ Topic: {topic}\n\n" : "";
            try
            {
                await _bot.SendMessage(
                    state.PendingForStudentId!.Value,
                    $"📚 New vocabulary from {teacher?.DisplayName ?? "your teacher"}:\n\n{topicLine}{state.PendingTranslationText}",
                    cancellationToken: ct);
            }
            catch { }
        }

        ResetState(userId);
        await GoMenu(userId, chatId, ct);
    }

    // ── Pool helpers ──────────────────────────────────────────────────────────

    private static readonly string[] CefrLevels = ["A0", "A1", "A2", "B1", "B2", "C1", "C2"];

    private async Task SendPoolLevelSelectionAsync(long chatId, CancellationToken ct)
    {
        var levelButtons = CefrLevels
            .Select(l => InlineKeyboardButton.WithCallbackData(l, $"pool_level_{l}"))
            .ToArray();

        await _bot.SendMessage(chatId,
            "🎯 *Assign from Pool*\n\nSelect a CEFR level to filter words:",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                levelButtons[..4],   // A0 A1 A2 B1
                levelButtons[4..],   // B2 C1 C2
                [InlineKeyboardButton.WithCallbackData("🔀 Any level", "pool_level_any")],
                [InlineKeyboardButton.WithCallbackData("⬅️ Back",      "pool_cancel")]
            }),
            cancellationToken: ct);
    }

    private async Task SendPoolCountSelectionAsync(long chatId, CancellationToken ct)
    {
        await _bot.SendMessage(chatId,
            "📦 How many words would you like to assign?",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("5",  "pool_count_5"),
                    InlineKeyboardButton.WithCallbackData("10", "pool_count_10"),
                    InlineKeyboardButton.WithCallbackData("20", "pool_count_20"),
                    InlineKeyboardButton.WithCallbackData("30", "pool_count_30"),
                },
                [InlineKeyboardButton.WithCallbackData("⬅️ Back", "pool_start")]
            }),
            cancellationToken: ct);
    }

    private async Task FetchAndShowPoolPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null) { await GoMenu(userId, chatId, ct); return; }

        var words = await _db.GetPoolWordsAsync(userId, state.SelectedStudentId.Value,
                                                state.PoolLevel, state.PoolCount);
        if (words.Count == 0)
        {
            var levelNote = state.PoolLevel is not null ? $" at *{state.PoolLevel}*" : "";
            await _bot.SendMessage(chatId,
                $"📭 No new words found in your pool{levelNote} for this student.\n\nTry a different level or count.",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                   new[] { InlineKeyboardButton.WithCallbackData("🔙 Change level", "pool_start") },
                   new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",        "pool_cancel") }
                }),
                cancellationToken: ct);
            return;
        }

        state.PoolPreview = words;
        SetState(userId, state);

        var student   = await _db.GetUserAsync(state.SelectedStudentId.Value);
        var levelLabel = state.PoolLevel is not null ? $" *[{state.PoolLevel}]*" : "";
        var preview    = string.Join("\n\n", words.Select(w =>
        {
            var lvl = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : "";
            return $"{lvl}{w.Translation}";
        }));

        await _bot.SendMessage(chatId,
            $"🎯 Preview — {words.Count} words{levelLabel} for *{EscapeMd(student?.DisplayName ?? "")}*:\n\n{preview}",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Confirm", "pool_confirm"),
                    InlineKeyboardButton.WithCallbackData("🔄 Shuffle", "pool_shuffle"),
                    InlineKeyboardButton.WithCallbackData("❌ Cancel",  "pool_cancel")
                }
            }),
            cancellationToken: ct);
    }

    // ── Quiz helpers ──────────────────────────────────────────────────────────

    private static readonly System.Text.RegularExpressions.Regex StripAnnotations =
        new(@"\[.*?\]|\(.*?\)", System.Text.RegularExpressions.RegexOptions.Compiled |
                                System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>Extracts a short Ukrainian label from a full Translation string.</summary>
    private static string ShortUkr(string translation)
    {
        var t = translation;
        // Take the part after '—' (the Ukrainian side)
        var dash = t.IndexOf('—');
        if (dash >= 0) t = t[(dash + 1)..];
        t = StripAnnotations.Replace(t, "").Trim().TrimEnd(';', ',', ' ');
        if (t.Length > 48) t = t[..48].TrimEnd(',', ';', ' ') + "…";
        return t;
    }

    /// <summary>Returns the correct "question" text depending on quiz direction.</summary>
    private static string QuizQuestion(VocabifyBot.Models.Word word, string direction) =>
        direction == "eu" ? word.OriginalWord : ShortUkr(word.Translation);

    /// <summary>Returns the correct short "answer" text depending on quiz direction.</summary>
    private static string QuizAnswer(VocabifyBot.Models.Word word, string direction) =>
        direction == "eu" ? ShortUkr(word.Translation) : word.OriginalWord;

    private async Task SendQuizDirectionAsync(long chatId, CancellationToken ct) =>
        await _bot.SendMessage(chatId,
            "🧩 *Quiz*\n\nChoose quiz direction:",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🇬🇧→🇺🇦 Eng→Ukr", "quiz_dir_eu"),
                    InlineKeyboardButton.WithCallbackData("🇺🇦→🇬🇧 Ukr→Eng", "quiz_dir_ue")
                },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "quiz_cancel") }
            }),
            cancellationToken: ct);

    private async Task SendQuizAmountAsync(long chatId, CancellationToken ct) =>
        await _bot.SendMessage(chatId,
            "📦 How many words in this session?",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("5",       "quiz_amt_5"),
                    InlineKeyboardButton.WithCallbackData("10",      "quiz_amt_10"),
                    InlineKeyboardButton.WithCallbackData("20",      "quiz_amt_20"),
                    InlineKeyboardButton.WithCallbackData("30",      "quiz_amt_30"),
                    InlineKeyboardButton.WithCallbackData("✏️ Custom","quiz_amt_custom")
                },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "quiz_cancel") }
            }),
            cancellationToken: ct);

    private async Task SendQuizLevelAsync(long chatId, CancellationToken ct)
    {
        var btns = CefrLevels
            .Select(l => InlineKeyboardButton.WithCallbackData(l, $"quiz_lvl_{l}"))
            .ToArray();
        await _bot.SendMessage(chatId,
            "🎯 Filter by CEFR level? _(optional)_",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                btns[..4],
                btns[4..],
                new[] { InlineKeyboardButton.WithCallbackData("🔀 Any level", "quiz_lvl_any") },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",     "quiz_cancel")  }
            }),
            cancellationToken: ct);
    }

    private async Task SendQuizTopicAsync(long chatId, List<string> topics, CancellationToken ct)
    {
        var rows = topics
            .Select((t, i) => new[] { InlineKeyboardButton.WithCallbackData($"🏷️ {t}", $"quiz_top_{i}") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("🔀 Any topic", "quiz_top_any") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",    "quiz_cancel")  })
            .ToArray();

        await _bot.SendMessage(chatId,
            "🏷️ Filter by topic? _(optional)_",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: ct);
    }

    private async Task HandleQuizCustomAmountAsync(long userId, long chatId, string text, CancellationToken ct)
    {
        if (!int.TryParse(text.Trim(), out var n) || n < 2 || n > 100)
        {
            await _bot.SendMessage(chatId, "❌ Please enter a number between 2 and 100:", cancellationToken: ct);
            return;
        }
        var state = GetState(userId);
        state.State      = UserState.None;
        state.QuizAmount = n;
        SetState(userId, state);

        if (state.IsMistakesQuiz)
            await StartMistakesQuizAsync(userId, chatId, ct);
        else
            await SendQuizLevelAsync(chatId, ct);
    }

    private async Task StartQuizAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var words = await _db.GetWordsForQuizAsync(userId, state.QuizLevel, state.QuizTopic, state.QuizAmount);

        if (words.Count < 2)
        {
            await _bot.SendMessage(chatId,
                "📭 Not enough words match the selected filters (need at least 2). " +
                "Try different level/topic or add more vocabulary first.",
                cancellationToken: ct);
            await GoMenu(userId, chatId, ct);
            return;
        }

        state.QuizWords  = words;
        state.QuizIndex  = 0;
        state.QuizScore  = 0;
        SetState(userId, state);

        var dir  = state.QuizDirection == "eu" ? "Eng → Ukr" : "Ukr → Eng";
        var lvl  = state.QuizLevel  is not null ? $" | Level: {state.QuizLevel}"  : "";
        var top  = state.QuizTopic  is not null ? $" | Topic: {state.QuizTopic}"  : "";
        await _bot.SendMessage(chatId,
            $"🧩 Quiz starting!\n*{dir}* | {words.Count} words{lvl}{top}\n\nGood luck! 🍀",
            parseMode: ParseMode.Markdown, cancellationToken: ct);

        await SendQuizQuestionAsync(userId, chatId, ct);
    }

    private async Task SendQuizQuestionAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var idx   = state.QuizIndex;
        var words = state.QuizWords;

        if (idx >= words.Count)
        {
            await SendQuizResultsAsync(userId, chatId, ct);
            return;
        }

        var correct   = words[idx];
        var direction = state.QuizDirection ?? "eu";
        var question  = QuizQuestion(correct, direction);

        // Pick 3 wrong options from other quiz words, shuffled
        var rng    = new Random();
        var wrongs = words.Where(w => w.Id != correct.Id)
                          .OrderBy(_ => rng.Next())
                          .Take(3)
                          .ToList();

        // Shuffle all 4 options
        var options = wrongs.Append(correct).OrderBy(_ => rng.Next()).ToList();

        var answerButtons = options
            .Select(w => InlineKeyboardButton.WithCallbackData(
                Truncate(QuizAnswer(w, direction), 32),
                $"quiz_ans_{idx}_{w.Id}"))
            .ToArray();

        var levelTag = correct.EnglishLevel is not null ? $" *[{correct.EnglishLevel}]*" : "";
        var header   = $"🧩 *Question {idx + 1}/{words.Count}*{levelTag}\n\n";

        var msg = await _bot.SendMessage(chatId,
            $"{header}*{EscapeMd(question)}*\n\nChoose the correct translation:",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                answerButtons[..2],
                answerButtons[2..],
                new[] { InlineKeyboardButton.WithCallbackData("🚫 Stop Quiz", "quiz_cancel") }
            }),
            cancellationToken: ct);

        state.QuizMessageId = msg.MessageId;
        SetState(userId, state);
    }

    private async Task HandleQuizAnswerAsync(long userId, long chatId, int qIdx, int selectedId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (qIdx >= state.QuizWords.Count) return;

        var correct   = state.QuizWords[qIdx];
        var direction = state.QuizDirection ?? "eu";
        var isCorrect = selectedId == correct.Id;

        if (isCorrect) state.QuizScore++;
        SetState(userId, state);

        // Record result — in Mistakes mode also halve the wrong count on correct answers
        _ = _db.RecordQuizAnswerAsync(userId, correct.Id, isCorrect);
        if (isCorrect && state.IsMistakesQuiz)
            _ = _db.ReduceWrongCountAsync(userId, correct.Id);

        var correctLabel = Truncate(QuizAnswer(correct, direction), 60);
        var emoji = isCorrect ? "✅" : "❌";
        var feedback = isCorrect
            ? $"{emoji} *Correct!*"
            : $"{emoji} Wrong! The answer was:\n_{EscapeMd(correctLabel)}_";

        var nextIdx  = qIdx + 1;
        var isLast   = nextIdx >= state.QuizWords.Count;
        var navBtn   = isLast
            ? InlineKeyboardButton.WithCallbackData("🏁 See Results", $"quiz_next_{nextIdx}")
            : InlineKeyboardButton.WithCallbackData("▶️ Next",         $"quiz_next_{nextIdx}");

        try
        {
            await _bot.EditMessageText(chatId, state.QuizMessageId,
                $"🧩 *Question {qIdx + 1}/{state.QuizWords.Count}*\n\n{feedback}",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[] { new[] { navBtn } }),
                cancellationToken: ct);
        }
        catch
        {
            // If edit fails, just send a new message
            await _bot.SendMessage(chatId, feedback, parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[] { new[] { navBtn } }),
                cancellationToken: ct);
        }
    }

    private async Task SendQuizResultsAsync(long userId, long chatId, CancellationToken ct)
    {
        var state       = GetState(userId);
        var total       = state.QuizWords.Count;
        var score       = state.QuizScore;
        var pct         = total > 0 ? (int)Math.Round(score * 100.0 / total) : 0;
        var isMistakes  = state.IsMistakesQuiz;

        var medal = pct switch
        {
            >= 90 => "🥇",
            >= 70 => "🥈",
            >= 50 => "🥉",
            _     => "📚"
        };

        var encouragement = isMistakes
            ? pct switch
            {
                >= 90 => "Fantastic — you've crushed those mistakes! 🎉",
                >= 70 => "Great progress! Keep drilling the tough ones 💪",
                >= 50 => "Good effort! Repeat this session to clear more errors 📖",
                _     => "Don't give up — every attempt reduces your errors! 💡"
            }
            : pct switch
            {
                >= 90 => "Excellent work! 🎉",
                >= 70 => "Good job! Keep it up 💪",
                >= 50 => "Not bad! Practice more 📖",
                _     => "Keep studying — you'll get there! 💡"
            };

        var title = isMistakes ? "💪 *Mistakes Session Complete!*" : "🧩 *Quiz Complete!*";

        ResetState(userId);
        await _bot.SendMessage(chatId,
            $"{medal} {title}\n\nScore: *{score}/{total}* ({pct}%)\n\n{encouragement}",
            parseMode: ParseMode.Markdown, cancellationToken: ct);

        await GoMenu(userId, chatId, ct);
    }

    private async Task StartMistakesQuizAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        var count = state.QuizAmount > 0 ? state.QuizAmount : 10;
        var words = await _db.GetWordsForMistakesAsync(userId, count);

        if (words.Count < 2)
        {
            ResetState(userId);
            await _bot.SendMessage(chatId,
                "🎉 No mistakes to practice! You haven't made enough errors yet, or you've already mastered everything. Try the regular quiz first.",
                cancellationToken: ct);
            await GoMenu(userId, chatId, ct);
            return;
        }

        state.QuizWords      = words;
        state.QuizIndex      = 0;
        state.QuizScore      = 0;
        state.IsMistakesQuiz = true;
        SetState(userId, state);

        var dir = state.QuizDirection == "eu" ? "Eng → Ukr" : "Ukr → Eng";
        await _bot.SendMessage(chatId,
            $"💪 *Work on Mistakes*\n*{dir}* | {words.Count} most-missed words\n\nCorrect answers will cut each word's error count in half. Let's go! 🔥",
            parseMode: ParseMode.Markdown, cancellationToken: ct);

        await SendQuizQuestionAsync(userId, chatId, ct);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd(' ', ',') + "…";

    // ── Display helpers ───────────────────────────────────────────────────────

    private async Task ShowStudentSelectionAsync(long teacherId, long chatId, string mode, CancellationToken ct)
    {
        var students = await _db.GetStudentsForTeacherAsync(teacherId);
        if (students.Count == 0)
        {
            await _bot.SendMessage(chatId, "You have no students yet. Use *Add Student* first.",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        var prefix = mode == "send"   ? "send_to_"        :
                     mode == "remove" ? "remove_student_"  :
                                        "words_sent_to_";
        var title = mode == "send"   ? "👥 Choose a student to send vocabulary to:" :
                    mode == "remove" ? "👥 Choose a student to remove:"              :
                                       "👥 Choose a student to view words sent:";

        var rows = students
            .Select(s => new[] { InlineKeyboardButton.WithCallbackData(s.DisplayName, $"{prefix}{s.TelegramId}") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "back_to_menu") })
            .ToArray();

        await _bot.SendMessage(chatId, title,
            replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task ShowMyStudentsAsync(long teacherId, long chatId, CancellationToken ct)
    {
        var students = await _db.GetStudentsForTeacherAsync(teacherId);
        if (students.Count == 0)
        {
            await _bot.SendMessage(chatId, "You have no students yet.", cancellationToken: ct);
            return;
        }
        var list = string.Join("\n", students.Select((s, i) => $"{i + 1}. {s.DisplayName}"));
        await _bot.SendMessage(chatId, $"👥 Your students:\n\n{list}", cancellationToken: ct);
    }

    private async Task ShowWordsSentToStudentAsync(long teacherId, long chatId, long studentId, CancellationToken ct)
    {
        var student = await _db.GetUserAsync(studentId);
        var words   = await _db.GetWordsSentToStudentAsync(teacherId, studentId);
        if (words.Count == 0)
        {
            await _bot.SendMessage(chatId,
                $"You haven't sent any words to {student?.DisplayName ?? "this student"} yet.",
                cancellationToken: ct);
            return;
        }
        await SendWordListAsync(chatId, words, $"📤 Words sent to {student?.DisplayName ?? "student"}:", ct);
    }

    private async Task ShowMyWordsAsync(long studentId, long chatId, CancellationToken ct)
    {
        var topics = await _db.GetTopicsForStudentAsync(studentId);

        if (topics.Count == 0)
        {
            var words = await _db.GetWordsForStudentAsync(studentId);
            if (words.Count == 0) { await _bot.SendMessage(chatId, "Your vocabulary is empty. 📭", cancellationToken: ct); return; }

            await _bot.SendMessage(chatId, "📚 Browse your vocabulary:",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🔤 By Level",  "vocab_level") },
                    new[] { InlineKeyboardButton.WithCallbackData("📋 All Words", "vocab_all") }
                }),
                cancellationToken: ct);
            return;
        }

        // Cache topics in state for indexed callbacks
        var state = GetState(studentId);
        state.CachedTopics = topics;
        SetState(studentId, state);

        var rows = topics
            .Select((t, i) => new[] { InlineKeyboardButton.WithCallbackData($"🏷️ {t}", $"vocab_t_{i}") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("🔤 By Level",  "vocab_level") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("📋 All Words", "vocab_all") })
            .ToArray();

        await _bot.SendMessage(chatId, "📚 Browse vocabulary by topic:",
            replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    // ── UI builders ───────────────────────────────────────────────────────────

    private async Task SendWordFilterSelectionAsync(long chatId, string studentName, CancellationToken ct) =>
        await _bot.SendMessage(chatId,
            $"🔍 What words do you want to see for *{EscapeMd(studentName)}*?",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("👨‍🏫 By teacher",         "wfilter_teacher") },
                new[] { InlineKeyboardButton.WithCallbackData("🧑 By student",   "wfilter_student") },
                new[] { InlineKeyboardButton.WithCallbackData("📋 Both",                "wfilter_both")    },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",               "back_to_menu")    }
            }),
            cancellationToken: ct);

    private async Task SendWordModeSelectionAsync(long chatId, CancellationToken ct) =>
        await _bot.SendMessage(chatId,
            "📂 How would you like to view the words?",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🏷️ By topic",         "wmode_topic")    },
                new[] { InlineKeyboardButton.WithCallbackData("📦 By chunks",  "wmode_chunks")   },
                new[] { InlineKeyboardButton.WithCallbackData("💬 By message",       "wmode_messages") },
                new[] { InlineKeyboardButton.WithCallbackData("📋 All",       "wmode_all")      },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",             "back_to_menu")   }
            }),
            cancellationToken: ct);

    private async Task SendBrowsingPageAsync(long userId, long chatId, CancellationToken ct)
    {
        var s = GetState(userId);

        string header;
        List<VocabifyBot.Models.Word> page;
        bool hasMore;

        if (s.BrowsingMode == "chunks")
        {
            var offset = s.BrowsingOffset;
            page    = s.BrowsingWords.Skip(offset).Take(ChunkSize).ToList();
            hasMore = offset + ChunkSize < s.BrowsingWords.Count;
            header  = $"📦 Words {offset + 1}–{offset + page.Count} of {s.BrowsingWords.Count}";
        }
        else if (s.BrowsingMode == "all")
        {
            // Send all in multiple messages (no nav buttons)
            await SendAllWordsAsync(chatId, s.BrowsingWords, ct);
            await SendMainMenuAsync(chatId, "Teacher", ct);
            return;
        }
        else
        {
            // topic or messages — group-based
            var groups = s.BrowsingGroups;
            var idx    = s.BrowsingGroupIdx;
            if (idx >= groups.Count)
            {
                await _bot.SendMessage(chatId, "✅ No more groups.", cancellationToken: ct);
                await SendMainMenuAsync(chatId, "Teacher", ct);
                return;
            }
            page    = groups[idx];
            hasMore = idx + 1 < groups.Count;
            header  = s.BrowsingMode == "topic"
                ? $"🏷️ Topic: *{EscapeMd(page[0].Topic ?? "No topic")}* ({page.Count} words) — group {idx + 1}/{groups.Count}"
                : $"💬 Message {idx + 1}/{groups.Count} — {page[0].CreatedAt:dd MMM yyyy HH:mm} ({page.Count} words)";
        }

        var body = string.Join("\n\n", page.Select(w =>
        {
            var levelTag = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : "";
            return $"{levelTag}{w.Translation}";
        }));
        await _bot.SendMessage(chatId, $"{header}\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: BrowseNavKeyboard(hasMore),
            cancellationToken: ct);
    }

    private async Task SendAllWordsAsync(long chatId, List<VocabifyBot.Models.Word> words, CancellationToken ct)
    {
        const int msgChunk = 20;
        for (int i = 0; i < words.Count; i += msgChunk)
        {
            var slice  = words.Skip(i).Take(msgChunk).ToList();
            var header = $"📋 Words {i + 1}–{i + slice.Count} of {words.Count}";
            var body   = string.Join("\n\n", slice.Select(w =>
            {
                var levelTag = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : "";
                return $"{levelTag}{w.Translation}";
            }));
            await _bot.SendMessage(chatId, $"{header}\n\n{body}", parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
    }

    private async Task SendRoleSelectionAsync(long chatId, CancellationToken ct) =>
        await _bot.SendMessage(chatId,
            "👋 Welcome to *EnglishBot*!\n\nAre you a teacher or a student?",
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👨‍🏫 Teacher", "role_teacher"),
                    InlineKeyboardButton.WithCallbackData("📚 Student",  "role_student")
                }
            }),
            cancellationToken: ct);

    private async Task SendMainMenuAsync(long chatId, string role, CancellationToken ct, long userId = 0)
    {
        if (role == "Student" && userId != 0)
        {
            var student = await _db.GetUserAsync(userId);
            if (student is not null && !student.IsActivated)
            {
                await _bot.SendMessage(chatId,
                    "🔒 You haven't been added to a teacher's group yet.\n\n" +
                    "Please ask your teacher to add you by your Telegram username.",
                    cancellationToken: ct);
                return;
            }
        }

        if (role == "Teacher")
        {
            await _bot.SendMessage(chatId, "👨‍🏫 Teacher Menu",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("👤 Add Student",  "menu_add_student"),
                        InlineKeyboardButton.WithCallbackData("📤 Send Words",   "menu_send_words")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("👥 Students",   "menu_my_students"),
                        InlineKeyboardButton.WithCallbackData("📋 Words Sent",    "menu_words_sent")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🗑 Remove Student", "menu_remove_student"),
                        InlineKeyboardButton.WithCallbackData("🔍 Search Words",   "menu_search")
                    },
                    new[] { InlineKeyboardButton.WithCallbackData("✏️ My Name", "menu_set_name") }
                }),
                cancellationToken: ct);
        }
        else
        {
            await _bot.SendMessage(chatId, "📚 Student Menu",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("📝 Add Words",  "menu_add_words"),
                        InlineKeyboardButton.WithCallbackData("📚 Vocabulary", "menu_my_words")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🧩 Quiz",          "menu_quiz"),
                        InlineKeyboardButton.WithCallbackData("💪 Mistakes",      "menu_mistakes"),
                        InlineKeyboardButton.WithCallbackData("🔍 Search Words",  "menu_search")
                    },
                    new[] { InlineKeyboardButton.WithCallbackData("✏️ My Name",   "menu_set_name") }
                }),
                cancellationToken: ct);
        }
    }

    private async Task SendTopicChoiceAsync(long chatId, CancellationToken ct) =>
        await _bot.SendMessage(chatId, "🏷️ Add a topic to these words?",
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🤖 Auto-detect", "topic_auto"),
                    InlineKeyboardButton.WithCallbackData("✏️ Specify",     "topic_specify"),
                    InlineKeyboardButton.WithCallbackData("⏭ Skip",         "topic_skip")
                }
            }),
            cancellationToken: ct);

    // ── Utilities ─────────────────────────────────────────────────────────────

    private async Task GoMenu(long userId, long chatId, CancellationToken ct)
    {
        var user = await _db.GetUserAsync(userId);
        await SendMainMenuAsync(chatId, user?.Role ?? "Student", ct, userId);
    }

    private static readonly System.Text.RegularExpressions.Regex CefrPrefix =
        new(@"^\[(A0|A1|A2|B1|B2|C1|C2)\]\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<PendingWordEntry> BuildPendingWords(string inputText, string translationText)
    {
        var inputs = inputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var transl = translationText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return inputs.Select((orig, i) =>
        {
            var raw   = i < transl.Length ? transl[i] : orig;
            var match = CefrPrefix.Match(raw);
            return new PendingWordEntry
            {
                Original     = orig,
                Translation  = match.Success ? raw[match.Length..] : raw,
                EnglishLevel = match.Success ? match.Groups[1].Value : null
            };
        }).ToList();
    }

    private async Task SendWordListAsync(long chatId, List<VocabifyBot.Models.Word> words, string header, CancellationToken ct)
    {
        const int pageSize = 15;
        var page = words.Take(pageSize).ToList();
        var body = string.Join("\n\n", page.Select(w =>
        {
            var levelTag = w.EnglishLevel is not null ? $"*[{w.EnglishLevel}]* " : "";
            var topicTag = w.Topic is not null ? $"_{EscapeMd(w.Topic)}_ " : "";
            return $"{levelTag}{topicTag}{w.Translation}";
        }));
        var suffix = words.Count > pageSize ? $"\n\n_(Showing {pageSize} of {words.Count})_" : "";
        await _bot.SendMessage(chatId, $"{header}\n\n{body}{suffix}",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private static InlineKeyboardMarkup BackKeyboard(string callbackData) =>
        new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", callbackData) }
        });

    private static InlineKeyboardMarkup BrowseNavKeyboard(bool hasMore) =>
        new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                hasMore
                    ? InlineKeyboardButton.WithCallbackData("▶️ Next", "browse_next")
                    : InlineKeyboardButton.WithCallbackData("✅ Done",        "browse_cancel"),
                InlineKeyboardButton.WithCallbackData("❌ Cancel", "browse_cancel")
            }
        });

    private static List<List<VocabifyBot.Models.Word>> GroupByTopic(List<VocabifyBot.Models.Word> words) =>
        words
            .GroupBy(w => w.Topic ?? "")
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

    private static List<List<VocabifyBot.Models.Word>> GroupByBatch(List<VocabifyBot.Models.Word> words) =>
        words
            .GroupBy(w => w.BatchId ?? Guid.Empty)
            .OrderBy(g => g.First().CreatedAt)
            .Select(g => g.ToList())
            .ToList();

    // ── Search ─────────────────────────────────────────────────────────────────

    private async Task HandleMenuSearchAsync(long userId, long chatId, CancellationToken ct)
    {
        var user = await _db.GetUserAsync(userId);
        if (user?.Role == "Teacher")
        {
            // Teacher: pick student first
            var students = await _db.GetStudentsForTeacherAsync(userId);
            if (students.Count == 0)
            {
                await _bot.SendMessage(chatId,
                    "You have no students yet. Use *Add Student* first.",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
                return;
            }

            await _bot.SendMessage(chatId,
                "🔍 Choose a student to search vocabulary for:",
                replyMarkup: new InlineKeyboardMarkup(
                    students
                        .Select(s => new[] { InlineKeyboardButton.WithCallbackData(s.DisplayName, $"search_for_{s.TelegramId}") })
                        .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "back_to_menu") })
                        .ToArray()),
                cancellationToken: ct);
        }
        else
        {
            // Student: search own vocabulary directly
            SetState(userId, new ConversationState { State = UserState.AwaitingSearchQuery });
            await _bot.SendMessage(chatId,
                "🔍 Type the word or phrase to search in your vocabulary:",
                replyMarkup: BackKeyboard("back_to_menu"),
                cancellationToken: ct);
        }
    }

    private async Task HandleSearchForStudentAsync(long userId, long chatId, string data, CancellationToken ct)
    {
        var studentId = long.Parse(data["search_for_".Length..]);
        var student   = await _db.GetUserAsync(studentId);

        SetState(userId, new ConversationState
        {
            State             = UserState.AwaitingSearchQuery,
            SelectedStudentId = studentId
        });

        await _bot.SendMessage(chatId,
            $"🔍 Searching vocabulary for *{EscapeMd(student?.DisplayName ?? studentId.ToString())}*\n\nType the word or phrase to search:",
            parseMode: ParseMode.Markdown,
            replyMarkup: BackKeyboard("back_to_menu"),
            cancellationToken: ct);
    }

    private async Task HandleSearchQueryAsync(long userId, long chatId, string query, CancellationToken ct)
    {
        var user      = await _db.GetUserAsync(userId);
        var state     = GetState(userId);
        var isTeacher = user?.Role == "Teacher";
        var searchId  = isTeacher ? state.SelectedStudentId ?? userId : userId;
        var studentId = state.SelectedStudentId;

        var results = await _db.SearchWordsAsync(searchId, query);
        ResetState(userId);

        var navigation = isTeacher && studentId.HasValue
            ? new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔍 Search again", $"search_for_{studentId.Value}") },
                new[] { InlineKeyboardButton.WithCallbackData("👥 Other student",         "menu_search") },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Menu",                 "back_to_menu") }
            })
            : new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔍 Search again", "menu_search") },
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Menu", "back_to_menu") }
            });

        if (results.Count == 0)
        {
            await _bot.SendMessage(chatId,
                $"🔍 No results found for *{EscapeMd(query)}*.",
                parseMode: ParseMode.Markdown,
                replyMarkup: navigation,
                cancellationToken: ct);
            return;
        }

        VocabifyBot.Models.User? student = isTeacher && studentId.HasValue ? await _db.GetUserAsync(studentId.Value) : null;
        var who  = student is not null ? $" in {EscapeMd(student.DisplayName)}'s vocabulary" : string.Empty;
        var body = string.Join("\n\n", results.Select(w => FormatWord(w)));

        await _bot.SendMessage(chatId,
            $"🔍 *{results.Count}* result(s) for *{EscapeMd(query)}*{who}:\n\n{body}",
            parseMode: ParseMode.Markdown,
            replyMarkup: navigation,
            cancellationToken: ct);
    }

    private static string FormatWord(VocabifyBot.Models.Word w)
    {
        var level = w.EnglishLevel is not null ? $" *[{w.EnglishLevel}]*" : string.Empty;
        var topic = w.Topic is not null ? $"\n🏷️ _{EscapeMd(w.Topic)}_" : string.Empty;
        return $"*{EscapeMd(w.OriginalWord)}*{level} — {w.Translation}{topic}";
    }

    // ── Generate by Level ──────────────────────────────────────────────────────

    private async Task HandleGenTopicInputAsync(long userId, long chatId, string topic, CancellationToken ct)
    {
        MutateState(userId, s => { s.GenTopic = topic.Trim(); s.State = UserState.None; });
        await GenerateAndShowPreviewAsync(userId, chatId, ct);
    }

    private async Task GenerateAndShowPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null || state.GenLevel is null) { await GoMenu(userId, chatId, ct); return; }

        var notice = await _bot.SendMessage(chatId, "⏳ Generating words with AI…", cancellationToken: ct);
        try
        {
            var existing  = await _db.GetAllWordOriginalsAsync(state.SelectedStudentId.Value);
            var generated = await _openAi.GenerateWordsByLevelAsync(state.GenLevel, state.GenCount, state.GenTopic, existing);
            var preview   = VocabifyBot.UI.WordFormatter.BuildPendingWordsFromGeneration(generated);

            MutateState(userId, s => s.GenPreview = preview);

            if (preview.Count == 0)
            {
                await _bot.SendMessage(chatId,
                    "⚠️ AI couldn't generate words. Try regenerating or change the settings.",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Regenerate", "gen_retry") },
                        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",        "gen_start") }
                    }),
                    cancellationToken: ct);
                return;
            }

            var student    = await _db.GetUserAsync(state.SelectedStudentId.Value);
            var topicNote  = state.GenTopic is not null ? $" · 🏷️ _{EscapeMd(state.GenTopic)}_" : string.Empty;
            var header     = $"🤖 *{preview.Count} {state.GenLevel} words* for *{EscapeMd(student?.DisplayName ?? "")}*{topicNote}:";
            var body       = string.Join("\n\n", preview.Select(p =>
            {
                var lvl = p.EnglishLevel is not null ? $"*[{p.EnglishLevel}]* " : string.Empty;
                return $"{lvl}{EscapeMd(p.Translation)}";
            }));

            await _bot.SendMessage(chatId,
                $"{header}\n\n{body}",
                parseMode: ParseMode.Markdown,
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✅ Confirm",     "gen_confirm"),
                        InlineKeyboardButton.WithCallbackData("🔄 Regenerate", "gen_retry"),
                        InlineKeyboardButton.WithCallbackData("⬅️ Back",        "gen_start")
                    }
                }),
                cancellationToken: ct);
        }
        finally
        {
            try { await _bot.DeleteMessage(chatId, notice.MessageId, cancellationToken: ct); } catch { }
        }
    }

    private async Task ConfirmGenPreviewAsync(long userId, long chatId, CancellationToken ct)
    {
        var state = GetState(userId);
        if (state.SelectedStudentId is null || state.GenPreview.Count == 0) { await GoMenu(userId, chatId, ct); return; }

        var batchId = Guid.NewGuid();
        var words   = state.GenPreview.Select(p => new VocabifyBot.Models.Word
        {
            OriginalWord  = p.Original,
            Translation   = p.Translation,
            EnglishLevel  = p.EnglishLevel,
            Topic         = state.GenTopic,
            BatchId       = batchId,
            AddedByUserId = userId,
            ForStudentId  = state.SelectedStudentId.Value
        }).ToList();

        await _db.SaveWordsAsync(words);

        var teacher = await _db.GetUserAsync(userId);
        var student = await _db.GetUserAsync(state.SelectedStudentId.Value);

        await _bot.SendMessage(chatId,
            $"✅ *{words.Count}* {state.GenLevel} words saved for *{EscapeMd(student?.DisplayName ?? "")}*!",
            parseMode: ParseMode.Markdown, cancellationToken: ct);

        // Notify student
        try
        {
            var body      = string.Join("\n\n", words.Select(w => $"*[{w.EnglishLevel}]* {EscapeMd(w.Translation)}"));
            var topicNote = state.GenTopic is not null ? $" · {state.GenTopic}" : string.Empty;
            await _bot.SendMessage(state.SelectedStudentId.Value,
                $"📚 New *{state.GenLevel}* vocabulary from {EscapeMd(teacher?.DisplayName ?? "your teacher")}{topicNote}:\n\n{body}",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch { }

        ResetState(userId);
        await SendMainMenuAsync(chatId, "Teacher", ct);
    }

    /// <summary>Inline CEFR level keyboard used for Generate by Level (avoids dependency on Keyboards/WordFormatter).</summary>
    private static InlineKeyboardMarkup BuildCefrKeyboard(string prefix, string cancelCallback)
    {
        string[] levels = ["A0", "A1", "A2", "B1", "B2", "C1", "C2"];
        return new InlineKeyboardMarkup(new[]
        {
            levels[..4].Select(l => InlineKeyboardButton.WithCallbackData(l, $"{prefix}{l}")).ToArray(),
            levels[4..].Select(l => InlineKeyboardButton.WithCallbackData(l, $"{prefix}{l}")).ToArray(),
            new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", cancelCallback) }
        });
    }

    private const int ChunkSize = 20;

    private ConversationState GetState(long userId) =>
        _states.GetValueOrDefault(userId, new ConversationState());

    private void SetState(long userId, ConversationState state) => _states[userId] = state;

    private void ResetState(long userId) => _states[userId] = new ConversationState();

    private void MutateState(long userId, Action<ConversationState> mutate)
    {
        var state = GetState(userId);
        mutate(state);
        _states[userId] = state;
    }

    private static string EscapeMd(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("`", "\\`");

    private static string FullMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException) parts.Add(e.Message);
        return string.Join(" → ", parts);
    }

    private static Task HandleErrorAsync(ITelegramBotClient _, Exception ex, HandleErrorSource __, CancellationToken ___)
    {
        Console.WriteLine($"⚠️  {(ex is ApiRequestException api ? $"Telegram [{api.ErrorCode}]: {api.Message}" : ex.ToString())}");
        return Task.CompletedTask;
    }
}
