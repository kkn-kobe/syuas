using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocHeadingGenerator
{
    public static string StripMarker(string title) => Regex.Replace(title, @"^={1,6}\s+", "");

    public static string Generate(HeadingDefinition definition)
    {
        if (definition.Level is < 1 or > 5) throw new ArgumentException("見出しレベルは1〜5を選択してください。");
        var title = StripMarker(AsciiDocSyntax.SingleLine(definition.Title, "見出し"));
        return new string('=', definition.Level + 1) + " " + title;
    }
}
