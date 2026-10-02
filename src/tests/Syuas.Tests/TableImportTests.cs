using Syuas.Core.Models;

namespace Syuas.Tests;

public sealed class TableImportTests
{
    [Fact]
    public void ImportCopiesInputsWithoutMergeSideEffects()
    {
        var cells = new List<TableCellDefinition>
        {
            new(0, 1, 1, 1, "right"), new(0, 0, 2, 1, "\n left \n"), new(1, 1, 1, 1, "bottom")
        };
        var widths = new[] { "2", "" };
        var table = TableDefinition.FromCells(2, 2, cells, "title", columnWidths: widths);
        cells.Clear(); widths[0] = "99";
        Assert.Equal("2", table.ColumnWidths[0]);
        Assert.Equal(3, table.Cells.Count);
        Assert.Equal("\n left \n", table.CellAt(1, 0).Text);
        Assert.Equal("right", table.CellAt(0, 1).Text);
        Assert.Equal("bottom", table.CellAt(1, 1).Text);
        table.Validate();
    }

    public static IEnumerable<object[]> InvalidCells()
    {
        yield return new object[] { Array.Empty<TableCellDefinition>() };
        yield return new object[] { new[] { new TableCellDefinition(0, 0, 1, 1, "missing") } };
        yield return new object[] { new[] { new TableCellDefinition(0, 0, 2, 2, "all"), new TableCellDefinition(0, 1, 1, 1, "overlap") } };
        yield return new object[] { new[] { new TableCellDefinition(-1, 0, 1, 1, "negative") } };
        yield return new object[] { new[] { new TableCellDefinition(0, 0, 0, 1, "zero") } };
        yield return new object[] { new[] { new TableCellDefinition(0, 0, 1, -1, "negative span") } };
        yield return new object[] { new[] { new TableCellDefinition(0, 0, int.MaxValue, int.MaxValue, "overflow") } };
        yield return new object[] { new[] { new TableCellDefinition(int.MaxValue, int.MaxValue, 1, 1, "outside") } };
        yield return new object[] { new[] { new TableCellDefinition(0, 0, 2, 2, null!) } };
        yield return new object[] { new TableCellDefinition[] { null! } };
        yield return new object[] { Enumerable.Repeat(new TableCellDefinition(0, 0, 1, 1, "too many"), 5).ToArray() };
    }

    [Theory]
    [MemberData(nameof(InvalidCells))]
    public void InvalidCoverageAndContentNeverProduceAModel(TableCellDefinition[] cells)
        => Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells));

    [Fact]
    public void MetadataAndHeaderBoundaryAreValidated()
    {
        TableCellDefinition[] cells = [new(0, 0, 2, 2, "all")];
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells, hasHeader: true));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells, "multiple\nlines"));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells, columnWidths: ["1"]));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells, columnWidths: ["0", "1"]));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells, columnWidths: ["1a", "1"]));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 2, cells, columnWidths: [null!, "1"]));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(101, 2, cells));
        Assert.Throws<ArgumentException>(() => TableDefinition.FromCells(2, 51, cells));
    }
}
