using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocImageGenerator
{
    public static string Generate(ImageDefinition definition, string newLine = "\n")
    {
        var target = AsciiDocPathService.Resolve(definition.FilePath, definition.DocumentPath, definition.Relative);
        List<string> attributes = [AsciiDocSyntax.Attribute(definition.AltText)];
        foreach (var (name, value) in new[] { ("width", definition.Width), ("height", definition.Height) })
        {
            if (value.Length == 0) continue;
            if (!Regex.IsMatch(value, @"^[1-9]\d*(?:px|%)?$")) throw new ArgumentException($"{name}は正の整数、px、%で指定してください。");
            attributes.Add($"{name}={value}");
        }
        var prefix = "";
        if (definition.Id.Length > 0)
        {
            var id = AsciiDocSyntax.Id(definition.Id);
            if (definition.Inline) attributes.Add($"id={id}");
            else prefix = $"[#{id}]" + newLine;
        }
        if (definition.Title.Length > 0)
        {
            if (definition.Inline) attributes.Add("title=" + AsciiDocSyntax.Attribute(definition.Title));
            else prefix += "." + AsciiDocSyntax.SingleLine(definition.Title, "タイトル") + newLine;
        }
        return prefix + $"image{(definition.Inline ? ":" : "::")}{target}[{string.Join(",", attributes)}]";
    }
}
