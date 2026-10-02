using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public static class AsciiDocTableGenerator
{
    public static string Generate(TableDefinition definition, string newLine = "\n")
    {
        definition.Validate();
        var widths = definition.ColumnWidths.Select(width =>
        {
            if (string.IsNullOrWhiteSpace(width)) return "1";
            if (!int.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                throw new ArgumentException("列幅は正の整数で入力してください。空欄は均等幅（1）です。");
            return number.ToString(CultureInfo.InvariantCulture);
        }).ToArray();
        var columns = definition.ColumnWidths.All(string.IsNullOrWhiteSpace) ? $"{definition.ColumnCount}*" : string.Join(",", widths);
        var output = new StringBuilder();
        if (definition.Title.Length > 0) output.Append('.').Append(AsciiDocSyntax.SingleLine(definition.Title, "表タイトル")).Append(newLine);
        // Explicit noheader also prevents implicit header detection for a single merged first row.
        output.Append("[cols=\"").Append(columns).Append("\",options=\"").Append(definition.HasHeader ? "header" : "noheader").Append("\"]").Append(newLine);
        output.Append("|===").Append(newLine);
        var rows = definition.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).GroupBy(c => c.Row);
        var first = true;
        foreach (var row in rows)
        {
            if (!first) output.Append(newLine);
            first = false;
            foreach (var cell in row)
            {
                if (cell.ColumnSpan > 1) output.Append(cell.ColumnSpan);
                if (cell.RowSpan > 1) output.Append('.').Append(cell.RowSpan);
                if (cell.RowSpan > 1 || cell.ColumnSpan > 1) output.Append('+');
                // Protect both the separator and preceding backslashes: a raw backslash would
                // escape the character reference instead of displaying the intended pipe.
                var text = Regex.Replace(AsciiDocSyntax.Normalize(cell.Text, newLine), @"(\\*)\|",
                    match => string.Concat(Enumerable.Repeat("&#92;", match.Groups[1].Length)) + "&#124;");
                output.Append('|').Append(text).Append(newLine);
            }
        }
        return output.Append("|===").ToString();
    }
}
