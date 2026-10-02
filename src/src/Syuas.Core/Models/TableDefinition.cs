using System.Globalization;

namespace Syuas.Core.Models;

// Immutable input for importing cells without executing designer merge operations.
public sealed record TableCellDefinition(int Row, int Column, int RowSpan, int ColumnSpan, string Text);

public sealed class TableCell
{
    internal TableCell(int row, int column) { Row = row; Column = column; }
    public int Row { get; internal set; }
    public int Column { get; internal set; }
    public int RowSpan { get; internal set; } = 1;
    public int ColumnSpan { get; internal set; } = 1;
    public string Text { get; set; } = "";
}

public readonly record struct TableSelection(int Row, int Column, int RowCount, int ColumnCount)
{
    public int LastRow => Row + RowCount - 1;
    public int LastColumn => Column + ColumnCount - 1;
    public bool Intersects(TableCell cell) => cell.Row <= LastRow && cell.Row + cell.RowSpan > Row
        && cell.Column <= LastColumn && cell.Column + cell.ColumnSpan > Column;
    public bool Contains(TableCell cell) => cell.Row >= Row && cell.Row + cell.RowSpan - 1 <= LastRow
        && cell.Column >= Column && cell.Column + cell.ColumnSpan - 1 <= LastColumn;
}

// Every logical slot is covered by exactly one cell. Covered slots do not own text.
public sealed class TableDefinition
{
    public const int MaxRows = 100;
    public const int MaxColumns = 50;
    private readonly List<TableCell> cells = [];
    private readonly List<string> columnWidths = [];
    private bool hasHeader;

