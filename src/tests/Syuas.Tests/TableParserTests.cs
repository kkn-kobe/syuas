using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class TableParserTests
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void ReadsTitleWidthsHeaderAndSourceText(string newLine, bool trailingNewLine)
    {
        var source = string.Join(newLine, new[]
        {
            ".項目一覧", "[cols=\"2,1,3\",options=\"header\"]", "|===",
            "|項目", "|内容", "|備考", "", "|日本語 😀", "|*重要*", "|xref:manual.adoc[参照]", "|==="
        }) + (trailingNewLine ? newLine : "");
        var result = AssertSuccess(source);
        var table = result.Definition!;
        Assert.Equal("項目一覧", table.Title);
        Assert.True(table.HasHeader);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(new[] { "2", "1", "3" }, table.ColumnWidths);
        Assert.Equal("日本語 😀", table.CellAt(1, 0).Text);
        Assert.Equal("*重要*", table.CellAt(1, 1).Text);
        Assert.Equal(newLine, result.NewLine);
        Assert.Equal(trailingNewLine, result.HasTrailingNewLine);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MultilineWhitespaceEmptyCellsAndEntitiesSurvive(string newLine)
    {
        var table = new TableDefinition(3, 2);
        var values = new[] { " \t先頭と末尾  ", "\n段落1\n\n段落2\n\n", "", "\n", "\t", "&#124; &#92; &amp; \\&#124;" };
        for (var i = 0; i < values.Length; i++) table.CellAt(i / 2, i % 2).Text = values[i];
        var result = AssertSuccess(AsciiDocTableGenerator.Generate(table, newLine));
        for (var i = 0; i < values.Length; i++)
            Assert.Equal(values[i].Replace("\n", newLine), result.Definition!.CellAt(i / 2, i % 2).Text);
    }

    [Fact]
    public void PipeEscapesRemainSourceRatherThanBeingHtmlDecoded()
    {
        var table = new TableDefinition(1, 2);
        table.CellAt(0, 0).Text = "A|B\n|===\nC\\|D";
        table.CellAt(0, 1).Text = "&#124; &#92; &amp;#124; \\&#124;";
        var parsed = AssertSuccess(AsciiDocTableGenerator.Generate(table)).Definition!;
        Assert.Equal("A&#124;B\n&#124;===\nC&#92;&#124;D", parsed.CellAt(0, 0).Text);
        Assert.Equal(table.CellAt(0, 1).Text, parsed.CellAt(0, 1).Text);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 1)]
    [InlineData(4, 3)]
    [InlineData(100, 50)]
    public void EntireTableSpanRestoresRowsWithoutAnyRowSeparators(int rows, int columns)
    {
        var table = new TableDefinition(rows, columns);
        table.Merge(new(0, 0, rows, columns));
        table.CellAt(0, 0).Text = "結合\n\n本文\n";
        var parsed = AssertSuccess(AsciiDocTableGenerator.Generate(table)).Definition!;
        Assert.Equal(rows, parsed.RowCount);
        Assert.Equal(columns, parsed.ColumnCount);
        Assert.Single(parsed.Cells);
        Assert.Same(parsed.CellAt(0, 0), parsed.CellAt(rows - 1, columns - 1));
    }

    [Fact]
    public void AdjacentSpansAndFullyCoveredMiddleRowHaveCorrectCoordinates()
    {
        var table = new TableDefinition(4, 4);
        table.Merge(new(0, 0, 2, 2));
        table.Merge(new(0, 2, 3, 2));
        table.Merge(new(2, 0, 2, 1));
        table.CellAt(0, 0).Text = "left\n\n";
        var parsed = AssertSuccess(AsciiDocTableGenerator.Generate(table)).Definition!;
        AssertStructure(table, parsed);
        Assert.Equal("left\n\n", parsed.CellAt(0, 0).Text);
    }

    [Fact]
    public void MaximumUnmergedTableHasAllFiveThousandCells()
    {
        var table = new TableDefinition(100, 50);
        table.CellAt(99, 49).Text = "last";
        var parsed = AssertSuccess(AsciiDocTableGenerator.Generate(table)).Definition!;
        Assert.Equal(5000, parsed.Cells.Count);
        Assert.Equal("last", parsed.CellAt(99, 49).Text);
    }

    [Fact]
    public void ParsedModelCanBeEditedWithExistingOperations()
    {
        var table = AssertSuccess("[cols=\"2*\",options=\"noheader\"]\n|===\n2.2+|original\n|===").Definition!;
        table.Unmerge(new(0, 0, 2, 2));
        table.InsertRow(1);
        table.InsertColumn(1);
        table.CellAt(2, 2).Text = "changed";
        table.Merge(new(0, 0, 1, 3));
        var reparsed = AssertSuccess(AsciiDocTableGenerator.Generate(table)).Definition!;
        Assert.Equal("original", reparsed.CellAt(0, 2).Text);
        Assert.Equal("changed", reparsed.CellAt(2, 2).Text);
        AssertStructure(table, reparsed);
    }

    [Fact]
    public void DeterministicVariedLayoutsRoundTripWithoutLosingSource()
    {
        var random = new Random(24103);
        var texts = new[] { "", "日本語 😀", "\n", "first\n\nlast\n", " | ", "C\\|D", "&#124;", " ", "*bold*", "\tend  " };
        for (var sample = 0; sample < 80; sample++)
        {
            var table = new TableDefinition(random.Next(1, 9), random.Next(1, 8)) { HasHeader = sample % 2 == 0, Title = sample % 3 == 0 ? "test" : "" };
            for (var i = 0; i < 12; i++)
            {
                var row = random.Next(table.RowCount);
                var column = random.Next(table.ColumnCount);
                try { table.Merge(new(row, column, random.Next(1, table.RowCount - row + 1), random.Next(1, table.ColumnCount - column + 1))); }
                catch (ArgumentException) { } // Partial intersections and header-crossing selections are rejected by the existing model.
            }
            foreach (var cell in table.Cells) cell.Text = texts[random.Next(texts.Length)];
            if (sample % 4 == 0) table.SetColumnWidth(0, "3");
            var parsed = AssertSuccess(AsciiDocTableGenerator.Generate(table, sample % 2 == 0 ? "\n" : "\r\n")).Definition!;
            AssertStructure(table, parsed);
        }
    }

    [Theory]
    [InlineData("", TableParseDiagnosticCode.UnsupportedAttributes)]
    [InlineData(".title", TableParseDiagnosticCode.UnsupportedAttributes)]
    [InlineData(".\n[cols=\"1*\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.NonCanonicalLayout)]
    [InlineData("[cols=\"1*\"]\n|===\n|a\n|===", TableParseDiagnosticCode.UnsupportedAttributes)]
    [InlineData("[cols=\"1*\",options=\"noheader\",frame=none]\n|===\n|a\n|===", TableParseDiagnosticCode.UnsupportedAttributes)]
    [InlineData("[format=csv]\n|===\na,b\n|===", TableParseDiagnosticCode.UnsupportedAttributes)]
    [InlineData("[cols=\"0*\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.InvalidColumns)]
    [InlineData("[cols=\"99999999999999999*\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.InvalidColumns)]
    [InlineData("[cols=\"51*\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.TableTooLarge)]
    [InlineData("[cols=\"1,0\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.InvalidColumns)]
    [InlineData("[cols=\"1a,2\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.InvalidColumns)]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\n|a\n|===", TableParseDiagnosticCode.MissingOpeningDelimiter)]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\n|===\n|a", TableParseDiagnosticCode.MissingClosingDelimiter)]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\n|===\n|===", TableParseDiagnosticCode.EmptyTable)]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\n|===\n|a\n|===\ntext", TableParseDiagnosticCode.UnexpectedContent)]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\r|===\r|a\r|===", TableParseDiagnosticCode.InvalidNewLine)]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\r\n|===\n|a\n|===", TableParseDiagnosticCode.InvalidNewLine)]
    public void InvalidEnvelopeReturnsDiagnosticWithoutPartialModel(string source, TableParseDiagnosticCode code)
        => AssertFailure(source, code);

    [Theory]
    [InlineData("a|nested AsciiDoc", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData(">|aligned", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("2*|duplicated", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("|one |two", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("|escaped \\| pipe", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("|include::file.adoc[]", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("|text\nifdef::flag[]", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("include::rows.adoc[]", TableParseDiagnosticCode.UnsupportedSyntax)]
    [InlineData("0+|bad", TableParseDiagnosticCode.InvalidSpan)]
    [InlineData(".0+|bad", TableParseDiagnosticCode.InvalidSpan)]
    [InlineData("999999999999999+|bad", TableParseDiagnosticCode.InvalidSpan)]
    [InlineData("2+|too wide", TableParseDiagnosticCode.InvalidSpan)]
    [InlineData(".101+|too tall", TableParseDiagnosticCode.TableTooLarge)]
    [InlineData("1+|redundant span", TableParseDiagnosticCode.NonCanonicalLayout)]
    [InlineData("|first\n|second", TableParseDiagnosticCode.NonCanonicalLayout)]
    [InlineData("\n|leading blank", TableParseDiagnosticCode.NonCanonicalLayout)]
    public void UnsupportedOrInvalidBodyIsNotRepaired(string body, TableParseDiagnosticCode code)
        => AssertFailure("[cols=\"1*\",options=\"noheader\"]\n|===\n" + body + "\n|===", code);

    [Fact]
    public void OverlapMissingCoverageAndHeaderSpanAreDiagnosed()
    {
        AssertFailure("[cols=\"3*\",options=\"noheader\"]\n|===\n|a\n.2+|b\n|c\n\n2+|overlap\n|===", TableParseDiagnosticCode.OverlappingCells);
        AssertFailure("[cols=\"2*\",options=\"noheader\"]\n|===\n.2+|a\n|b\n|===", TableParseDiagnosticCode.MissingCells);
        AssertFailure("[cols=\"1*\",options=\"header\"]\n|===\n.2+|header\n|===", TableParseDiagnosticCode.HeaderSpan);
    }

    [Fact]
    public void ExplicitRowAndColumnLimitsAreCheckedBeforeBuildingModel()
    {
        AssertFailure("[cols=\"" + string.Join(",", Enumerable.Repeat("1", 51)) + "\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.TableTooLarge);
        AssertFailure("[cols=\"1*\",options=\"noheader\"]\n|===\n" + string.Join("\n\n", Enumerable.Repeat("|a", 101)) + "\n|===", TableParseDiagnosticCode.TableTooLarge);
    }

    [Fact]
    public void DiagnosticsUseOneBasedSourceLocationsIncludingTitleAndCrLf()
    {
        var result = AsciiDocTableParser.Parse(".表\r\n[cols=\"1*\",options=\"noheader\"]\r\n|===\r\n|first\r\n\r\n|a|b\r\n|===");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(TableParseDiagnosticCode.UnsupportedSyntax, diagnostic.Code);
        Assert.Equal(6, diagnostic.Line);
        Assert.Equal(3, diagnostic.Column);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
    }

    [Theory]
    [InlineData("01*")]
    [InlineData("01")]
    public void NonCanonicalNumbersAreNotSilentlyNormalized(string columns)
        => AssertFailure($"[cols=\"{columns}\",options=\"noheader\"]\n|===\n|a\n|===", TableParseDiagnosticCode.NonCanonicalLayout);

    [Fact]
    public void IndependentParseResultsHaveIndependentMutableCells()
    {
        const string source = "[cols=\"1*\",options=\"noheader\"]\n|===\n|original\n|===";
        var first = AssertSuccess(source).Definition!;
        var second = AssertSuccess(source).Definition!;
        first.CellAt(0, 0).Text = "changed";
        Assert.Equal("original", second.CellAt(0, 0).Text);
        Assert.Equal(source, AsciiDocTableGenerator.Generate(second));
    }

    private static TableParseResult AssertSuccess(string source)
    {
        var result = AsciiDocTableParser.Parse(source);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Definition);
        Assert.NotNull(result.NewLine);
        result.Definition.Validate();
        Assert.Equal(source, AsciiDocTableGenerator.Generate(result.Definition, result.NewLine) + (result.HasTrailingNewLine ? result.NewLine : ""));
        return result;
    }

    private static void AssertFailure(string source, TableParseDiagnosticCode code)
    {
        var result = AsciiDocTableParser.Parse(source);
        Assert.False(result.Succeeded);
        Assert.Null(result.Definition);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
        Assert.InRange(result.Diagnostics[0].Line, 1, source.Count(c => c == '\n') + 1);
        Assert.True(result.Diagnostics[0].Column > 0);
    }

    private static void AssertStructure(TableDefinition expected, TableDefinition actual)
    {
        Assert.Equal(expected.RowCount, actual.RowCount);
        Assert.Equal(expected.ColumnCount, actual.ColumnCount);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.HasHeader, actual.HasHeader);
        var original = expected.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => (c.Row, c.Column, c.RowSpan, c.ColumnSpan));
        var parsed = actual.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => (c.Row, c.Column, c.RowSpan, c.ColumnSpan));
        Assert.Equal(original, parsed);
    }
}
