using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public sealed class TableCellViewModel(TableCell cell, TableDefinition table, Action textChanged) : ObservableObject
{
    private bool selected;
    public TableCell Cell => cell;
    public int Row => cell.Row;
    public int Column => cell.Column;
    public int RowSpan => cell.RowSpan;
    public int ColumnSpan => cell.ColumnSpan;
    public string Text { get => cell.Text; set { cell.Text = value; Changed(); textChanged(); } }
    public string Label => $"{Row + 1}行 {Column + 1}列" + (RowSpan > 1 || ColumnSpan > 1 ? $"  ({RowSpan}行 × {ColumnSpan}列)" : "");
    public bool IsHeader => table.HasHeader && Row == 0;
    public bool IsSelected { get => selected; internal set { selected = value; Changed(); Changed(nameof(IsHeader)); } }
}

public sealed class TableDesignerViewModel : ObservableObject
{
    private readonly string newLine;
    private readonly Func<TableEditApplyResult>? applyEdit;
    private readonly int sourceLine;
    private bool applying;
    private string rowsInput;
    private string columnsInput;
    private int anchorRow;
    private int anchorColumn;

    public TableDesignerViewModel(string newLine = "\n", TableDefinition? definition = null,
        Func<TableEditApplyResult>? applyEdit = null, int sourceLine = 1)
    {
        this.newLine = newLine;
        this.applyEdit = applyEdit;
        this.sourceLine = sourceLine;
        Definition = definition ?? new();
        rowsInput = Definition.RowCount.ToString(); columnsInput = Definition.ColumnCount.ToString();
        Selection = new(0, 0, 1, 1);
        ResizeCommand = new(_ => Mutate(() =>
        {
            if (!int.TryParse(RowsInput, out var rows) || !int.TryParse(ColumnsInput, out var columns))
                throw new ArgumentException("行数と列数は整数で入力してください。");
            Definition.Resize(rows, columns);
        }));
        MergeCommand = new(_ => Mutate(() => Definition.Merge(Selection)), _ => Definition.Cells.Count(Selection.Intersects) > 1);
        UnmergeCommand = new(_ => Mutate(() => Definition.Unmerge(Selection)), _ => Definition.Cells.Any(c => Selection.Intersects(c) && (c.RowSpan > 1 || c.ColumnSpan > 1)));
        AddRowCommand = new(_ => Mutate(() => Definition.InsertRow(Selection.LastRow + 1)), _ => Definition.RowCount < TableDefinition.MaxRows);
        DeleteRowCommand = new(_ => Mutate(() => Definition.DeleteRow(Selection.Row)), _ => Definition.RowCount > 1);
        AddColumnCommand = new(_ => Mutate(() => Definition.InsertColumn(Selection.LastColumn + 1)), _ => Definition.ColumnCount < TableDefinition.MaxColumns);
        DeleteColumnCommand = new(_ => Mutate(() => Definition.DeleteColumn(Selection.Column)), _ => Definition.ColumnCount > 1);
        ConfirmCommand = new(_ => Confirm(), _ => Snippet is not null && !applying);
        Rebuild();
    }

