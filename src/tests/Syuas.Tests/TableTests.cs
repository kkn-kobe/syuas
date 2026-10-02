using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class TableTests
{
    [Fact]
    public void TwoByTwoTableHasExactlyFourCells()
    {
        var table = new TableDefinition(2, 2);
        Assert.Equal("[cols=\"2*\",options=\"noheader\"]\n|===\n|\n|\n\n|\n|\n|===", AsciiDocTableGenerator.Generate(table));
        AssertCoverage(table);
    }

    [Fact]
    public void HeaderTitleAndColumnWidthsAreExplicit()
    {
        var table = new TableDefinition(2, 3) { HasHeader = true, Title = "項目一覧" };
        table.SetColumnWidth(0, "2"); table.SetColumnWidth(2, "3");
        table.CellAt(0, 0).Text = "項目";
        Assert.StartsWith(".項目一覧\r\n[cols=\"2,1,3\",options=\"header\"]\r\n|===\r\n|項目\r\n", AsciiDocTableGenerator.Generate(table, "\r\n"));
    }

    [Theory]
    [InlineData(1, 2, "2+|結合")]
    [InlineData(3, 1, ".3+|結合")]
    [InlineData(3, 2, "2.3+|結合")]
    public void SpanNotationIsCalculatedFromRectangle(int rows, int columns, string expected)
    {
        var table = new TableDefinition(4, 4);
        table.CellAt(1, 1).Text = "結合";
        table.Merge(new(1, 1, rows, columns));
        Assert.Contains(expected, AsciiDocTableGenerator.Generate(table));
        Assert.Equal(16 - rows * columns + 1, table.Cells.Count);
        Assert.Same(table.CellAt(1, 1), table.CellAt(rows, columns));
        AssertCoverage(table);
    }

    [Fact]
    public void MergeKeepsTextInReadingOrderAndUnmergeKeepsItAtTopLeft()
    {
        var table = new TableDefinition(2, 2);
        table.CellAt(0, 0).Text = "A"; table.CellAt(0, 1).Text = "B";
        table.CellAt(1, 0).Text = "C"; table.CellAt(1, 1).Text = "D";
        table.Merge(new(0, 0, 2, 2));
        Assert.Equal("A\nB\nC\nD", table.CellAt(1, 1).Text);
        table.Unmerge(new(0, 0, 2, 2));
        Assert.Equal("A\nB\nC\nD", table.CellAt(0, 0).Text);
        Assert.All(table.Cells.Where(c => c.Row != 0 || c.Column != 0), c => Assert.Empty(c.Text));
        AssertCoverage(table);
    }

    [Fact]
    public void PartialMergeAndHeaderCrossingDoNotChangeModel()
    {
        var table = new TableDefinition(3, 3);
        table.Merge(new(0, 0, 2, 2));
        var before = AsciiDocTableGenerator.Generate(table);
        Assert.Throws<ArgumentException>(() => table.Merge(new(1, 1, 2, 2)));
        Assert.Throws<ArgumentException>(() => table.HasHeader = true);
        Assert.Equal(before, AsciiDocTableGenerator.Generate(table));
        table.Unmerge(new(0, 0, 2, 2));
        table.HasHeader = true;
        Assert.Throws<ArgumentException>(() => table.Merge(new(0, 0, 2, 2)));
        table.Merge(new(0, 0, 1, 3));
        Assert.Contains("3+|", AsciiDocTableGenerator.Generate(table));
        AssertCoverage(table);
    }

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 1, 2)]
    [InlineData(4, 1, 2)]
    public void InsertRowAdjustsMergedCellAtEachBoundary(int index, int row, int span)
    {
        var table = new TableDefinition(4, 3);
        table.Merge(new(1, 0, 2, 2)); table.CellAt(1, 0).Text = "kept";
        table.InsertRow(index);
        var merged = Assert.Single(table.Cells, c => c.Text == "kept");
        Assert.Equal(row, merged.Row); Assert.Equal(span, merged.RowSpan);
        AssertCoverage(table);
    }

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 1, 2)]
    [InlineData(4, 1, 2)]
    public void InsertColumnAdjustsMergedCellAtEachBoundary(int index, int column, int span)
    {
        var table = new TableDefinition(3, 4);
        table.Merge(new(0, 1, 2, 2)); table.CellAt(0, 1).Text = "kept";
        table.SetColumnWidth(1, "7");
        table.InsertColumn(index);
        var merged = Assert.Single(table.Cells, c => c.Text == "kept");
        Assert.Equal(column, merged.Column); Assert.Equal(span, merged.ColumnSpan);
        Assert.Equal("7", table.ColumnWidths[index <= 1 ? 2 : 1]);
        AssertCoverage(table);
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 2)]
    public void DeleteRowKeepsSurvivingMergedTextIncludingDeletedAnchor(int index, int row, int span)
    {
        var table = new TableDefinition(4, 3);
        table.Merge(new(1, 0, 2, 2)); table.CellAt(1, 0).Text = "kept";
        table.DeleteRow(index);
        var merged = Assert.Single(table.Cells, c => c.Text == "kept");
        Assert.Equal(row, merged.Row); Assert.Equal(span, merged.RowSpan);
        AssertCoverage(table);
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 2)]
    public void DeleteColumnKeepsSurvivingMergedTextIncludingDeletedAnchor(int index, int column, int span)
    {
        var table = new TableDefinition(3, 4);
        table.Merge(new(0, 1, 2, 2)); table.CellAt(0, 1).Text = "kept";
        table.DeleteColumn(index);
        var merged = Assert.Single(table.Cells, c => c.Text == "kept");
        Assert.Equal(column, merged.Column); Assert.Equal(span, merged.ColumnSpan);
        AssertCoverage(table);
    }

    [Fact]
    public void DeletingHeaderRejectsInvalidPromotionWithoutPartialMutation()
    {
        var table = new TableDefinition(4, 2) { HasHeader = true };
        table.Merge(new(1, 0, 2, 1));
        var before = AsciiDocTableGenerator.Generate(table);
        Assert.Throws<ArgumentException>(() => table.DeleteRow(0));
        Assert.Equal(before, AsciiDocTableGenerator.Generate(table));
    }

    [Fact]
    public void ResizePreservesContentsAndShrinksSpansAtRightAndBottom()
    {
        var table = new TableDefinition(4, 4);
        table.Merge(new(1, 1, 3, 3)); table.CellAt(1, 1).Text = "kept";
        table.Resize(2, 2);
        Assert.Equal("kept", table.CellAt(1, 1).Text);
        Assert.Equal(1, table.CellAt(1, 1).RowSpan); Assert.Equal(1, table.CellAt(1, 1).ColumnSpan);
        table.Resize(5, 5);
        Assert.Equal("kept", table.CellAt(1, 1).Text);
        Assert.Equal(25, table.Cells.Count);
        AssertCoverage(table);
    }

    [Fact]
    public void InvalidSizesAndWidthsDoNotProduceBrokenTables()
    {
        var table = new TableDefinition(1, 1);
        Assert.Throws<ArgumentException>(() => table.Resize(0, 2));
        Assert.Throws<ArgumentException>(() => table.Resize(101, 2));
        Assert.Throws<ArgumentException>(() => table.Resize(2, 51));
        Assert.Throws<ArgumentException>(() => table.DeleteRow(0));
        Assert.Throws<ArgumentException>(() => table.DeleteColumn(0));
        Assert.Equal(1, table.RowCount); Assert.Equal(1, table.ColumnCount);
        foreach (var width in new[] { "0", "-1", "1,2", "x", "999999999999999999999" })
        {
            table.SetColumnWidth(0, width);
            Assert.Throws<ArgumentException>(() => AsciiDocTableGenerator.Generate(table));
        }
    }

    [Fact]
    public void LargeTableKeepsAllCellsAndValidCoverage()
    {
        var table = new TableDefinition(100, 50);
        table.CellAt(99, 49).Text = "last";
        var text = AsciiDocTableGenerator.Generate(table);
        Assert.StartsWith("[cols=\"50*\"", text);
        Assert.Equal(5000, text.Split('\n').Count(l => l.StartsWith('|') && l != "|==="));
        Assert.EndsWith("|last\n|===", text);
        table.Validate();
    }

    [Fact]
    public void MultilineAndPipeCharactersCannotCreateExtraCellsOrCloseTable()
    {
        var table = new TableDefinition(1, 2);
        table.CellAt(0, 0).Text = "A|B\r\n|===\nC\\|D";
        var text = AsciiDocTableGenerator.Generate(table);
        Assert.Contains("|A&#124;B\n&#124;===\nC&#92;&#124;D", text);
        Assert.Equal(2, text.Split('\n').Count(l => l == "|==="));
    }

    [Fact]
    public void DesignerSelectionExpandsAcrossExistingMergedCells()
    {
        var table = new TableDefinition(4, 4);
        table.Merge(new(1, 1, 2, 2));
        var vm = new TableDesignerViewModel(definition: table);
        vm.SelectCell(0, 0); vm.SelectCell(1, 3, true);
        Assert.Equal(new TableSelection(0, 0, 3, 4), vm.Selection);
        vm.MergeCommand.Execute(null);
        Assert.Equal(3, table.CellAt(0, 0).RowSpan); Assert.Equal(4, table.CellAt(0, 0).ColumnSpan);
        AssertCoverage(table);
    }

    [Fact]
    public void DesignerPreviewValidationAndCommandsFollowEdits()
    {
        var vm = new TableDesignerViewModel();
        Assert.Equal(3, vm.Definition.RowCount); Assert.Equal(3, vm.Definition.ColumnCount); Assert.False(vm.HasHeader);
        vm.Cells[0].Text = "本文";
        Assert.Contains("|本文", vm.Preview);
        vm.ColumnWidths[0].Value = "bad";
        Assert.False(vm.InsertCommand.CanExecute(null)); Assert.NotEmpty(vm.Error);
        vm.ColumnWidths[0].Value = "2";
        Assert.True(vm.InsertCommand.CanExecute(null)); Assert.Contains("cols=\"2,1,1\"", vm.Preview);
        vm.RowsInput = "4";
        Assert.False(vm.InsertCommand.CanExecute(null));
        vm.ResizeCommand.Execute(null);
        Assert.Equal(4, vm.Definition.RowCount); Assert.True(vm.InsertCommand.CanExecute(null));
        vm.SelectCell(0, 0); vm.SelectCell(1, 1, true); vm.MergeCommand.Execute(null);
        Assert.True(vm.UnmergeCommand.CanExecute(null));
        vm.HasHeader = true;
        Assert.False(vm.HasHeader); Assert.NotEmpty(vm.Error);
        vm.UnmergeCommand.Execute(null); vm.HasHeader = true;
        Assert.True(vm.HasHeader); Assert.Empty(vm.Error);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void InsertedTableIsOneUndoAndCaretStartsInFirstCell(string nl) => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("before" + nl + nl + "after"); editor.Select(6 + 2 * nl.Length, 0);
        var original = editor.Text;
        var vm = new InputAssistanceViewModel(editor, () => null, new TableDialogs());
        vm.Insert(AssistanceKind.Table);
        Assert.Contains("2.2+|", editor.Text);
        Assert.Equal(nl, editor.Text.Substring(editor.SelectionStart, nl.Length));
        editor.Undo(); Assert.Equal(original, editor.Text); Assert.False(editor.CanUndo); Assert.False(editor.IsModified);
        editor.Redo(); Assert.Contains("2.2+|", editor.Text);
    });

    [Fact]
    public void CancelledTableDoesNotChangeSelectedText() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load("selected"); editor.Select(0, 8);
        new InputAssistanceViewModel(editor, () => null, new TableDialogs { Cancel = true }).Insert(AssistanceKind.Table);
        Assert.Equal("selected", editor.Text); Assert.Equal(8, editor.SelectionLength); Assert.False(editor.CanUndo);
    });

    [Fact]
    public void MixedOperationsMaintainCoverage()
    {
        var random = new Random(3103);
        var table = new TableDefinition(8, 8);
        for (var i = 0; i < 300; i++)
        {
            var row = random.Next(table.RowCount); var col = random.Next(table.ColumnCount);
            switch (random.Next(6))
            {
                case 0: table.InsertRow(row); break;
                case 1: table.InsertColumn(col); break;
                case 2 when table.RowCount > 1: table.DeleteRow(row); break;
                case 3 when table.ColumnCount > 1: table.DeleteColumn(col); break;
                case 4: table.Unmerge(new(row, col, 1, 1)); break;
                case 5:
                    var selection = new TableSelection(row, col, random.Next(1, table.RowCount - row + 1), random.Next(1, table.ColumnCount - col + 1));
                    if (table.Cells.Where(selection.Intersects).All(selection.Contains)) table.Merge(selection);
                    break;
            }
            AssertCoverage(table);
        }
    }

    [Fact]
    public void ExportProcessorFixturesWhenRequested()
    {
        if (Environment.GetEnvironmentVariable("SYUAS_TABLE_FIXTURES") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var table = new TableDefinition(4, 4) { HasHeader = true, Title = "結合テスト" };
        foreach (var cell in table.Cells) cell.Text = $"R{cell.Row + 1}C{cell.Column + 1}";
        table.Merge(new(1, 1, 3, 2));
        table.CellAt(1, 1).Text = "A|B\nC\\|D\n|===";
        File.WriteAllText(Path.Combine(directory, "combined.adoc"), AsciiDocTableGenerator.Generate(table));
        var full = new TableDefinition(3, 2);
        full.Merge(new(0, 0, 3, 2)); full.CellAt(0, 0).Text = "Full";
        File.WriteAllText(Path.Combine(directory, "full.adoc"), AsciiDocTableGenerator.Generate(full));
    }

    private static void AssertCoverage(TableDefinition table)
    {
        table.Validate();
        Assert.Equal(table.RowCount * table.ColumnCount, table.Cells.Sum(c => c.RowSpan * c.ColumnSpan));
        Assert.Equal(table.ColumnCount, table.ColumnWidths.Count);
    }

    private sealed class TableDialogs : IInputAssistanceDialogs
    {
        public bool Cancel { get; init; }
        public InsertionSnippet? Show(AssistanceKind kind, InsertionContext context)
        {
            Assert.Equal(AssistanceKind.Table, kind);
            var vm = new TableDesignerViewModel(context.NewLine);
            vm.SelectCell(0, 0); vm.SelectCell(1, 1, true); vm.MergeCommand.Execute(null);
            return Cancel ? null : vm.Snippet;
        }
    }
}
