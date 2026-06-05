using KnowlBot.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace KnowlBot.UI;

public static class Keyboards
{
    public static InlineKeyboardMarkup TeacherMenu() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("👤 Add Student",    "menu_add_student"),
            InlineKeyboardButton.WithCallbackData("📤 Send Words",     "menu_send_words")
        },
        new[]
        {
            InlineKeyboardButton.WithCallbackData("👥 Students",       "menu_my_students"),
            InlineKeyboardButton.WithCallbackData("📋 Words Sent",     "menu_words_sent")
        },
        new[]
        {
            InlineKeyboardButton.WithCallbackData("🗑 Remove Student", "menu_remove_student"),
            InlineKeyboardButton.WithCallbackData("🔍 Search Words",   "menu_search")
        },
        new[]
        {
            InlineKeyboardButton.WithCallbackData("📚 Vocabulary",  "menu_my_words"),
            InlineKeyboardButton.WithCallbackData("🧩 Quiz",        "menu_quiz"),
            InlineKeyboardButton.WithCallbackData("💪 Mistakes",    "menu_mistakes")
        },
        new[] { InlineKeyboardButton.WithCallbackData("🗂 Delete Words",  "menu_delete_words") },
        new[] { InlineKeyboardButton.WithCallbackData("✏️ My Name",       "menu_set_name") }
    });

    public static InlineKeyboardMarkup StudentMenu() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("📝 Add Words",      "menu_add_words"),
            InlineKeyboardButton.WithCallbackData("📚 Vocabulary",     "menu_my_words")
        },
        new[]
        {
            InlineKeyboardButton.WithCallbackData("🧩 Quiz",           "menu_quiz"),
            InlineKeyboardButton.WithCallbackData("💪 Mistakes",       "menu_mistakes"),
            InlineKeyboardButton.WithCallbackData("🔍 Search Words",   "menu_search")
        },
        new[] { InlineKeyboardButton.WithCallbackData("✏️ My Name",    "menu_set_name") }
    });

    public static InlineKeyboardMarkup RoleSelection() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("👨‍🏫 Teacher", "role_teacher"),
            InlineKeyboardButton.WithCallbackData("📚 Student",  "role_student")
        }
    });

    public static InlineKeyboardMarkup BackButton(string callbackData) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", callbackData) }
    });

    public static InlineKeyboardMarkup TopicChoice() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("🤖 Auto-detect", "topic_auto"),
            InlineKeyboardButton.WithCallbackData("✏️ Specify",     "topic_specify"),
            InlineKeyboardButton.WithCallbackData("⏭ Skip",         "topic_skip")
        }
    });

    public static InlineKeyboardMarkup StudentList(IEnumerable<User> students, string callbackPrefix) => new(
        students
            .Select(student => new[]
            {
                InlineKeyboardButton.WithCallbackData(student.DisplayName, $"{callbackPrefix}{student.TelegramId}")
            })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "back_to_menu") })
            .ToArray());

    public static InlineKeyboardMarkup SearchResultNavigation() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔍 Search again", "menu_search") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Menu",         "back_to_menu") }
    });

    public static InlineKeyboardMarkup TeacherSearchResultNavigation(long studentId) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔍 Search again",   $"search_for_{studentId}") },
        new[] { InlineKeyboardButton.WithCallbackData("👥 Other student",   "menu_search") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Menu",            "back_to_menu") }
    });

    public static InlineKeyboardMarkup CefrLevelButtons(string callbackPrefix)
    {
        var buttons = WordFormatter.CefrLevels
            .Select(level => InlineKeyboardButton.WithCallbackData(level, $"{callbackPrefix}{level}"))
            .ToArray();

        var cancelCallback = callbackPrefix switch
        {
            "pool_level_"  => "pool_cancel",
            "gen_level_"   => "gen_cancel",
            "sgen_level_"  => "sgen_cancel",
            _ => "quiz_cancel"
        };

        return new InlineKeyboardMarkup(
            buttons.Chunk(4)
                .Append(new[] { InlineKeyboardButton.WithCallbackData("🔀 Any",   $"{callbackPrefix}any") })
                .Append(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", cancelCallback) })
                .ToArray());
    }

    public static InlineKeyboardMarkup CefrLevelMultiSelectButtons(
        IReadOnlySet<string> selected, string togglePrefix, string doneCallback, string cancelCallback)
    {
        var buttons = WordFormatter.CefrLevels
            .Select(level => InlineKeyboardButton.WithCallbackData(
                selected.Contains(level) ? $"✅ {level}" : level,
                $"{togglePrefix}{level}"))
            .ToArray();

        var doneLabel = selected.Count > 0 ? $"Done ({selected.Count} selected)" : "Any level";

        return new InlineKeyboardMarkup(
            buttons.Chunk(3)
                .Select(row => row)
                .Append(new[] { InlineKeyboardButton.WithCallbackData($"✅ {doneLabel}", doneCallback) })
                .Append(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", cancelCallback) })
                .ToArray());
    }

    public static InlineKeyboardMarkup SendWordChoice() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("✍️ Type Words",        "type_words") },
        new[] { InlineKeyboardButton.WithCallbackData("🎯 Assign from Pool",  "pool_start") },
        new[] { InlineKeyboardButton.WithCallbackData("🤖 Generate by Level", "gen_start") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",              "back_from_send_words") }
    });

    public static InlineKeyboardMarkup GenCountButtons() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("5",  "gen_count_5"),
            InlineKeyboardButton.WithCallbackData("10", "gen_count_10"),
            InlineKeyboardButton.WithCallbackData("20", "gen_count_20")
        },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "gen_start") }
    });

    public static InlineKeyboardMarkup GenTopicPromptButtons() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("⏭ Skip topic", "gen_topic_skip") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",       "gen_start") }
    });

    public static InlineKeyboardMarkup ExpandableWordKeyboard(
        int wordCount,
        IReadOnlySet<int> expanded,
        string togglePrefix,
        InlineKeyboardButton[][]? actionRows = null)
    {
        const int buttonsPerRow = 5;
        var rows = Enumerable.Range(0, wordCount)
            .Chunk(buttonsPerRow)
            .Select(chunk => chunk.Select(i => InlineKeyboardButton.WithCallbackData(
                expanded.Contains(i) ? $"{i + 1}✕" : $"{i + 1}≡", $"{togglePrefix}{i}")).ToArray())
            .ToList<InlineKeyboardButton[]>();

        if (actionRows is not null)
            rows.AddRange(actionRows);

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardButton[][] GenPreviewActionRows() => new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("✅ Confirm", "gen_confirm"), InlineKeyboardButton.WithCallbackData("🔄 Regenerate", "gen_retry"), InlineKeyboardButton.WithCallbackData("⬅️ Back", "gen_start") },
        new[] { InlineKeyboardButton.WithCallbackData("✂️ Remove Words", "gen_remove") }
    };

    public static InlineKeyboardButton[][] SGenPreviewActionRows() => new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("✅ Save", "sgen_confirm"), InlineKeyboardButton.WithCallbackData("🔄 Regenerate", "sgen_retry"), InlineKeyboardButton.WithCallbackData("⬅️ Back", "sgen_start") },
        new[] { InlineKeyboardButton.WithCallbackData("✂️ Remove Words", "sgen_remove") }
    };

    public static InlineKeyboardButton[][] VocabPageActionRows(int currentPage, int totalPages)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var nav = new List<InlineKeyboardButton>();
        if (currentPage > 0) nav.Add(InlineKeyboardButton.WithCallbackData("◀️ Prev", "vocab_page_prev"));
        if (currentPage < totalPages - 1) nav.Add(InlineKeyboardButton.WithCallbackData("▶️ Next", "vocab_page_next"));
        if (nav.Count > 0) rows.Add(nav.ToArray());
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Menu", "back_to_menu") });
        return rows.ToArray();
    }

    public static InlineKeyboardMarkup GenPreviewButtons() => new(GenPreviewActionRows());

    public static InlineKeyboardMarkup SGenPreviewButtons() => new(SGenPreviewActionRows());

    public static InlineKeyboardMarkup PoolCountButtons() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("5",  "pool_count_5"),
            InlineKeyboardButton.WithCallbackData("10", "pool_count_10"),
            InlineKeyboardButton.WithCallbackData("20", "pool_count_20")
        },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "pool_start") }
    });

    public static InlineKeyboardMarkup PoolPreviewButtons() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Confirm", "pool_confirm"),
            InlineKeyboardButton.WithCallbackData("🔄 Shuffle", "pool_shuffle"),
            InlineKeyboardButton.WithCallbackData("❌ Cancel",  "pool_cancel")
        }
    });

    public static InlineKeyboardMarkup WordFilterSelection(bool forStudent = false)
    {
        var prefix        = forStudent ? "sfilter_" : "wfilter_";
        var studentLabel  = forStudent ? "🧑 By me" : "🧑 By student";
        return new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("👨‍🏫 By teacher", prefix + "teacher") },
            new[] { InlineKeyboardButton.WithCallbackData(studentLabel,      prefix + "student") },
            new[] { InlineKeyboardButton.WithCallbackData("📋 Both",         prefix + "both") },
            new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",         "back_to_menu") }
        });
    }

    public static InlineKeyboardMarkup WordModeSelection() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🏷️ By topic",    "wmode_topic") },
        new[] { InlineKeyboardButton.WithCallbackData("🔤 By level",    "wmode_level") },
        new[] { InlineKeyboardButton.WithCallbackData("📦 By chunks",   "wmode_chunks") },
        new[] { InlineKeyboardButton.WithCallbackData("💬 By message",  "wmode_messages") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",        "back_to_menu") }
    });

    public static InlineKeyboardMarkup BrowseNavigation(bool hasPrev, bool hasMore)
    {
        var nav = new List<InlineKeyboardButton>();
        if (hasPrev) nav.Add(InlineKeyboardButton.WithCallbackData("◀️ Prev", "browse_prev"));
        if (hasMore) nav.Add(InlineKeyboardButton.WithCallbackData("▶️ Next", "browse_next"));

        var rows = new List<InlineKeyboardButton[]>();
        if (nav.Count > 0) rows.Add(nav.ToArray());
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Menu", "browse_cancel") });
        return new(rows.ToArray());
    }

    public static InlineKeyboardMarkup VocabPageNavigation(int currentPage, int totalPages) =>
        new(VocabPageActionRows(currentPage, totalPages));

    public static InlineKeyboardMarkup ConfirmRemoveStudent(long studentId) => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Remove",  $"confirm_remove_{studentId}"),
            InlineKeyboardButton.WithCallbackData("❌ Cancel",  "back_to_menu")
        }
    });

    public static InlineKeyboardMarkup QuizDirectionSelection() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("🇬🇧→🇺🇦 Eng→Ukr", "quiz_dir_eu"),
            InlineKeyboardButton.WithCallbackData("🇺🇦→🇬🇧 Ukr→Eng", "quiz_dir_ue")
        },
        new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", "quiz_cancel") }
    });

    public static InlineKeyboardMarkup QuizAmountSelection() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("5",  "quiz_amt_5"),
            InlineKeyboardButton.WithCallbackData("10", "quiz_amt_10"),
            InlineKeyboardButton.WithCallbackData("20", "quiz_amt_20"),
            InlineKeyboardButton.WithCallbackData("30", "quiz_amt_30")
        },
        new[] { InlineKeyboardButton.WithCallbackData("✏️ Custom",  "quiz_amt_custom") },
        new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",   "quiz_cancel") }
    });

    public static InlineKeyboardMarkup QuizTopicButtons(IReadOnlyList<string> topics) => new(
        topics
            .Select((topic, index) => new[]
            {
                InlineKeyboardButton.WithCallbackData($"🏷️ {topic}", $"quiz_top_{index}")
            })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("🔀 Any topic", "quiz_top_any") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",     "quiz_cancel") })
            .ToArray());

    public static InlineKeyboardMarkup QuizAnswerGrid(int qIdx, IReadOnlyList<Word> options, string direction)
    {
        var rows = new List<InlineKeyboardButton[]>();
        var buttons = options
            .Select(option => InlineKeyboardButton.WithCallbackData(
                WordFormatter.Truncate(WordFormatter.QuizAnswer(option, direction), 32),
                $"quiz_ans_{qIdx}_{option.Id}"))
            .ToList();

        for (var i = 0; i < buttons.Count; i += 2)
            rows.Add(buttons.Skip(i).Take(2).ToArray());

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🚫 Stop Quiz", "quiz_cancel") });
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup VocabTopicButtons(IReadOnlyList<string> topics) => new(
        topics
            .Select((topic, index) => new[]
            {
                InlineKeyboardButton.WithCallbackData($"🏷️ {topic}", $"vocab_t_{index}")
            })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("🔤 By Level",  "vocab_level") })
            .Append(new[] { InlineKeyboardButton.WithCallbackData("📋 All Words", "vocab_all") })
            .ToArray());

    public static InlineKeyboardMarkup VocabLevelButtons() => new(
        WordFormatter.CefrLevels
            .Select(level => InlineKeyboardButton.WithCallbackData(level, $"vocab_lvl_{level}"))
            .ToArray()
            .Chunk(4)
            .Select(row => row)
            .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "menu_my_words") })
            .ToArray());

    public static InlineKeyboardMarkup StudentAddWordsChoice() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("✍️ Type Words",        "stype_words") },
        new[] { InlineKeyboardButton.WithCallbackData("🤖 Generate by Level", "sgen_start") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",              "back_to_menu") }
    });

    public static InlineKeyboardMarkup SGenCountButtons() => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("5",  "sgen_count_5"),
            InlineKeyboardButton.WithCallbackData("10", "sgen_count_10"),
            InlineKeyboardButton.WithCallbackData("20", "sgen_count_20")
        },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "sgen_start") }
    });

    public static InlineKeyboardMarkup SGenTopicPromptButtons() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("⏭ Skip topic", "sgen_topic_skip") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",       "sgen_start") }
    });

    public static InlineKeyboardMarkup PoolEmptyState() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔙 Change level", "pool_start") },
        new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",        "pool_cancel") }
    });

    public static InlineKeyboardMarkup CefrLevelBrowseButtons() => new(
        WordFormatter.CefrLevels
            .Select(level => InlineKeyboardButton.WithCallbackData(level, $"wlevel_{level}"))
            .ToArray()
            .Chunk(4)
            .Select(row => row)
            .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "back_to_menu") })
            .ToArray());

    public static InlineKeyboardMarkup TopicSelectionButtons(List<string> topics) => new(
        topics
            .Select((t, i) => InlineKeyboardButton.WithCallbackData(
                string.IsNullOrEmpty(t) ? "(no topic)" : t, $"wtopic_{i}"))
            .ToArray()
            .Chunk(2)
            .Select(row => row)
            .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "back_to_menu") })
            .ToArray());

    public static InlineKeyboardMarkup DeleteWordsMode() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔤 By Level",    "delwords_level") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back",        "back_to_menu") }
    });

    public static InlineKeyboardMarkup DeleteWordsByLevelButtons() => new(
        WordFormatter.CefrLevels
            .Select(level => InlineKeyboardButton.WithCallbackData(level, $"delwords_lvl_{level}"))
            .ToArray()
            .Chunk(4)
            .Select(row => row)
            .Append(new[] { InlineKeyboardButton.WithCallbackData("⬅️ Back", "menu_delete_words") })
            .ToArray());

    public static InlineKeyboardMarkup DeleteWordsActionButtons(int count) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData($"🗑 Delete all {count}",      "delwords_confirm") },
        new[] { InlineKeyboardButton.WithCallbackData("✂️ Choose words to DELETE",   "delwords_pick_delete") },
        new[] { InlineKeyboardButton.WithCallbackData("✅ Choose words to KEEP",     "delwords_pick_keep") },
        new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel",                    "back_to_menu") }
    });

    public static InlineKeyboardMarkup ConfirmDeleteWords(int count, string description) => new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData($"✅ Delete {count} words", "delwords_confirm"),
            InlineKeyboardButton.WithCallbackData("❌ Cancel", "back_to_menu")
        }
    });

    public static InlineKeyboardMarkup SingleAction(string text, string callbackData) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData(text, callbackData) }
    });
}
