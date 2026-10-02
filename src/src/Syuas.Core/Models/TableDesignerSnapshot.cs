using System.Globalization;

namespace Syuas.Core.Models;

/// <summary>Table data plus editable size fields and selection, independent of WPF controls.</summary>
public sealed class TableDesignerSnapshot
{
    private TableDesignerSnapshot(TableSnapshot table, TableSelection selection, int anchorRow,
        int anchorColumn, string rowsInput, string columnsInput)
    {
        Table = table;
        Selection = selection;
        AnchorRow = anchorRow;
        AnchorColumn = anchorColumn;
        RowsInput = rowsInput;
        ColumnsInput = columnsInput;
    }

    public TableSnapshot Table { get; }
    public TableSelection Selection { get; }
    public int AnchorRow { get; }
    public int AnchorColumn { get; }
    public string RowsInput { get; }
    public string ColumnsInput { get; }
    public long EstimatedBytes => 64L + Table.EstimatedBytes
        + TableSnapshot.StringBytes(RowsInput) + TableSnapshot.StringBytes(ColumnsInput);

    public static TableDesignerSnapshot Capture(TableDefinition table, TableSelection selection,
        int anchorRow, int anchorColumn, string? rowsInput = null, string? columnsInput = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (selection.Row < 0 || selection.Column < 0 || selection.RowCount < 1 || selection.ColumnCount < 1
            || selection.Row >= table.RowCount || selection.Column >= table.ColumnCount
            || selection.RowCount > table.RowCount - selection.Row
            || selection.ColumnCount > table.ColumnCount - selection.Column)
            throw new ArgumentException("選択範囲が表の範囲外です。", nameof(selection));
        if (anchorRow < selection.Row || anchorRow > selection.LastRow
            || anchorColumn < selection.Column || anchorColumn > selection.LastColumn)
            throw new ArgumentException("選択の起点が選択範囲外です。");
        if (table.Cells.Any(c => selection.Intersects(c) && !selection.Contains(c)))
            throw new ArgumentException("選択範囲には結合セル全体を含めてください。", nameof(selection));
        return new(TableSnapshot.Capture(table), selection, anchorRow, anchorColumn,
            rowsInput ?? table.RowCount.ToString(CultureInfo.InvariantCulture),
            columnsInput ?? table.ColumnCount.ToString(CultureInfo.InvariantCulture));
    }

    // Navigation alone is not an edit and must not consume Undo or discard Redo.
    public bool ContentEquals(TableDesignerSnapshot? other) => other is not null
        && Table.ContentEquals(other.Table) && RowsInput == other.RowsInput && ColumnsInput == other.ColumnsInput;
}
