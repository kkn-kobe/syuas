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
        if (definition.Tags.Length > 0)
        {
            if (definition.Lines.Length > 0 || definition.Tag.Length > 0)
                throw new ArgumentException("lines・tag・tagsはどれか1つだけ指定してください。");
            if (!Regex.IsMatch(definition.Tags, @"^!?[\p{L}\p{N}_.*:-]+(?:;!?[\p{L}\p{N}_.*:-]+)*$"))
                throw new ArgumentException("tagsは intro;usage または **;!internal のように ; で区切ってください。");
            options.Add("tags=\"" + definition.Tags + "\"");
        }
        if (definition.Indent.Length > 0)
        {
            if (!int.TryParse(definition.Indent, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var indent) || indent < 0)
                throw new ArgumentException("indentは0以上の整数で指定してください。");
            options.Add("indent=" + indent);
        }
        if (definition.Encoding.Length > 0)
        {
            if (!Regex.IsMatch(definition.Encoding, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$"))
                throw new ArgumentException("encodingはUTF-8などの文字コード名で指定してください。");
            options.Add("encoding=" + definition.Encoding);
        }
        if (definition.Optional) options.Add("opts=optional");
        return $"include::{target}[{string.Join(",", options)}]";
    }
}
