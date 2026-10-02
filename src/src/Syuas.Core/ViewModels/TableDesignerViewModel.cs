using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public sealed class TableCellViewModel(TableCell cell, TableDefinition table, Action<string> setText) : ObservableObject
{
    private bool selected;
    private bool header = table.HasHeader && cell.Row == 0;
    public TableCell Cell => cell;
    public int Row => cell.Row;
    public int Column => cell.Column;
    public int RowSpan => cell.RowSpan;
    public int ColumnSpan => cell.ColumnSpan;
    public string Text { get => cell.Text; set { if (cell.Text == value) return; setText(value); Changed(); } }
    public string Label => $"{Row + 1}行 {Column + 1}列" + (RowSpan > 1 || ColumnSpan > 1 ? $"  ({RowSpan}行 × {ColumnSpan}列)" : "");
    public bool IsHeader => table.HasHeader && Row == 0;
    public bool IsSelected
    {
        get => selected;
        internal set
        {
            if (selected != value) { selected = value; Changed(); }
            if (header != IsHeader) { header = IsHeader; Changed(nameof(IsHeader)); }
        }
    }
}

public sealed class TableDesignerViewModel : ObservableObject
{
    private readonly string newLine;
    private readonly Func<TableEditApplyResult>? applyEdit;
    private readonly int sourceLine;
    private readonly TableEditHistory history;
    private bool applying;
    private bool changing;
    private int bindingGeneration;
    private TableDesignerSnapshot? pendingSizeInput;
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
        history = new(Definition);
        rowsInput = Definition.RowCount.ToString(); columnsInput = Definition.ColumnCount.ToString();
        Selection = new(0, 0, 1, 1);
        ResizeCommand = new(_ => Mutate("サイズ変更", () =>
        {
            if (!int.TryParse(RowsInput, out var rows) || !int.TryParse(ColumnsInput, out var columns))
                throw new ArgumentException("行数と列数は整数で入力してください。");
            Definition.Resize(rows, columns);
        }, structural: true, includeSizeInput: true), _ => CanEdit);
        MergeCommand = new(_ => Mutate("セル結合", () => Definition.Merge(Selection), structural: true), _ => CanEdit && Definition.Cells.Count(Selection.Intersects) > 1);
        UnmergeCommand = new(_ => Mutate("結合解除", () => Definition.Unmerge(Selection), structural: true), _ => CanEdit && Definition.Cells.Any(c => Selection.Intersects(c) && (c.RowSpan > 1 || c.ColumnSpan > 1)));
        AddRowCommand = new(_ => Mutate("行追加", () => Definition.InsertRow(Selection.LastRow + 1), structural: true), _ => CanEdit && Definition.RowCount < TableDefinition.MaxRows);
        DeleteRowCommand = new(_ => Mutate("行削除", () => Definition.DeleteRow(Selection.Row), structural: true), _ => CanEdit && Definition.RowCount > 1);
        AddColumnCommand = new(_ => Mutate("列追加", () => Definition.InsertColumn(Selection.LastColumn + 1), structural: true), _ => CanEdit && Definition.ColumnCount < TableDefinition.MaxColumns);
        DeleteColumnCommand = new(_ => Mutate("列削除", () => Definition.DeleteColumn(Selection.Column), structural: true), _ => CanEdit && Definition.ColumnCount > 1);
        UndoCommand = new(_ => RestoreHistory(redo: false), _ => CanUndo);
        RedoCommand = new(_ => RestoreHistory(redo: true), _ => CanRedo);
        ConfirmCommand = new(_ => Confirm(), _ => Snippet is not null && CanEdit);
        SetSelection(0, 0);
        Rebuild();
        PublishState();
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
    public string RowsInput { get => rowsInput; set => SetSizeInput(value, rows: true); }
    public string ColumnsInput { get => columnsInput; set => SetSizeInput(value, rows: false); }
    public string Title
    {
        get => Definition.Title;
        set { if (Definition.Title != value) Mutate("表タイトル", () => Definition.Title = value); }
    }
    public bool HasHeader
    {
        get => Definition.HasHeader;
        set
        {
            if (Definition.HasHeader != value) Mutate("ヘッダー設定", () => Definition.HasHeader = value);
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
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    private bool CanEdit => !applying && !changing;
    public bool CanUndo => CanEdit && (pendingSizeInput is not null || history.CanUndo);
    public bool CanRedo => CanEdit && pendingSizeInput is null && history.CanRedo;
    public int UndoCount => history.UndoCount + (pendingSizeInput is null ? 0 : 1);
    public int RedoCount => pendingSizeInput is null ? history.RedoCount : 0;
    public string? UndoDescription => pendingSizeInput is null ? history.UndoDescription : "表サイズの入力";
    public string? RedoDescription => pendingSizeInput is null ? history.RedoDescription : null;
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand InsertCommand => ConfirmCommand;

    private void Confirm()
    {
        if (!ConfirmCommand.CanExecute(null)) return;
        CommitSizeInput();
        applying = true;
        RefreshCommands();
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
        finally { applying = false; RefreshCommands(); }
    }

    public void SelectCell(int row, int column, bool extend = false)
    {
        if (!CanEdit) return;
        SetSelection(row, column, extend);
        UpdateSelection();
        RefreshCommands();
    }

    private void SetSelection(int row, int column, bool extend = false)
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
    }

    private TableDesignerSnapshot CaptureState() => TableDesignerSnapshot.Capture(
        Definition, Selection, anchorRow, anchorColumn, rowsInput, columnsInput);

    private void SetSizeInput(string value, bool rows)
    {
        if (!CanEdit || value == (rows ? rowsInput : columnsInput)) return;
        ArgumentNullException.ThrowIfNull(value);
        changing = true;
        try
        {
            pendingSizeInput ??= CaptureState();
            if (rows) rowsInput = value; else columnsInput = value;
            if (rowsInput == pendingSizeInput.RowsInput && columnsInput == pendingSizeInput.ColumnsInput)
                pendingSizeInput = null;
            PublishState();
        }
        finally { changing = false; RefreshCommands(); }
    }

    private void CommitSizeInput()
    {
        if (pendingSizeInput is null) return;
        history.Record("表サイズの入力", pendingSizeInput, CaptureState());
        pendingSizeInput = null;
    }

    private void Mutate(string description, Action action, bool structural = false, bool includeSizeInput = false)
    {
        if (!CanEdit) return;
        changing = true;
        try
        {
            if (!includeSizeInput) CommitSizeInput();
            var current = CaptureState();
            var before = includeSizeInput ? pendingSizeInput ?? current : current;
            bool changed;
            try
            {
                changed = history.Execute(description, before, () =>
                {
                    action();
                    if (structural)
                    {
                        rowsInput = Definition.RowCount.ToString(); columnsInput = Definition.ColumnCount.ToString();
                        SetSelection(Math.Min(current.Selection.Row, Definition.RowCount - 1),
                            Math.Min(current.Selection.Column, Definition.ColumnCount - 1));
                    }
                    return CaptureState();
                });
            }
            catch (ArgumentException e)
            {
                // Execute restored the same Definition instance but replaced its cells.
                // Keep invalid size drafts available for correction and for Undo.
                RestoreViewState(current);
                PublishState(e.Message);
                return;
            }
            catch
            {
                RestoreViewState(current);
                PublishState();
                throw;
            }
            if (includeSizeInput) pendingSizeInput = null;
            if (changed && structural) Rebuild();
            if (!changed && structural)
            {
                Selection = current.Selection;
                anchorRow = current.AnchorRow;
                anchorColumn = current.AnchorColumn;
            }
            PublishState();
        }
        finally { changing = false; RefreshCommands(); }
    }

    private void RestoreHistory(bool redo)
    {
        if (redo ? !CanRedo : !CanUndo) return;
        changing = true;
        try
        {
            CommitSizeInput();
            var restored = redo ? history.Redo() : history.Undo();
            if (restored is null) return;
            RestoreViewState(restored);
            PublishState();
        }
        finally { changing = false; RefreshCommands(); }
    }

    private void RestoreViewState(TableDesignerSnapshot snapshot)
    {
        rowsInput = snapshot.RowsInput;
        columnsInput = snapshot.ColumnsInput;
        Selection = snapshot.Selection;
        anchorRow = snapshot.AnchorRow;
        anchorColumn = snapshot.AnchorColumn;
        Rebuild();
    }

    private void Rebuild()
    {
        var generation = ++bindingGeneration;
        Cells = Definition.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column)
            .Select(c => new TableCellViewModel(c, Definition, value =>
            {
                if (generation == bindingGeneration) Mutate("セル入力", () => c.Text = value);
            })).ToArray();
        ColumnWidths = Enumerable.Range(0, Definition.ColumnCount).Select(column =>
        {
            var field = new InputField(column.ToString(), $"列 {column + 1}", Definition.ColumnWidths[column]);
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(InputField.Value) && generation == bindingGeneration
                    && field.Value != Definition.ColumnWidths[column])
                {
                    if (!CanEdit) { field.Value = Definition.ColumnWidths[column]; return; }
                    Mutate("列幅", () => Definition.SetColumnWidth(column, field.Value));
                }
            };
            return field;
        }).ToArray();
        Changed(nameof(Cells)); Changed(nameof(ColumnWidths));
        UpdateSelection();
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelection()
    {
        foreach (var cell in Cells) cell.IsSelected = Selection.Contains(cell.Cell);
        Changed(nameof(SelectionLabel));
    }

    private void PublishState(string? operationError = null)
    {
        Changed(nameof(RowsInput)); Changed(nameof(ColumnsInput));
        Changed(nameof(Title)); Changed(nameof(HasHeader));
        UpdateSelection();
        Refresh(operationError);
    }

    private void RefreshCommands()
    {
        Changed(nameof(CanUndo)); Changed(nameof(CanRedo));
        Changed(nameof(UndoCount)); Changed(nameof(RedoCount));
        Changed(nameof(UndoDescription)); Changed(nameof(RedoDescription));
        UndoCommand.Refresh(); RedoCommand.Refresh();
        ResizeCommand.Refresh(); ConfirmCommand.Refresh();
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
