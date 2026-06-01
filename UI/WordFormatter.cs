using KnowlBot.Models;
using System.Text.RegularExpressions;

namespace KnowlBot.UI;

public static class WordFormatter
{
    public static readonly string[] CefrLevels = ["A1", "A2", "B1", "B2", "C1", "C2"];

    private static readonly Regex CefrPrefix = new(@"^\[(A0|A1|A2|B1|B2|C1|C2)\]\s*", RegexOptions.Compiled);

    public static string EscapeMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return Regex.Replace(text, @"([_\*`\[\\])", @"\$1");
    }

    public static string Truncate(string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s;
        return s[..maxLength].TrimEnd(' ', ',', ';') + "…";
    }

    /// <summary>
    /// Formats a saved Word for display.
    /// [B2] diminish [dɪˈmɪnɪʃ] (decrease) - зменшувати, применшувати (example — translation)
    /// </summary>
    public static string FormatWordLine(Word w)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(w.CefrLevel))
            sb.Append($"*[{EscapeMarkdown(w.CefrLevel)}]* ");

        sb.Append(EscapeMarkdown(w.OriginalWord));

        if (!string.IsNullOrWhiteSpace(w.Transcription))
            sb.Append($" \\[{EscapeMarkdown(w.Transcription)}\\]");

        if (!string.IsNullOrWhiteSpace(w.Synonym))
            sb.Append($" \\({EscapeMarkdown(w.Synonym)}\\)");

        sb.Append(" \\- ");
        sb.Append(EscapeMarkdown(w.MostlyUsedTranslation));

        if (!string.IsNullOrWhiteSpace(w.OtherTranslation))
            sb.Append($", {EscapeMarkdown(w.OtherTranslation)}");

        if (!string.IsNullOrWhiteSpace(w.ExampleUsage) && !string.IsNullOrWhiteSpace(w.ExampleUsageTranslation))
            sb.Append($" \\({EscapeMarkdown(w.ExampleUsage)} — {EscapeMarkdown(w.ExampleUsageTranslation)}\\)");

        return sb.ToString();
    }

    /// <summary>
    /// Formats a PendingWordEntry for preview display (before saving).
    /// Same layout as FormatWordLine.
    /// </summary>
    public static string FormatPendingLine(PendingWordEntry p)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(p.CefrLevel))
            sb.Append($"*[{EscapeMarkdown(p.CefrLevel)}]* ");

        sb.Append(EscapeMarkdown(p.Word));

        if (!string.IsNullOrWhiteSpace(p.Transcription))
            sb.Append($" \\[{EscapeMarkdown(p.Transcription)}\\]");

        if (!string.IsNullOrWhiteSpace(p.Synonym))
            sb.Append($" \\({EscapeMarkdown(p.Synonym)}\\)");

        sb.Append(" \\- ");
        sb.Append(EscapeMarkdown(p.MostlyUsedTranslation));

        if (!string.IsNullOrWhiteSpace(p.OtherTranslation))
            sb.Append($", {EscapeMarkdown(p.OtherTranslation)}");

        if (!string.IsNullOrWhiteSpace(p.ExampleUsage) && !string.IsNullOrWhiteSpace(p.ExampleUsageTranslation))
            sb.Append($" \\({EscapeMarkdown(p.ExampleUsage)} — {EscapeMarkdown(p.ExampleUsageTranslation)}\\)");

        return sb.ToString();
    }

    public static string QuizQuestion(Word w, string direction) =>
        direction == "eu" ? w.OriginalWord : w.MostlyUsedTranslation ?? string.Empty;

    public static string QuizAnswer(Word w, string direction) =>
        direction == "eu" ? w.MostlyUsedTranslation ?? string.Empty : w.OriginalWord;
}