    public TableDefinition(int rows = 3, int columns = 3)
    {
        CheckSize(rows, columns);
        RowCount = rows; ColumnCount = columns;
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++) cells.Add(new(row, column));
        columnWidths.AddRange(Enumerable.Repeat("", columns));
    }

    public static TableDefinition FromCells(int rows, int columns, IEnumerable<TableCellDefinition> cells,
        string title = "", bool hasHeader = false, IReadOnlyList<string>? columnWidths = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(title);
        var table = new TableDefinition(rows, columns);
        if (title.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("表タイトルは1行で指定してください。", nameof(title));
        table.Title = title;
        if (columnWidths is not null)
        {
            if (columnWidths.Count != columns) throw new ArgumentException("列幅の数と列数が一致しません。", nameof(columnWidths));
            for (var column = 0; column < columns; column++)
            {
                var width = columnWidths[column];
                if (width is null || !string.IsNullOrWhiteSpace(width) &&
                    (!int.TryParse(width, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1))
                    throw new ArgumentException("列幅は正の整数または空欄で指定してください。", nameof(columnWidths));
                table.columnWidths[column] = width;
            }
        }
        table.cells.Clear();
        foreach (var cell in cells)
        {
            if (cell is null || cell.Text is null) throw new ArgumentException("セルと本文はnullにできません。", nameof(cells));
            if (table.cells.Count >= rows * columns) throw new ArgumentException("セル数が表のサイズを超えています。", nameof(cells));
            table.cells.Add(new(cell.Row, cell.Column) { RowSpan = cell.RowSpan, ColumnSpan = cell.ColumnSpan, Text = cell.Text });
        }
        table.HasHeader = hasHeader;
        table.Validate();
        return table;
    }

    public int RowCount { get; private set; }
    public int ColumnCount { get; private set; }
    public string Title { get; set; } = "";
    public bool HasHeader
    {
        get => hasHeader;
        set
        {
            if (value && cells.Any(c => c.Row == 0 && c.RowSpan > 1))
                throw new ArgumentException("ヘッダーと本文をまたぐセル結合はできません。先頭行の結合を解除してください。");
            hasHeader = value;
        }
    }
    public IReadOnlyList<TableCell> Cells => cells.AsReadOnly();
    public IReadOnlyList<string> ColumnWidths => columnWidths.AsReadOnly();
    public void SetColumnWidth(int column, string width) => columnWidths[column] = width;

    /// <summary>
    /// Restores a captured state into this instance. Existing cell references must be rebound
    /// afterwards. Import validation remains in FromCells; snapshots may contain draft values.
    /// </summary>
    public void Restore(TableSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Prepare all allocations before replacing the live state. Snapshot construction
        // already checked coverage/header constraints and exposes no mutable collections.
        var restoredCells = snapshot.Cells.Select(c => new TableCell(c.Row, c.Column)
        {
            RowSpan = c.RowSpan, ColumnSpan = c.ColumnSpan, Text = c.Text
        }).ToArray();
        cells.EnsureCapacity(restoredCells.Length);
        columnWidths.EnsureCapacity(snapshot.ColumnCount);
        cells.Clear();
        cells.AddRange(restoredCells);
        columnWidths.Clear();
        for (var column = 0; column < snapshot.ColumnCount; column++)
            columnWidths.Add(snapshot.ColumnWidths[column]);
        RowCount = snapshot.RowCount;
        ColumnCount = snapshot.ColumnCount;
        Title = snapshot.Title;
        hasHeader = snapshot.HasHeader;
    }

    public TableCell CellAt(int row, int column)
    {
        if (row < 0 || row >= RowCount || column < 0 || column >= ColumnCount) throw new ArgumentOutOfRangeException(nameof(row));
        return cells.Single(c => c.Row <= row && c.Row + c.RowSpan > row && c.Column <= column && c.Column + c.ColumnSpan > column);
    }

    public void Merge(TableSelection selection)
    {
        CheckSelection(selection);
        var selected = cells.Where(selection.Intersects).OrderBy(c => c.Row).ThenBy(c => c.Column).ToArray();
        if (selected.Any(c => !selection.Contains(c))) throw new ArgumentException("結合済みセル全体を含む範囲を選択してください。");
        if (HasHeader && selection.Row == 0 && selection.RowCount > 1)
            throw new ArgumentException("ヘッダー行と本文のセルは結合できません。");
        if (selected.Length < 2) return;
        var anchor = selected[0];
        // Preserve all content in reading order instead of silently discarding other cells.
        anchor.Text = string.Join("\n", selected.Where(c => c.Text.Length > 0).Select(c => c.Text));
        anchor.RowSpan = selection.RowCount;
        anchor.ColumnSpan = selection.ColumnCount;
        foreach (var cell in selected.Skip(1)) cells.Remove(cell);
    }

    public void Unmerge(TableSelection selection)
    {
        CheckSelection(selection);
        foreach (var cell in cells.Where(selection.Intersects).ToArray())
        {
            for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                    if (row != cell.Row || column != cell.Column) cells.Add(new(row, column));
            cell.RowSpan = 1; cell.ColumnSpan = 1;
        }
    }

    public void InsertRow(int index)
    {
        if (index < 0 || index > RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        CheckSize(RowCount + 1, ColumnCount);
        foreach (var cell in cells)
        {
            if (cell.Row >= index) cell.Row++;
            else if (cell.Row + cell.RowSpan > index) cell.RowSpan++;
        }
        RowCount++;
        var covered = new bool[ColumnCount];
        foreach (var cell in cells.Where(c => c.Row < index && c.Row + c.RowSpan > index))
            for (var col = cell.Column; col < cell.Column + cell.ColumnSpan; col++) covered[col] = true;
        for (var col = 0; col < ColumnCount; col++) if (!covered[col]) cells.Add(new(index, col));
    }

    public void InsertColumn(int index)
    {
        if (index < 0 || index > ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        CheckSize(RowCount, ColumnCount + 1);
        foreach (var cell in cells)
        {
            if (cell.Column >= index) cell.Column++;
            else if (cell.Column + cell.ColumnSpan > index) cell.ColumnSpan++;
        }
        ColumnCount++;
        columnWidths.Insert(index, "");
        var covered = new bool[RowCount];
        foreach (var cell in cells.Where(c => c.Column < index && c.Column + c.ColumnSpan > index))
            for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++) covered[row] = true;
        for (var row = 0; row < RowCount; row++) if (!covered[row]) cells.Add(new(row, index));
    }

    public void DeleteRow(int index)
    {
        if (index < 0 || index >= RowCount) throw new ArgumentOutOfRangeException(nameof(index));
        CheckSize(RowCount - 1, ColumnCount);
        if (HasHeader && index == 0 && cells.Any(c => c.Row == 1 && c.RowSpan > 1))
            throw new ArgumentException("次の行に縦結合があります。結合を解除してからヘッダー行を削除してください。");
        foreach (var cell in cells.ToArray())
        {
            if (cell.Row > index) cell.Row--;
            else if (cell.Row + cell.RowSpan > index)
            {
                if (cell.RowSpan == 1) cells.Remove(cell);
                else cell.RowSpan--;
            }
        }
        RowCount--;
    }

    public void DeleteColumn(int index)
    {
        if (index < 0 || index >= ColumnCount) throw new ArgumentOutOfRangeException(nameof(index));
        CheckSize(RowCount, ColumnCount - 1);
        foreach (var cell in cells.ToArray())
        {
            if (cell.Column > index) cell.Column--;
            else if (cell.Column + cell.ColumnSpan > index)
            {
                if (cell.ColumnSpan == 1) cells.Remove(cell);
                else cell.ColumnSpan--;
            }
        }
        ColumnCount--;
        columnWidths.RemoveAt(index);
    }

    public void Resize(int rows, int columns)
    {
        CheckSize(rows, columns);
        while (RowCount < rows) InsertRow(RowCount);
        while (RowCount > rows) DeleteRow(RowCount - 1);
        while (ColumnCount < columns) InsertColumn(ColumnCount);
        while (ColumnCount > columns) DeleteColumn(ColumnCount - 1);
    }

    public void Validate()
    {
        CheckSize(RowCount, ColumnCount);
        var occupied = new bool[RowCount, ColumnCount];
        foreach (var cell in cells)
        {
            CheckSelection(new(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan));
            if (HasHeader && cell.Row == 0 && cell.RowSpan > 1) throw new ArgumentException("ヘッダー行に縦結合は指定できません。");
            for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var col = cell.Column; col < cell.Column + cell.ColumnSpan; col++)
                {
                    if (occupied[row, col]) throw new ArgumentException("セル範囲が重複しています。");
                    occupied[row, col] = true;
                }
        }
        foreach (var value in occupied) if (!value) throw new ArgumentException("セルが不足しています。");
    }

    private void CheckSelection(TableSelection selection)
    {
        if (selection.Row < 0 || selection.Row >= RowCount || selection.Column < 0 || selection.Column >= ColumnCount
            || selection.RowCount < 1 || selection.ColumnCount < 1
            || selection.RowCount > RowCount - selection.Row || selection.ColumnCount > ColumnCount - selection.Column)
            throw new ArgumentException("表の範囲内のセルを選択してください。");
    }
    private static void CheckSize(int rows, int columns)
    {
        if (rows is < 1 or > MaxRows || columns is < 1 or > MaxColumns)
            throw new ArgumentException($"行数は1〜{MaxRows}、列数は1〜{MaxColumns}で指定してください。");
    }
}
