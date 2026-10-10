using System.Text.RegularExpressions;

namespace FinGuitar.Web.Chat;

/// <summary>Removes any thinking text the model still produces (D4). Thinking is never shown or stored.</summary>
public static partial class ThinkStripper
{
    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Closed();

    [GeneratedRegex(@"<think>.*$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Unclosed();

    [GeneratedRegex(@"\[Shown:[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex ShownNote();

    /// <summary>The "[Shown: …]" notes are history hints for the model; if it copies one into its reply, remove it.</summary>
    public static string StripShownNotes(string? text) =>
        string.IsNullOrEmpty(text) ? "" : ShownNote().Replace(text, "").Trim();

    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = Closed().Replace(text, "");
        s = Unclosed().Replace(s, "");
        s = s.Replace("</think>", "", StringComparison.OrdinalIgnoreCase);
        return s.Trim();
    }
}
