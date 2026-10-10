using System.Text.RegularExpressions;

namespace FinGuitar.Web.Chat;

/// <summary>Removes any thinking text the model still produces (D4). Thinking is never shown or stored.</summary>
public static partial class ThinkStripper
{
    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Closed();

    [GeneratedRegex(@"<think>.*$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Unclosed();

    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = Closed().Replace(text, "");
        s = Unclosed().Replace(s, "");
        s = s.Replace("</think>", "", StringComparison.OrdinalIgnoreCase);
        return s.Trim();
    }
}
