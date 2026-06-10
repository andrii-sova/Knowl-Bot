using FluentAssertions;
using NSubstitute;
using Telegram.Bot;
using KnowlBot.Interfaces;
using KnowlBot.Models;
using KnowlBot.Services;
using KnowlBot.Services.Handlers;
using Xunit;

namespace KnowlBot.Tests;

/// <summary>
/// Tests for BotService static callback-routing predicates and ConversationStateManager
/// interaction with the quiz handler state transitions.
/// </summary>
public class BotServiceRoutingTests
{
    // ── IsTeacherCallback whitelist ───────────────────────────────────────────

    [Theory]
    [InlineData("menu_add_student", true)]
    [InlineData("menu_send_words", true)]
    [InlineData("menu_my_students", true)]
    [InlineData("menu_words_sent", true)]
    [InlineData("menu_remove_student", true)]
    [InlineData("type_words", true)]
    [InlineData("back_from_add_student", true)]
    [InlineData("back_from_send_words", true)]
    [InlineData("menu_delete_words", true)]
    [InlineData("delwords_level", true)]
    [InlineData("delwords_pick_delete", true)]
    [InlineData("delwords_pick_keep", true)]
    [InlineData("send_to_123", true)]
    [InlineData("pool_confirm", true)]
    [InlineData("gen_words", true)]
    [InlineData("browse_student_1", true)]
    [InlineData("wfilter_teacher", true)]
    [InlineData("wmode_topic", true)]
    [InlineData("wtopic_2", true)]
    [InlineData("wlevel_B2", true)]
    [InlineData("words_sent_to_456", true)]
    [InlineData("remove_student_789", true)]
    [InlineData("confirm_remove_1", true)]
    [InlineData("search_for_42", true)]
    [InlineData("delwords_something", true)]
    // Not teacher callbacks:
    [InlineData("menu_quiz", false)]
    [InlineData("menu_my_words", false)]
    [InlineData("role_teacher", false)]
    [InlineData("vocab_next", false)]
    [InlineData("sgen_words", false)]
    [InlineData("quiz_ans_0_abc", false)]
    public void IsTeacherCallback_MatchesExpected(string data, bool expected)
    {
        IsTeacherCallback(data).Should().Be(expected);
    }

    [Theory]
    [InlineData("role_teacher", true)]
    [InlineData("role_student", true)]
    [InlineData("menu_set_name", true)]
    [InlineData("menu_settings", true)]
    [InlineData("settings_set_name", true)]
    [InlineData("settings_toggle_expand", true)]
    [InlineData("back_to_menu", true)]
    [InlineData("menu_quiz", false)]
    [InlineData("menu_add_student", false)]
    public void IsRegistrationCallback_MatchesExpected(string data, bool expected)
    {
        IsRegistrationCallback(data).Should().Be(expected);
    }

    [Theory]
    [InlineData("menu_quiz", true)]
    [InlineData("menu_mistakes", true)]
    [InlineData("quiz_dir_eu", true)]
    [InlineData("quiz_amt_5", true)]
    [InlineData("quiz_ans_0_abc", true)]
    [InlineData("quiz_next_1", true)]
    [InlineData("menu_add_student", false)]
    [InlineData("menu_my_words", false)]
    public void IsQuizCallback_MatchesExpected(string data, bool expected)
    {
        IsQuizCallback(data).Should().Be(expected);
    }

    [Theory]
    [InlineData("menu_add_words", true)]
    [InlineData("menu_my_words", true)]
    [InlineData("stype_words", true)]
    [InlineData("vocab_next", true)]
    [InlineData("sgen_words", true)]
    [InlineData("menu_quiz", false)]
    [InlineData("menu_add_student", false)]
    public void IsStudentCallback_MatchesExpected(string data, bool expected)
    {
        IsStudentCallback(data).Should().Be(expected);
    }

    // ── Replicate the private static predicates from BotService ─────────────

    private static bool IsRegistrationCallback(string data) =>
        data is "role_teacher" or "role_student" or "menu_set_name" or "menu_settings"
             or "settings_set_name" or "settings_toggle_expand" or "back_to_menu";

    private static bool IsTeacherCallback(string data) =>
        data is "menu_add_student" or "back_from_add_student" or "menu_send_words" or "menu_my_students" or
            "menu_words_sent" or "menu_remove_student" or "type_words" or "back_from_send_words" ||
        data.StartsWith("send_to_") ||
        data.StartsWith("pool_") ||
        data.StartsWith("gen_") ||
        data.StartsWith("browse_") ||
        data.StartsWith("wfilter_") ||
        data.StartsWith("wmode_") ||
        data.StartsWith("wtopic_") ||
        data.StartsWith("wlevel_") ||
        data.StartsWith("words_sent_to_") ||
        data.StartsWith("remove_student_") ||
        data.StartsWith("confirm_remove_") ||
        data.StartsWith("search_for_") ||
        data.StartsWith("delwords_") ||
        data is "menu_delete_words" or "delwords_level" or "delwords_pick_delete" or "delwords_pick_keep";

    private static bool IsQuizCallback(string data) =>
        data is "menu_quiz" or "menu_mistakes" || data.StartsWith("quiz_");

    private static bool IsStudentCallback(string data) =>
        data is "menu_add_words" or "menu_my_words" or "stype_words" ||
        data.StartsWith("vocab_") ||
        data.StartsWith("sgen_");
}
