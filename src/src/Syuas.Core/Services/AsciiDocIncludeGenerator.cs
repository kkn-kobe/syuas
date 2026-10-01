using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocIncludeGenerator
{
    public static string Generate(IncludeDefinition definition)
    {
        var target = AsciiDocPathService.Resolve(definition.FilePath, definition.DocumentPath, definition.Relative);
        List<string> options = [];
        if (definition.Lines.Length > 0)
        {
            if (!Regex.IsMatch(definition.Lines, @"^[1-9]\d*(?:\.\.(?:[1-9]\d*|-1))?(?:[;,][1-9]\d*(?:\.\.(?:[1-9]\d*|-1))?)*$"))
                throw new ArgumentException("linesは 1..5 または 1;3;5..10 の形式で入力してください。");
            options.Add("lines=" + AsciiDocSyntax.Attribute(definition.Lines));
        }
        if (definition.Tag.Length > 0)
        {
            if (definition.Lines.Length > 0) throw new ArgumentException("linesとtagはどちらか一方を指定してください。");
            if (definition.Tag.IndexOfAny(['\r', '\n', '[', ']', '"']) >= 0)
                throw new ArgumentException("tagに改行・角括弧・ダブルクォートは使用できません。");
            options.Add("tag=\"" + definition.Tag + "\"");
        }
        if (definition.LevelOffset.Length > 0)
        {
            if (!Regex.IsMatch(definition.LevelOffset, @"^[+-]?\d+$")) throw new ArgumentException("leveloffsetは +1、-1、1 などの整数で入力してください。");
            options.Add("leveloffset=" + definition.LevelOffset);
        }
        return $"include::{target}[{string.Join(",", options)}]";
    }
}
