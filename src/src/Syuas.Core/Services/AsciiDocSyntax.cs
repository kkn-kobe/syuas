using System.Text.RegularExpressions;

namespace Syuas.Core.Services;

internal static class AsciiDocSyntax
{
    public static string SingleLine(string text, string label)
    {
        if (text.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException($"{label}は1行で入力してください。");
        return text;
    }

    public static string Attribute(string text) => "\"" + SingleLine(text, "属性")
        .Replace("&", "&amp;").Replace("\"", "&quot;").Replace("]", "&#93;") + "\"";

    public static string Label(string text) => SingleLine(text, "表示文字列").Replace("]", "\\]");

    public static string Id(string text)
    {
        if (!Regex.IsMatch(text, @"^[\p{L}_][\p{L}\p{N}_.:-]*$"))
            throw new ArgumentException("IDは文字または _ で始め、文字・数字・_・.・:・- を使用してください。");
        return text;
    }

    public static string Normalize(string text, string newLine) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newLine);

    public static string Delimiter(string text, char character)
    {
        var lines = Normalize(text, "\n").Split('\n').Select(l => l.TrimEnd()).ToHashSet();
        var delimiter = new string(character, 4);
        while (lines.Contains(delimiter)) delimiter += character;
        return delimiter;
    }
}