    public event EventHandler? StructureChanged;
    public event EventHandler? CloseRequested;
    public TableDefinition Definition { get; }
    public bool IsEditing => applyEdit is not null;
    public string DialogTitle => IsEditing ? "表を再編集 — SYUAS" : "表デザイナー";
    public string ConfirmLabel => IsEditing ? "適用" : "挿入";
    public string EditHint => $"文書の{sourceLine}行目からの表を編集中です。「適用」で元の表を更新します。\nセル内はAsciiDocソースです。&#124; などの文字参照はそのまま保持されます。";
    public IReadOnlyList<TableCellViewModel> Cells { get; private set; } = [];
    public IReadOnlyList<InputField> ColumnWidths { get; private set; } = [];
    public TableSelection Selection { get; private set; }
    public string SelectionLabel => $"選択: {Selection.Row + 1}〜{Selection.LastRow + 1}行 / {Selection.Column + 1}〜{Selection.LastColumn + 1}列";
    public string RowsInput { get => rowsInput; set { rowsInput = value; Changed(); Refresh(); } }
    public string ColumnsInput { get => columnsInput; set { columnsInput = value; Changed(); Refresh(); } }
    public string Title { get => Definition.Title; set { Definition.Title = value; Changed(); Refresh(); } }
    public bool HasHeader
    {
        get => Definition.HasHeader;
        set
        {
            try { Definition.HasHeader = value; Refresh(); }
            catch (ArgumentException e) { Refresh(e.Message); }
            Changed(); UpdateSelection();
        }
    }
    public string Preview { get; private set; } = "";
    public string Error { get; private set; } = "";
    public InsertionSnippet? Snippet { get; private set; }
    public RelayCommand ResizeCommand { get; }
    public RelayCommand MergeCommand { get; }
    public RelayCommand UnmergeCommand { get; }
    public RelayCommand AddRowCommand { get; }
    public RelayCommand DeleteRowCommand { get; }
    public RelayCommand AddColumnCommand { get; }
    public RelayCommand DeleteColumnCommand { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand InsertCommand => ConfirmCommand;

    private void Confirm()
    {
        if (!ConfirmCommand.CanExecute(null)) return;
        applying = true;
        ConfirmCommand.Refresh();
        try
        {
            if (applyEdit is not null)
            {
                var result = applyEdit();
                if (!result.Succeeded)
                {
                    Error = result.Diagnostic?.DisplayMessage ?? "表を適用できませんでした。入力内容を確認してください。";
                    Changed(nameof(Error));
                    return;
                }
            }
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        finally { applying = false; ConfirmCommand.Refresh(); }
    }

    public void SelectCell(int row, int column, bool extend = false)
    {
        var cell = Definition.CellAt(row, column);
        if (!extend) { anchorRow = cell.Row; anchorColumn = cell.Column; }
        var anchor = Definition.CellAt(anchorRow, anchorColumn);
        var top = Math.Min(anchor.Row, cell.Row);
        var left = Math.Min(anchor.Column, cell.Column);
        var bottom = Math.Max(anchor.Row + anchor.RowSpan - 1, cell.Row + cell.RowSpan - 1);
        var right = Math.Max(anchor.Column + anchor.ColumnSpan - 1, cell.Column + cell.ColumnSpan - 1);
        // A selection always contains whole merged cells, including spans crossed by its edges.
        while (true)
        {
            var bounds = new TableSelection(top, left, bottom - top + 1, right - left + 1);
            var crossing = Definition.Cells.Where(c => bounds.Intersects(c) && !bounds.Contains(c)).ToArray();
            if (crossing.Length == 0) { Selection = bounds; break; }
            foreach (var crossed in crossing)
            {
                top = Math.Min(top, crossed.Row); left = Math.Min(left, crossed.Column);
                bottom = Math.Max(bottom, crossed.Row + crossed.RowSpan - 1);
                right = Math.Max(right, crossed.Column + crossed.ColumnSpan - 1);
            }
        }
        UpdateSelection();
    }

    private void Mutate(Action action)
    {
        try
        {
            action();
            rowsInput = Definition.RowCount.ToString(); columnsInput = Definition.ColumnCount.ToString();
            Changed(nameof(RowsInput)); Changed(nameof(ColumnsInput));
            anchorRow = Math.Min(Selection.Row, Definition.RowCount - 1);
            anchorColumn = Math.Min(Selection.Column, Definition.ColumnCount - 1);
            Rebuild();
        }
        catch (ArgumentException e) { Refresh(e.Message); }
    }

    private void Rebuild()
    {
        Cells = Definition.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column)
            .Select(c => new TableCellViewModel(c, Definition, () => Refresh())).ToArray();
        ColumnWidths = Enumerable.Range(0, Definition.ColumnCount).Select(column =>
        {
            var field = new InputField(column.ToString(), $"列 {column + 1}", Definition.ColumnWidths[column]);
            field.PropertyChanged += (_, _) => { Definition.SetColumnWidth(column, field.Value); Refresh(); };
            return field;
        }).ToArray();
        Changed(nameof(Cells)); Changed(nameof(ColumnWidths));
        SelectCell(anchorRow, anchorColumn);
        Refresh();
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelection()
    {
        foreach (var cell in Cells) cell.IsSelected = Selection.Contains(cell.Cell);
        Changed(nameof(SelectionLabel));
        MergeCommand.Refresh(); UnmergeCommand.Refresh();
        AddRowCommand.Refresh(); DeleteRowCommand.Refresh(); AddColumnCommand.Refresh(); DeleteColumnCommand.Refresh();
    }

    private void Refresh(string? operationError = null)
    {
        try
        {
            if (!int.TryParse(RowsInput, out var rows) || !int.TryParse(ColumnsInput, out var columns)
                || rows != Definition.RowCount || columns != Definition.ColumnCount)
                throw new ArgumentException("行・列数を変更した場合は「サイズを適用」を押してください。");
            Preview = AsciiDocTableGenerator.Generate(Definition, newLine);
            var delimiter = Preview.IndexOf("|===" + newLine, StringComparison.Ordinal);
            var firstCell = Preview.IndexOf('|', delimiter + 4 + newLine.Length);
            Snippet = new(Preview, firstCell + 1, true);
            Error = operationError ?? "";
        }
        catch (ArgumentException e) { Preview = ""; Snippet = null; Error = operationError ?? e.Message; }
        Changed(nameof(Preview)); Changed(nameof(Error)); InsertCommand.Refresh();
    }
}
