using System.Globalization;
using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Reads the explicit, one-cell-per-line format emitted by AsciiDocTableGenerator.
// Does not expand directives, infer attributes, decode entities or repair malformed tables.
public static class AsciiDocTableParser
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private static readonly Regex Attributes = new("\\A\\[cols=\"(?<cols>[^\"]*)\",options=\"(?<header>header|noheader)\"\\]\\z", Options);
    private static readonly Regex Span = new(@"\A(?:(?<columns>[0-9]+)(?:\.(?<rows>[0-9]+))?|\.(?<rows>[0-9]+))\+\z", Options);
    private static readonly Regex Directive = new(@"\A(?:include|ifdef|ifndef|ifeval|endif)::", Options);

    public static TableParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string? newLine = null;
        var sourceLine = 1;
        var sourceColumn = 1;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r' && (i + 1 == source.Length || source[i + 1] != '\n'))
                return Fail(TableParseDiagnosticCode.InvalidNewLine, "単独のCR改行には対応していません。LFまたはCRLFで指定してください。", sourceLine, sourceColumn);
            if (source[i] == '\n')
            {
                var detected = i > 0 && source[i - 1] == '\r' ? "\r\n" : "\n";
                if (newLine is not null && newLine != detected)
                    return Fail(TableParseDiagnosticCode.InvalidNewLine, "表の中でLFとCRLFが混在しています。改行を統一してから読み戻してください。", sourceLine, sourceColumn);
                newLine = detected;
                sourceLine++; sourceColumn = 1;
            }
            else sourceColumn++;
        }
        newLine ??= "\n";
        var normalized = source.Replace("\r\n", "\n");
        var trailingNewLine = normalized.EndsWith('\n');
        if (trailingNewLine) normalized = normalized[..^1];
        var lines = normalized.Split('\n');
        var index = 0;
        var title = "";
        if (lines[index].StartsWith('.'))
        {
            title = lines[index][1..];
            if (title.Length == 0)
                return Fail(TableParseDiagnosticCode.NonCanonicalLayout, "空のタイトル行には対応していません。タイトル行を削除してください。", 1);
            index++;
        }
        if (index >= lines.Length)
            return Fail(TableParseDiagnosticCode.UnsupportedAttributes, "列数とheader／noheaderを明示した属性行が必要です。", lines.Length);
        var attributes = Attributes.Match(lines[index]);
        if (!attributes.Success)
            return Fail(TableParseDiagnosticCode.UnsupportedAttributes, "現在は [cols=\"列指定\",options=\"header または noheader\"] の形式に対応しています。他の属性は自動変換しません。", index + 1);

        var columnsText = attributes.Groups["cols"].Value;
        var widths = new List<string>();
        int columns;
        if (columnsText.EndsWith('*'))
        {
            if (!PositiveInteger(columnsText[..^1], out columns))
                return Fail(TableParseDiagnosticCode.InvalidColumns, "列数は正の整数で指定してください。", index + 1, 8);
            if (columns > TableDefinition.MaxColumns)
                return Fail(TableParseDiagnosticCode.TableTooLarge, $"列数の上限は{TableDefinition.MaxColumns}列です。", index + 1, 8);
            widths.AddRange(Enumerable.Repeat("", columns));
        }
        else
        {
            var values = columnsText.Split(',');
            if (values.Length > TableDefinition.MaxColumns)
                return Fail(TableParseDiagnosticCode.TableTooLarge, $"列数の上限は{TableDefinition.MaxColumns}列です。", index + 1, 8);
            if (values.Any(value => !PositiveInteger(value, out _)))
                return Fail(TableParseDiagnosticCode.InvalidColumns, "列幅は正の整数をカンマで区切って指定してください。配置やスタイル指定には対応していません。", index + 1, 8);
            widths.AddRange(values);
            columns = values.Length;
        }
        var hasHeader = attributes.Groups["header"].Value == "header";
        index++;
        if (index >= lines.Length || lines[index] != "|===")
            return Fail(TableParseDiagnosticCode.MissingOpeningDelimiter, "属性行の直後に表の開始区切り |=== が必要です。", Math.Min(index + 1, lines.Length));
        var opening = index;
        var closing = Array.IndexOf(lines, "|===", opening + 1);
        if (closing < 0)
            return Fail(TableParseDiagnosticCode.MissingClosingDelimiter, "表の終了区切り |=== が見つかりません。", lines.Length);
        if (closing != lines.Length - 1)
            return Fail(TableParseDiagnosticCode.UnexpectedContent, "表の後ろに別の内容があります。1つの表だけを指定してください。", closing + 2);

        var occupied = new bool[TableDefinition.MaxRows, columns];
        var tokens = new List<CellToken>();
        var cursor = 0;
        var rows = 0;
        for (index = opening + 1; index < closing; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf('|');
            var content = separator < 0 ? line : line[(separator + 1)..];
            if (Directive.IsMatch(content.TrimStart()))
                return Fail(TableParseDiagnosticCode.UnsupportedSyntax, "includeや条件分岐を含む表の読み戻しには対応していません。", index + 1, separator + 2);
            if (separator < 0)
            {
                if (tokens.Count == 0)
                    return Fail(TableParseDiagnosticCode.NonCanonicalLayout, "表の開始区切りの直後にセルが必要です。", index + 1);
                continue;
            }
            var columnSpan = 1;
            var rowSpan = 1;
            if (separator > 0)
            {
                var span = Span.Match(line[..separator]);
                if (!span.Success)
                    return Fail(TableParseDiagnosticCode.UnsupportedSyntax, "セルの開始には |、横結合 n+|、縦結合 .n+|、縦横結合 n.n+| を使用してください。", index + 1);
                if (span.Groups["columns"].Success && !PositiveInteger(span.Groups["columns"].Value, out columnSpan) ||
                    span.Groups["rows"].Success && !PositiveInteger(span.Groups["rows"].Value, out rowSpan))
                    return Fail(TableParseDiagnosticCode.InvalidSpan, "結合数は正の整数で指定してください。", index + 1);
            }
            if (content.Contains('|'))
                return Fail(TableParseDiagnosticCode.UnsupportedSyntax, "1行に複数のセルや生の | を含む形式には対応していません。セル本文の | は &#124; で表してください。", index + 1, separator + 2 + content.IndexOf('|'));
            while (cursor < occupied.Length && occupied[cursor / columns, cursor % columns]) cursor++;
            var row = cursor / columns;
            var column = cursor % columns;
            if (row >= TableDefinition.MaxRows || rowSpan > TableDefinition.MaxRows - row)
                return Fail(TableParseDiagnosticCode.TableTooLarge, $"行数の上限は{TableDefinition.MaxRows}行です。", index + 1);
            if (columnSpan > columns - column)
                return Fail(TableParseDiagnosticCode.InvalidSpan, "横結合が表の右端を超えています。", index + 1);
            if (hasHeader && row == 0 && rowSpan > 1)
                return Fail(TableParseDiagnosticCode.HeaderSpan, "ヘッダーと本文をまたぐ縦結合には対応していません。", index + 1);
            for (var r = row; r < row + rowSpan; r++)
                for (var c = column; c < column + columnSpan; c++)
                {
                    if (occupied[r, c]) return Fail(TableParseDiagnosticCode.OverlappingCells, "結合範囲が別のセルと重なっています。", index + 1);
                    occupied[r, c] = true;
                }
            rows = Math.Max(rows, row + rowSpan);
            tokens.Add(new(index, separator + 1, row, column, rowSpan, columnSpan));
        }
        if (tokens.Count == 0) return Fail(TableParseDiagnosticCode.EmptyTable, "表にセルがありません。", closing + 1);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
                if (!occupied[r, c]) return Fail(TableParseDiagnosticCode.MissingCells, $"{r + 1}行{c + 1}列のセルが不足しています。", closing + 1);

        var cells = new List<TableCellDefinition>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var end = i + 1 < tokens.Count ? tokens[i + 1].Line : closing;
            // Split removed the final separator newline. Between distinct anchor rows the
            // generator adds exactly one blank line; all remaining whitespace belongs to the cell.
            if (i + 1 < tokens.Count && tokens[i + 1].Row != token.Row)
            {
                if (end - 1 <= token.Line || lines[end - 1].Length != 0)
                    return Fail(TableParseDiagnosticCode.NonCanonicalLayout, "行の区切りに空行が必要です。セル本文の改行を推測して変換することはできません。", end + 1);
                end--;
            }
            var text = lines[token.Line][token.ContentColumn..];
            if (end > token.Line + 1) text += newLine + string.Join(newLine, lines, token.Line + 1, end - token.Line - 1);
            cells.Add(new(token.Row, token.Column, token.RowSpan, token.ColumnSpan, text));
        }
        var definition = TableDefinition.FromCells(rows, columns, cells, title, hasHeader, widths);
        var regenerated = AsciiDocTableGenerator.Generate(definition, newLine) + (trailingNewLine ? newLine : "");
        if (regenerated != source)
        {
            var offset = 0;
            while (offset < source.Length && offset < regenerated.Length && source[offset] == regenerated[offset]) offset++;
            var line = 1;
            var column = 1;
            for (var i = 0; i < offset; i++) { if (source[i] == '\n') { line++; column = 1; } else column++; }
            return Fail(TableParseDiagnosticCode.NonCanonicalLayout, "再生成するとソースの表記が変わるため、読み戻しを中止しました。現在のSYUASの生成形式に対応しています。", line, column);
        }
        return TableParseResult.Success(definition, newLine, trailingNewLine);
    }

    private static bool PositiveInteger(string value, out int number)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    private static TableParseResult Fail(TableParseDiagnosticCode code, string message, int line, int column = 1)
        => TableParseResult.Failure(code, message, line, column);
    private sealed record CellToken(int Line, int ContentColumn, int Row, int Column, int RowSpan, int ColumnSpan);
}
