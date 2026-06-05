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
        // Escape all MarkdownV2 special characters
        return Regex.Replace(text, @"([_\*\[\]()~`>#+\-=|{}.!\\])", @"\$1");
    }

    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    public static string Truncate(string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s;
        return s[..maxLength].TrimEnd(' ', ',', ';') + "…";
    }

    /// <summary>
    /// Formats a saved Word for HTML display (ParseMode.Html).
    /// <b>[B2]</b> word [transcription] (synonym) - translation (example — translation)
    /// </summary>
    public static string FormatWordLine(Word w)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(w.CefrLevel))
            sb.Append($"[{w.CefrLevel}] ");

        sb.Append(w.OriginalWord);

        if (!string.IsNullOrWhiteSpace(w.Transcription))
            sb.Append($" [{w.Transcription}]");

        if (!string.IsNullOrWhiteSpace(w.Synonym))
            sb.Append($" ({w.Synonym})");

        sb.Append(" — ");
        sb.Append(w.MostlyUsedTranslation);

        if (!string.IsNullOrWhiteSpace(w.OtherTranslation))
            sb.Append($", {w.OtherTranslation}");

        if (!string.IsNullOrWhiteSpace(w.ExampleUsage))
        {
            sb.Append($" ({w.ExampleUsage}");
            if (!string.IsNullOrWhiteSpace(w.ExampleUsageTranslation))
                sb.Append($" — {w.ExampleUsageTranslation}");
            sb.Append(")");
        }

        return sb.ToString();
    }

    public static string FormatCompactLine(Word w)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(w.CefrLevel)) sb.Append($"[{w.CefrLevel}] ");
        sb.Append(w.OriginalWord);
        if (!string.IsNullOrWhiteSpace(w.Transcription)) sb.Append($" [{w.Transcription}]");
        sb.Append(" — ");
        sb.Append(w.MostlyUsedTranslation);
        return sb.ToString();
    }

    public static WordDisplayEntry ToDisplayEntry(Word w) =>
        new(FormatCompactLine(w), FormatWordLine(w));

    public static string FormatPendingLine(PendingWordEntry p)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(p.CefrLevel))
            sb.Append($"[{p.CefrLevel}] ");

        sb.Append(p.Word);

        if (!string.IsNullOrWhiteSpace(p.Transcription))
            sb.Append($" [{p.Transcription}]");

        if (!string.IsNullOrWhiteSpace(p.Synonym))
            sb.Append($" ({p.Synonym})");

        sb.Append(" — ");
        sb.Append(p.MostlyUsedTranslation);

        if (!string.IsNullOrWhiteSpace(p.OtherTranslation))
            sb.Append($", {p.OtherTranslation}");

        if (!string.IsNullOrWhiteSpace(p.ExampleUsage))
        {
            sb.Append($" ({p.ExampleUsage}");
            if (!string.IsNullOrWhiteSpace(p.ExampleUsageTranslation))
                sb.Append($" — {p.ExampleUsageTranslation}");
            sb.Append(")");
        }

        return sb.ToString();
    }

    public static string FormatCompactLine(PendingWordEntry p)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(p.CefrLevel)) sb.Append($"[{p.CefrLevel}] ");
        sb.Append(p.Word);
        if (!string.IsNullOrWhiteSpace(p.Transcription)) sb.Append($" [{p.Transcription}]");
        sb.Append(" — ");
        sb.Append(p.MostlyUsedTranslation);
        return sb.ToString();
    }

    public static WordDisplayEntry ToDisplayEntry(PendingWordEntry p) =>
        new(FormatCompactLine(p), FormatPendingLine(p));

    public static string QuizQuestion(Word w, string direction) =>
        direction == "eu" ? w.OriginalWord : w.MostlyUsedTranslation ?? string.Empty;

    public static string QuizAnswer(Word w, string direction) =>
        direction == "eu" ? w.MostlyUsedTranslation ?? string.Empty : w.OriginalWord;
}
