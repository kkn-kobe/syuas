using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class TableLocatorTests
{
    internal const string Table = ".一覧\n[cols=\"2*\",options=\"noheader\"]\n|===\n|日本語 😀\n|B\n\n|C\n|D\n|===";

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EveryPositionInTableSelectsWholeTableWithoutSurroundingText(string nl)
    {
        var table = Table.Replace("\n", nl);
        var before = "= 文書" + nl + nl + "[[table-id]]" + nl;
        var after = nl + nl + "本文" + nl;
        var source = before + table + after;
        for (var offset = 0; offset <= table.Length; offset++)
        {
            var result = AsciiDocTableLocator.Locate(source, before.Length + offset);
            Assert.True(result.Succeeded, result.Diagnostic?.Message);
            Assert.Equal(before.Length, result.Range.StartOffset);
            Assert.Equal(table.Length, result.Range.Length);
            Assert.Equal(table, result.Range.OriginalSource);
            Assert.Equal(4, result.Range.StartLine);
            Assert.Equal(nl, result.Range.NewLine);
        }
        Assert.Equal(TableEditDiagnosticCode.NotInTable, AsciiDocTableLocator.Locate(source, 0).Diagnostic!.Code);
        Assert.Equal(TableEditDiagnosticCode.NotInTable, AsciiDocTableLocator.Locate(source, before.Length + table.Length + nl.Length).Diagnostic!.Code);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SelectionInsideOrWholeTableIncludingClosingLineBreakIsAllowed(string nl)
    {
        var table = Table.Replace("\n", nl);
        var source = table + nl + nl + "following";
        foreach (var (start, length) in new[] { (0, table.Length), (0, table.Length + nl.Length), (table.IndexOf("B", StringComparison.Ordinal), 1) })
        {
            var result = AsciiDocTableLocator.Locate(source, start, length);
            Assert.True(result.Succeeded, result.Diagnostic?.Message);
            Assert.Equal(table, result.Range.OriginalSource);
        }
        Assert.Equal(TableEditDiagnosticCode.SelectionCrossesBoundary, AsciiDocTableLocator.Locate(source, 0, table.Length + nl.Length * 2).Diagnostic!.Code);
    }

    [Fact]
    public void MultipleTablesAreIndependentAndCrossingSelectionsAreRejected()
    {
        var second = Table.Replace("一覧", "別表", StringComparison.Ordinal);
        var source = "prefix\n\n" + Table + "\n\n" + second;
        var start = source.IndexOf(second, StringComparison.Ordinal);
        Assert.Equal(second, AsciiDocTableLocator.Locate(source, start).Range!.OriginalSource);
        Assert.Equal(Table, AsciiDocTableLocator.Locate(source, 8).Range!.OriginalSource);
        Assert.Equal(TableEditDiagnosticCode.SelectionCrossesBoundary, AsciiDocTableLocator.Locate(source, 8, source.Length - 8).Diagnostic!.Code);
        Assert.Equal(TableEditDiagnosticCode.SelectionCrossesBoundary, AsciiDocTableLocator.Locate(source, 0, 10).Diagnostic!.Code);
    }

    [Theory]
    [InlineData("----", "----")]
    [InlineData("-----", "-----")]
    [InlineData("....", "....")]
    [InlineData("////", "////")]
    [InlineData("++++", "++++")]
    [InlineData("====", "====")]
    [InlineData("____", "____")]
    [InlineData("****", "****")]
    [InlineData("--", "--")]
    [InlineData("```asciidoc", "```")]
    [InlineData("````asciidoc", "`````")]
    [InlineData("~~~", "~~~")]
    [InlineData("!===", "!===")]
    [InlineData("|====", "|====")]
    public void TablesInsideExcludedBlocksAreIgnoredButFollowingTableIsFound(string open, string close)
    {
        var source = open + "\n" + Table + "\n" + close + "\n\n" + Table;
        var ignored = AsciiDocTableLocator.Locate(source, open.Length + 1 + Table.IndexOf("日本語", StringComparison.Ordinal));
        Assert.False(ignored.Succeeded);
        Assert.Equal(TableEditDiagnosticCode.NotInTable, ignored.Diagnostic!.Code);
        var following = AsciiDocTableLocator.Locate(source, source.LastIndexOf("日本語", StringComparison.Ordinal));
        Assert.True(following.Succeeded, following.Diagnostic?.Message);
        Assert.Equal(source.LastIndexOf(".一覧", StringComparison.Ordinal), following.Range.StartOffset);
    }

    [Fact]
    public void CompoundBlockCannotBeClosedByDelimiterInsideVerbatimBlock()
    {
        var source = "====\n----\n====\n" + Table + "\n----\n====\n\n" + Table;
        Assert.False(AsciiDocTableLocator.Locate(source, source.IndexOf("日本語", StringComparison.Ordinal)).Succeeded);
        Assert.True(AsciiDocTableLocator.Locate(source, source.LastIndexOf("日本語", StringComparison.Ordinal)).Succeeded);
    }

    [Theory]
    [InlineData("ifdef::feature[]\n", "\nendif::[]")]
    [InlineData("ifndef::feature[]\nifdef::nested[]\n", "\nendif::[]\nendif::[]")]
    [InlineData("ifeval::[1 == 1]\n", "\nendif::[]")]
    [InlineData(" indented literal\n", "")]
    public void ConditionalSectionsAndIndentedLiteralParagraphsAreExcluded(string prefix, string suffix)
    {
        var source = prefix + Table + suffix + "\n\n" + Table;
        Assert.False(AsciiDocTableLocator.Locate(source, prefix.Length + 2).Succeeded);
        Assert.True(AsciiDocTableLocator.Locate(source, source.LastIndexOf("日本語", StringComparison.Ordinal)).Succeeded);
    }

    [Fact]
    public void LineCommentsContainingTableDelimitersAreNotTables()
    {
        var source = "// |===\n// |text\n// |===\n\n" + Table;
        Assert.False(AsciiDocTableLocator.Locate(source, 3).Succeeded);
        Assert.True(AsciiDocTableLocator.Locate(source, source.LastIndexOf("日本語", StringComparison.Ordinal)).Succeeded);
    }

    [Theory]
    [InlineData("[[table-id]]\n")]
    [InlineData("[#table-id]\n")]
    [InlineData("// table note\n[[table-id,表への参照]]\n// another note\n")]
    public void IndependentAnchorsAndCommentsRemainOutsideRange(string prefix)
    {
        var result = AsciiDocTableLocator.Locate(prefix + Table, prefix.Length);
        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(prefix.Length, result.Range.StartOffset);
        Assert.Equal(Table, result.Range.OriginalSource);
        Assert.False(AsciiDocTableLocator.Locate(prefix + Table, 0).Succeeded);
    }

    [Theory]
    [InlineData("[source]\n")]
    [InlineData("[role=custom]\n// attached comment\n")]
    [InlineData(".extra title\n")]
    public void ExtraAttachedMetadataIsNotSilentlyIgnored(string prefix)
    {
        var result = AsciiDocTableLocator.Locate(prefix + Table, prefix.Length + Table.IndexOf("日本語", StringComparison.Ordinal));
        Assert.False(result.Succeeded);
        Assert.Equal(TableEditDiagnosticCode.UnsupportedContext, result.Diagnostic!.Code);
    }

    [Fact]
    public void AnchorBetweenTitleAndAttributesIsRejectedAsAmbiguous()
    {
        var source = Table.Replace(".一覧\n", ".一覧\n[[id]]\n", StringComparison.Ordinal);
        var result = AsciiDocTableLocator.Locate(source, source.IndexOf("日本語", StringComparison.Ordinal));
        Assert.Equal(TableEditDiagnosticCode.UnsupportedContext, result.Diagnostic!.Code);
    }

    [Fact]
    public void UnclosedTableDoesNotConsumeNextDeclaredTable()
    {
        var broken = Table[..^4];
        var source = broken + "\n" + Table;
        var missing = AsciiDocTableLocator.Locate(source, source.IndexOf("日本語", StringComparison.Ordinal));
        Assert.Equal(TableEditDiagnosticCode.MissingClosingDelimiter, missing.Diagnostic!.Code);
        var nextStart = broken.Length + 1;
        var next = AsciiDocTableLocator.Locate(source, nextStart);
        Assert.True(next.Succeeded, next.Diagnostic?.Message);
        Assert.Equal(nextStart, next.Range.StartOffset);
        Assert.Equal(Table, next.Range.OriginalSource);
    }

    [Fact]
    public void MissingClosingDelimiterAndOrphanCellsReturnDiagnostic()
    {
        var missing = AsciiDocTableLocator.Locate(Table[..^4], 0);
        Assert.Equal(TableEditDiagnosticCode.MissingClosingDelimiter, missing.Diagnostic!.Code);
        var orphan = AsciiDocTableLocator.Locate(Table + "\n\n|another cell\n|===", 0);
        Assert.Equal(TableEditDiagnosticCode.AmbiguousBoundary, orphan.Diagnostic!.Code);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(0, int.MaxValue)]
    public void InvalidSelectionDoesNotOverflowOrThrow(int start, int length)
        => Assert.Equal(TableEditDiagnosticCode.InvalidSelection, AsciiDocTableLocator.Locate(Table, start, length).Diagnostic!.Code);

    [Fact]
    public void EmptyDocumentAndCaretAfterTableDoNotGuessNearestTable()
    {
        Assert.Equal(TableEditDiagnosticCode.NotInTable, AsciiDocTableLocator.Locate("", 0).Diagnostic!.Code);
        Assert.True(AsciiDocTableLocator.Locate(Table, Table.Length).Succeeded);
        Assert.Equal(TableEditDiagnosticCode.NotInTable, AsciiDocTableLocator.Locate(Table + "\n", Table.Length + 1).Diagnostic!.Code);
    }

    [Fact]
    public void TitleStartingWithDotIsStillPartOfTable()
    {
        var source = Table.Replace(".一覧", "..拡張子", StringComparison.Ordinal);
        var result = AsciiDocTableLocator.Locate(source, source.IndexOf("日本語", StringComparison.Ordinal));
        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(source, result.Range.OriginalSource);
        Assert.Equal(0, result.Range.StartOffset);
    }

    [Theory]
    [InlineData("[source]\nsource paragraph\n")]
    [InlineData("[literal]\nliteral paragraph\n")]
    public void UncertainParagraphAndListContextsAreRejected(string prefix)
    {
        var result = AsciiDocTableLocator.Locate(prefix + Table, prefix.Length);
        Assert.False(result.Succeeded);
        Assert.Equal(TableEditDiagnosticCode.UnsupportedContext, result.Diagnostic!.Code);
        Assert.True(AsciiDocTableLocator.Locate(prefix + "\n" + Table, prefix.Length + 1).Succeeded);
    }

    [Fact]
    public void AnchorDoesNotHideUncertainParagraphBoundary()
    {
        const string prefix = "[source]\ncode paragraph\n[[id]]\n";
        var result = AsciiDocTableLocator.Locate(prefix + Table, prefix.Length);
        Assert.Equal(TableEditDiagnosticCode.UnsupportedContext, result.Diagnostic!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    public void ListContinuationRemainsExcludedAcrossBlankLine(string gap)
    {
        var prefix = "* item\n+\n" + gap;
        Assert.Equal(TableEditDiagnosticCode.UnsupportedContext,
            AsciiDocTableLocator.Locate(prefix + Table, prefix.Length).Diagnostic!.Code);
    }
}
