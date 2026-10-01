using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocBlockGenerator
{
    public static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant() switch
    {
        "c#" => "csharp", "c++" => "cpp", "js" => "javascript", "ts" => "typescript",
        "ps" => "powershell", var other => other
    };

    public static string Generate(SourceBlockDefinition definition, string newLine = "\n")
    {
        var language = NormalizeLanguage(definition.Language);
        if (!Regex.IsMatch(language, @"^[a-z0-9_+.#-]*$")) throw new ArgumentException("言語名には英数字・_・+・.・#・-を使用してください。");
        return $"[source{(language.Length == 0 ? "" : "," + language)}]" + newLine + Wrap(definition.Text, '-', newLine);
    }

    public static string Generate(BlockKind kind, string text, string newLine = "\n") => Wrap(text, kind switch
    {
        BlockKind.Listing => '-', BlockKind.Literal => '.', BlockKind.Quote => '_', BlockKind.Example => '=',
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    }, newLine);

    public static string Generate(AdmonitionDefinition definition, string newLine = "\n")
    {
        if (!Enum.IsDefined(definition.Kind)) throw new ArgumentException("Admonitionの種類が不正です。");
        var text = AsciiDocSyntax.Normalize(definition.Text, newLine);
        return definition.UseBlock || text.Contains(newLine)
            ? $"[{definition.Kind}]" + newLine + Wrap(text, '=', newLine)
            : $"{definition.Kind}: {text}";
    }

    private static string Wrap(string text, char character, string newLine)
    {
        text = AsciiDocSyntax.Normalize(text, newLine);
        var delimiter = AsciiDocSyntax.Delimiter(text, character);
        return delimiter + newLine + text + (text.EndsWith(newLine) && text.Length > 0 ? "" : newLine) + delimiter;
    }
}
