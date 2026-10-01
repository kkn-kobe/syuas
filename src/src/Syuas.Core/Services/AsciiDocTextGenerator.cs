using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocTextGenerator
{
    public static string Marker(InlineFormat format, bool unconstrained = false)
    {
        var marker = format switch { InlineFormat.Bold => "*", InlineFormat.Italic => "_", InlineFormat.Monospace => "`", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
        return unconstrained ? marker + marker : marker;
    }

    public static string Format(string text, InlineFormat format, bool unconstrained = false)
    {
        var marker = Marker(format, unconstrained);
        return marker + text + marker;
    }

    public static string List(string text, ListKind kind, string newLine = "\n")
    {
        var marker = kind switch { ListKind.Unordered => "* ", ListKind.Ordered => ". ", ListKind.Checklist => "* [ ] ", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        return string.Join(newLine, AsciiDocSyntax.Normalize(text, "\n").Split('\n').Select(line =>
            line.Length == 0 && text.Length > 0 ? "" : marker + Regex.Replace(line, @"^\s*(?:\*|\.)\s+(?:\[[ xX*]\]\s+)?", "")));
    }
}
