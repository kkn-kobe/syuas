using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

// Hand-authored expectations, independent of the parser and generator.
internal static class TableCompatibilityFixtures
{
    internal sealed record Cell(int Row, int Column, int RowSpan, int ColumnSpan, string Source, string Text);
    internal sealed record Example(string Name, int Rows, int Columns, bool Header, string Title, int[] Widths, Cell[] Cells);
    internal static readonly Example[] All =
    [
        new("basic", 2, 2, false, "", [1, 1],
            [new(0, 0, 1, 1, "日本語 😀", "日本語 😀"), new(0, 1, 1, 1, "B", "B"), new(1, 0, 1, 1, "C", "C"), new(1, 1, 1, 1, "", "")]),
        new("header", 3, 3, true, "一覧", [2, 1, 1],
            [new(0, 0, 1, 1, "項目", "項目"), new(0, 1, 1, 1, "説明", "説明"), new(0, 2, 1, 1, "備考", "備考"),
             new(1, 0, 2, 1, "共通", "共通"), new(1, 1, 1, 2, "横結合", "横結合"), new(2, 1, 1, 1, "左", "左"), new(2, 2, 1, 1, "右", "右")]),
        new("spans", 3, 3, false, "", [1, 1, 1],
            [new(0, 0, 2, 2, "複合結合", "複合結合"), new(0, 2, 1, 1, "右上", "右上"), new(1, 2, 1, 1, "右下", "右下"), new(2, 0, 1, 3, "最終行", "最終行")]),
        new("full-span", 3, 2, false, "", [1, 1], [new(0, 0, 3, 2, "全体結合", "全体結合")]),
        new("entities", 2, 2, false, "", [1, 1],
            [new(0, 0, 1, 1, "A&#124;B", "A|B"), new(0, 1, 1, 1, "&#92;&#124;", "\\|"),
             new(1, 0, 1, 1, "*太字* と `code`", "太字 と code"), new(1, 1, 1, 1, "一段落\n続き\n\n二段落 <tag> & text", "一段落 続き 二段落 <tag> & text")]),
        new("empty", 2, 2, false, "", [1, 1],
            [new(0, 0, 1, 1, "", ""), new(0, 1, 1, 1, "", ""), new(1, 0, 1, 1, "", ""), new(1, 1, 1, 1, "", "")])
    ];

    internal static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TableFixtures", name + ".adoc")).Replace("\r\n", "\n");

    internal static TableDefinition ParseAndVerify(Example example, string source)
    {
        var parsed = AsciiDocTableParser.Parse(source);
        Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));
        var table = parsed.Definition;
        Assert.Equal(example.Rows, table.RowCount);
        Assert.Equal(example.Columns, table.ColumnCount);
        Assert.Equal(example.Header, table.HasHeader);
        Assert.Equal(example.Title, table.Title);
        Assert.Equal(example.Cells.Select(c => (c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.Source)),
            table.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => (c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.Text.Replace("\r\n", "\n"))));
        Assert.Equal(source, AsciiDocTableGenerator.Generate(table, parsed.NewLine) + (parsed.HasTrailingNewLine ? parsed.NewLine : ""));
        return table;
    }
}
