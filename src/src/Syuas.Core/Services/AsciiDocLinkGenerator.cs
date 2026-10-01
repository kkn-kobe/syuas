using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocLinkGenerator
{
    public static string Generate(LinkDefinition definition, bool crossReference = false)
    {
        var target = AsciiDocSyntax.SingleLine(definition.Target.Trim(), "Target");
        if (target.Length == 0) throw new ArgumentException("URLまたはTargetを入力してください。");
        target = target.Replace('\\', '/').Replace(" ", "%20").Replace("[", "%5B").Replace("]", "%5D");
        var label = AsciiDocSyntax.Label(definition.Text);
        if (!crossReference && label.Contains('=')) label = "\"" + label.Replace("\"", "\\\"") + "\"";
        return $"{(crossReference ? "xref" : "link")}:{target}[{label}]";
    }

    public static string GenerateAnchor(string id) => $"[[{AsciiDocSyntax.Id(id)}]]";
}
