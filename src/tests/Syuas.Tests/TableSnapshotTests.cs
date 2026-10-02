using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class TableSnapshotTests
{
    [Fact]
    public void RestorePreservesInstanceAndExactDataAfterDestructiveEdits()
    {
        var table = new TableDefinition(4, 3) { Title = " 一覧 ", HasHeader = true };
        table.SetColumnWidth(0, "02");
        foreach (var cell in table.Cells) cell.Text = $" {cell.Row}:{cell.Column} 日本語😀\r\n&#124;\\|\n ";
        table.Merge(new(1, 0, 2, 2));
        var before = TableSnapshot.Capture(table);
        var reference = table;
        table.Unmerge(new(1, 0, 2, 2));
        table.Resize(1, 1);
        table.Title = "changed";
        table.HasHeader = false;
        table.Restore(before);
        Assert.Same(reference, table);
        Assert.True(before.ContentEquals(TableSnapshot.Capture(table)));
        Assert.Equal(2, table.CellAt(2, 1).RowSpan);
        Assert.Equal(2, table.CellAt(2, 1).ColumnSpan);
        table.Validate();
    }

    [Fact]
    public void SnapshotCollectionsAndRestoredCellsCannotMutateSavedState()
    {
        var table = new TableDefinition(1, 2);
        table.CellAt(0, 0).Text = "before";
        var saved = TableSnapshot.Capture(table);
        Assert.Throws<NotSupportedException>(() => ((IList<TableCellDefinition>)saved.Cells)[0] = new(0, 0, 1, 1, "bad"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)saved.ColumnWidths)[0] = "9");
        table.CellAt(0, 0).Text = "after";
        table.SetColumnWidth(0, "bad");
        Assert.Equal("before", saved.Cells[0].Text);
        Assert.Equal("", saved.ColumnWidths[0]);
        table.Restore(saved);
        table.CellAt(0, 0).Text = "changed again";
        table.Restore(saved);
        Assert.Equal("before", table.CellAt(0, 0).Text);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9999999999999999999999")]
    public void DraftMetadataCanBeRestoredWithoutWeakeningImportValidation(string width)
    {
        var table = new TableDefinition(1, 1) { Title = "編集中\nのタイトル" };
        table.SetColumnWidth(0, width);
        var saved = TableSnapshot.Capture(table);
        table.Title = "valid";
        table.SetColumnWidth(0, "2");
        table.Restore(saved);
        Assert.Equal("編集中\nのタイトル", table.Title);
        Assert.Equal(width, table.ColumnWidths[0]);
        Assert.Throws<ArgumentException>(() => AsciiDocTableGenerator.Generate(table));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(1, 1, saved.Cells, columnWidths: saved.ColumnWidths));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(1, 1, saved.Cells, title: saved.Title));
    }

    [Fact]
    public void ContentEqualityUsesCellsInCoordinateOrder()
    {
        var table = new TableDefinition(2, 2);
        var saved = TableSnapshot.Capture(table);
        table.Merge(new(0, 0, 2, 2));
        table.Unmerge(new(0, 0, 2, 2));
        Assert.True(saved.ContentEquals(TableSnapshot.Capture(table)));
        table.SetColumnWidth(0, "1"); // An explicit 1 and an empty input are distinct editor states.
        Assert.False(saved.ContentEquals(TableSnapshot.Capture(table)));
    }

    [Fact]
    public void DesignerSnapshotKeepsDraftFieldsAndSelectionButNavigationIsNotAnEdit()
    {
        var table = new TableDefinition(3, 3);
        var first = TableDesignerSnapshot.Capture(table, new(0, 0, 1, 1), 0, 0, "", "未確定");
        var moved = TableDesignerSnapshot.Capture(table, new(1, 1, 2, 2), 2, 2, "", "未確定");
        Assert.True(first.ContentEquals(moved));
        Assert.Equal(new TableSelection(1, 1, 2, 2), moved.Selection);
        Assert.Equal(2, moved.AnchorRow);
        Assert.Equal(2, moved.AnchorColumn);
        Assert.Equal("", moved.RowsInput);
        Assert.Equal("未確定", moved.ColumnsInput);
        Assert.False(first.ContentEquals(TableDesignerSnapshot.Capture(table, new(0, 0, 1, 1), 0, 0)));
    }

    [Fact]
    public void SelectionMustBeWithinTableAndCoverWholeMergedCells()
    {
        var table = new TableDefinition(3, 3);
        table.Merge(new(0, 0, 2, 2));
        Assert.Throws<ArgumentException>(() => TableDesignerSnapshot.Capture(table, new(0, 0, 1, 1), 0, 0));
        Assert.Throws<ArgumentException>(() => TableDesignerSnapshot.Capture(table, new(0, 0, int.MaxValue, 3), 0, 0));
        Assert.Throws<ArgumentException>(() => TableDesignerSnapshot.Capture(table, new(0, 0, 3, 3), -1, 0));
        Assert.Throws<ArgumentException>(() => TableDesignerSnapshot.Capture(table, new(2, 2, 1, 1), 0, 0));
        Assert.Throws<ArgumentException>(() => TableDesignerSnapshot.Capture(table, new(0, 0, 0, 3), 0, 0));
        var snapshot = TableDesignerSnapshot.Capture(table, new(0, 0, 2, 2), 1, 1);
        Assert.Equal(4, snapshot.Selection.RowCount * snapshot.Selection.ColumnCount);
    }

    [Fact]
    public void NullContentCannotBecomeAUsableSnapshot()
    {
        var table = new TableDefinition(1, 1);
        var valid = TableSnapshot.Capture(table);
        table.CellAt(0, 0).Text = null!;
        Assert.Throws<ArgumentException>(() => TableSnapshot.Capture(table));
        table.Restore(valid);
        table.SetColumnWidth(0, null!);
        Assert.Throws<ArgumentException>(() => TableSnapshot.Capture(table));
        Assert.Throws<ArgumentNullException>(() => TableSnapshot.Capture(null!));
        Assert.Throws<ArgumentNullException>(() => table.Restore(null!));
    }
}
