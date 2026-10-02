using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.App.Views;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class TableDesignerHistoryTests
{
    private static TableCellViewModel Cell(TableDesignerViewModel model, int row, int column) =>
        model.Cells.Single(c => c.Row == row && c.Column == column);

    [Fact]
    public void AllEditPathsShareOneHistoryAndRestoreTheSameDefinition()
    {
        var table = new TableDefinition(4, 4) { HasHeader = true };
        foreach (var cell in table.Cells) cell.Text = $"{cell.Row}:{cell.Column}";
        var model = new TableDesignerViewModel(definition: table);
        var states = new List<TableSnapshot> { TableSnapshot.Capture(table) };
        Action[] edits =
        [
            () => Cell(model, 1, 1).Text = "日本語😀\r\n&#124;",
            () => model.Title = "タイトル",
            () => model.ColumnWidths[2].Value = "3",
            () => { model.SelectCell(1, 1); model.SelectCell(2, 2, true); model.MergeCommand.Execute(null); },
            () => model.UnmergeCommand.Execute(null),
            () => model.AddRowCommand.Execute(null),
            () => model.DeleteRowCommand.Execute(null),
            () => model.AddColumnCommand.Execute(null),
            () => model.DeleteColumnCommand.Execute(null),
            () => model.HasHeader = false,
            () => { model.RowsInput = "2"; model.ColumnsInput = "2"; model.ResizeCommand.Execute(null); }
        ];
        foreach (var edit in edits)
        {
            edit();
            Assert.Empty(model.Error);
            states.Add(TableSnapshot.Capture(table));
        }
        Assert.Equal(edits.Length, model.UndoCount);
        for (var i = states.Count - 2; i >= 0; i--)
        {
            model.UndoCommand.Execute(null);
            Assert.Same(table, model.Definition);
            Assert.True(states[i].ContentEquals(TableSnapshot.Capture(table)));
            Assert.Equal(AsciiDocTableGenerator.Generate(table), model.Preview);
            Assert.NotNull(model.Snippet);
            Assert.True(model.ConfirmCommand.CanExecute(null));
        }
        Assert.False(model.CanUndo);
        for (var i = 1; i < states.Count; i++)
        {
            model.RedoCommand.Execute(null);
            Assert.True(states[i].ContentEquals(TableSnapshot.Capture(table)));
            table.Validate();
        }
        Assert.False(model.CanRedo);
    }

    [Fact]
    public void MergeUndoRestoresSelectionAndItsOriginalExtensionAnchor()
    {
        var model = new TableDesignerViewModel();
        Cell(model, 0, 0).Text = "A";
        Cell(model, 0, 1).Text = "B";
        model.SelectCell(1, 1);
        model.SelectCell(0, 0, true);
        model.MergeCommand.Execute(null);
        Assert.Equal("セル結合", model.UndoDescription);
        model.UndoCommand.Execute(null);
        Assert.Equal(new TableSelection(0, 0, 2, 2), model.Selection);
        Assert.Equal(4, model.Cells.Count(c => c.IsSelected));
        Assert.Equal("A", Cell(model, 0, 0).Text);
        Assert.Equal("B", Cell(model, 0, 1).Text);
        model.SelectCell(2, 2, true);
        Assert.Equal(new TableSelection(1, 1, 2, 2), model.Selection);
        Assert.Equal(1, model.RedoCount);
        model.RedoCommand.Execute(null);
        Assert.Equal(new TableSelection(0, 0, 2, 2), model.Selection);
        Assert.Equal("A\nB", Cell(model, 0, 0).Text);
    }

    [Fact]
    public void ImportedMergedFirstCellHasAValidInitialSelection()
    {
        var table = new TableDefinition(3, 3);
        table.Merge(new(0, 0, 2, 2));
        var model = new TableDesignerViewModel(definition: table);
        Assert.Equal(new TableSelection(0, 0, 2, 2), model.Selection);
        Cell(model, 0, 0).Text = "更新";
        model.UndoCommand.Execute(null);
        Assert.Equal("", Cell(model, 0, 0).Text);
        Assert.Equal(2, Cell(model, 0, 0).RowSpan);
    }

    [Fact]
    public void OrdinaryInputDoesNotRebuildTheGridOrReplaceCellAndWidthBindings()
    {
        var model = new TableDesignerViewModel();
        var cell = model.Cells[0];
        var width = model.ColumnWidths[0];
        var rebuilds = 0;
        model.StructureChanged += (_, _) => rebuilds++;
        cell.Text = "入力";
        model.Title = "タイトル";
        width.Value = "2";
        model.HasHeader = true;
        model.RowsInput = "4";
        Assert.Equal(0, rebuilds);
        Assert.Same(cell, model.Cells[0]);
        Assert.Same(width, model.ColumnWidths[0]);
    }

    [Fact]
    public void DetachedBindingsCannotWriteIntoRestoredOrResizedTables()
    {
        var model = new TableDesignerViewModel();
        var oldCell = model.Cells[0];
        var oldWidth = model.ColumnWidths[2];
        oldCell.Text = "current";
        model.UndoCommand.Execute(null);
        oldCell.Text = "late event";
        oldWidth.Value = "bad";
        Assert.Equal("", model.Cells[0].Text);
        Assert.Equal("", model.ColumnWidths[2].Value);
        Assert.True(model.CanRedo);
        model.RedoCommand.Execute(null);
        Assert.Equal("current", model.Cells[0].Text);
        var removedWidth = model.ColumnWidths[2];
        model.ColumnsInput = "1";
        model.ResizeCommand.Execute(null);
        removedWidth.Value = "99";
        Assert.Single(model.ColumnWidths);
        Assert.Empty(model.Error);
    }

    [Fact]
    public void InvalidColumnWidthCanBeUndoneAndRedoneWithValidationAndPreview()
    {
        var model = new TableDesignerViewModel();
        model.ColumnWidths[0].Value = "bad";
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.Null(model.Snippet);
        Assert.Empty(model.Preview);
        Assert.NotEmpty(model.Error);
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.ColumnWidths[0].Value);
        Assert.Equal("", model.Definition.ColumnWidths[0]);
        Assert.Empty(model.Error);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        model.RedoCommand.Execute(null);
        Assert.Equal("bad", model.ColumnWidths[0].Value);
        Assert.NotEmpty(model.Error);
        model.ColumnWidths[0].Value = "3";
        Assert.True(model.ConfirmCommand.CanExecute(null));
        Assert.Contains("cols=\"3,1,1\"", model.Preview);
    }

    [Fact]
    public void InvalidTitleIsAnUndoableDraft()
    {
        var model = new TableDesignerViewModel();
        model.Title = "複数\n行";
        Assert.NotEmpty(model.Error);
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.Title);
        Assert.Empty(model.Error);
        model.RedoCommand.Execute(null);
        Assert.Equal("複数\n行", model.Title);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void SizeInputAndApplyAreOneOperationIncludingDeletedContents()
    {
        var table = new TableDefinition(3, 3);
        table.CellAt(2, 2).Text = "削除されるセル";
        var model = new TableDesignerViewModel(definition: table);
        model.RowsInput = "2";
        model.ColumnsInput = "2";
        Assert.Equal(1, model.UndoCount);
        Assert.Equal("表サイズの入力", model.UndoDescription);
        model.ResizeCommand.Execute(null);
        Assert.Equal(1, model.UndoCount);
        Assert.Equal("サイズ変更", model.UndoDescription);
        model.UndoCommand.Execute(null);
        Assert.Equal(3, model.Definition.RowCount);
        Assert.Equal("3", model.RowsInput);
        Assert.Equal("3", model.ColumnsInput);
        Assert.Equal("削除されるセル", Cell(model, 2, 2).Text);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        model.RedoCommand.Execute(null);
        Assert.Equal(2, model.Definition.RowCount);
        Assert.Equal("2", model.RowsInput);
        Assert.Equal("2", model.ColumnsInput);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("101")]
    public void FailedResizeRetainsDraftAndPreviousHistoryUntilCorrected(string input)
    {
        var model = new TableDesignerViewModel();
        model.Title = "前の変更";
        model.RowsInput = input;
        model.ResizeCommand.Execute(null);
        Assert.Equal(input, model.RowsInput);
        Assert.Equal(3, model.Definition.RowCount);
        Assert.NotEmpty(model.Error);
        Assert.Equal(2, model.UndoCount);
        model.RowsInput = "4";
        model.ResizeCommand.Execute(null);
        Assert.Empty(model.Error);
        Assert.Equal(2, model.UndoCount);
        model.UndoCommand.Execute(null);
        Assert.Equal("3", model.RowsInput);
        Assert.Equal("前の変更", model.Title);
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.Title);
    }

    [Fact]
    public void UnappliedSizeDraftCanBeUndoneRedoneAndThenApplied()
    {
        var model = new TableDesignerViewModel();
        model.RowsInput = "bad";
        model.UndoCommand.Execute(null);
        Assert.Equal("3", model.RowsInput);
        Assert.Empty(model.Error);
        model.RedoCommand.Execute(null);
        Assert.Equal("bad", model.RowsInput);
        Assert.NotEmpty(model.Error);
        model.RowsInput = "4";
        model.ResizeCommand.Execute(null);
        Assert.Equal(4, model.Definition.RowCount);
        model.UndoCommand.Execute(null);
        Assert.Equal(3, model.Definition.RowCount);
        Assert.Equal("bad", model.RowsInput);
        model.UndoCommand.Execute(null);
        Assert.Equal("3", model.RowsInput);
    }

    [Fact]
    public void OtherEditsCommitSizeDraftInChronologicalOrder()
    {
        var model = new TableDesignerViewModel();
        model.RowsInput = "4";
        model.Title = "タイトル";
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.Title);
        Assert.Equal("4", model.RowsInput);
        model.UndoCommand.Execute(null);
        Assert.Equal("3", model.RowsInput);
        Assert.Equal(3, model.Definition.RowCount);
    }

    [Fact]
    public void ReturningSizeDraftToOriginalAndNoOpsPreserveRedo()
    {
        var model = new TableDesignerViewModel();
        model.Title = "A";
        model.UndoCommand.Execute(null);
        model.RowsInput = "4";
        Assert.False(model.CanRedo);
        model.RedoCommand.Execute(null);
        Assert.Equal("", model.Title);
        model.RowsInput = "3";
        Assert.True(model.CanRedo);
        model.Title = "";
        model.Cells[0].Text = "";
        model.ColumnWidths[0].Value = "";
        model.SelectCell(1, 1);
        model.SelectCell(2, 2, true);
        model.ResizeCommand.Execute(null);
        Assert.Equal(new TableSelection(1, 1, 2, 2), model.Selection);
        Assert.True(model.CanRedo);
        model.RedoCommand.Execute(null);
        Assert.Equal("A", model.Title);
    }

    [Fact]
    public void NewInputAfterUndoDropsTheRedoBranch()
    {
        var model = new TableDesignerViewModel();
        model.Cells[0].Text = "A";
        model.Cells[0].Text = "B";
        model.UndoCommand.Execute(null);
        model.Cells[0].Text = "C";
        Assert.False(model.CanRedo);
        model.UndoCommand.Execute(null);
        Assert.Equal("A", model.Cells[0].Text);
        model.RedoCommand.Execute(null);
        Assert.Equal("C", model.Cells[0].Text);
    }

    [Fact]
    public void FailedHeaderAndMergePreserveHistoryAndRebindValidCells()
    {
        var model = new TableDesignerViewModel();
        model.SelectCell(0, 0); model.SelectCell(1, 1, true);
        model.MergeCommand.Execute(null);
        model.Title = "redo";
        model.UndoCommand.Execute(null);
        model.HasHeader = true;
        Assert.False(model.HasHeader);
        Assert.NotEmpty(model.Error);
        Assert.Equal(1, model.UndoCount);
        Assert.Equal(1, model.RedoCount);
        Assert.Equal(2, model.Cells[0].RowSpan);
        model.RedoCommand.Execute(null);
        Assert.Equal("redo", model.Title);
        model.UnmergeCommand.Execute(null);
        model.HasHeader = true;
        var count = model.UndoCount;
        model.SelectCell(0, 0); model.SelectCell(1, 1, true);
        model.MergeCommand.Execute(null);
        Assert.NotEmpty(model.Error);
        Assert.Equal(count, model.UndoCount);
        Assert.Equal(9, model.Cells.Count);
        model.Cells[0].Text = "after failure";
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.Cells[0].Text);
    }

    [Fact]
    public void NotificationsRefreshCommandsAndReentrantEditsCannotEnterHistory()
    {
        var model = new TableDesignerViewModel();
        var undoNotifications = 0;
        var mergeNotifications = 0;
        model.UndoCommand.CanExecuteChanged += (_, _) => undoNotifications++;
        model.MergeCommand.CanExecuteChanged += (_, _) => mergeNotifications++;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(model.Title)) return;
            model.Title = "reentrant";
            model.ColumnWidths[0].Value = "999";
            model.Cells[0].Text = "reentrant";
            model.UndoCommand.Execute(null);
            model.AddRowCommand.Execute(null);
            model.SelectCell(2, 2);
        };
        model.Title = "expected";
        Assert.Equal("expected", model.Title);
        Assert.Equal("", model.ColumnWidths[0].Value);
        Assert.Equal("", model.Cells[0].Text);
        Assert.Equal(3, model.Definition.RowCount);
        Assert.Equal(new TableSelection(0, 0, 1, 1), model.Selection);
        Assert.Equal(1, model.UndoCount);
        Assert.True(undoNotifications > 0);
        var beforeSelection = mergeNotifications;
        model.SelectCell(1, 1, true);
        Assert.True(mergeNotifications > beforeSelection);
        Assert.True(model.MergeCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void UndoAndRedoKeepPreviewNewlinesAndSnippet(string newline)
    {
        var model = new TableDesignerViewModel(newline);
        model.Cells[0].Text = "第一行\n第二行";
        var expected = model.Preview;
        var snippet = model.Snippet;
        model.UndoCommand.Execute(null);
        model.RedoCommand.Execute(null);
        Assert.Equal(expected, model.Preview);
        Assert.Equal(snippet, model.Snippet);
        Assert.Contains("第一行" + newline + "第二行", model.Preview);
    }

    [Fact]
    public void FailedConfirmationRetainsHistoryAndDisablesCommandsWhileApplying()
    {
        TableDesignerViewModel? model = null;
        model = new(applyEdit: () =>
        {
            Assert.False(model!.CanUndo);
            Assert.NotEqual("reentrant", model.Title);
            model.Title = "reentrant";
            model.UndoCommand.Execute(null);
            return new(TableEditApplyStatus.Rejected, new(TableEditDiagnosticCode.InvalidTable, "適用できません"));
        });
        var closes = 0;
        model.CloseRequested += (_, _) => closes++;
        model.Cells[0].Text = "入力";
        model.ConfirmCommand.Execute(null);
        Assert.Equal(0, closes);
        Assert.StartsWith("適用できません", model.Error);
        Assert.True(model.CanUndo);
        Assert.Equal("", model.Title);
        model.UndoCommand.Execute(null);
        Assert.Equal("", model.Cells[0].Text);
        Assert.Empty(model.Error);
    }

    [Fact]
    public void ViewRebindsCellsAndWidthInputsWhenCommandsRestoreState() => Sta.Run(() =>
    {
        var model = new TableDesignerViewModel();
        var dialog = new TableDesignerDialog(model);
        var panel = (FrameworkElement)dialog.Content;
        panel.Measure(new Size(1080, 790));
        panel.Arrange(new Rect(0, 0, 1080, 790));
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var grid = Assert.IsType<Grid>(dialog.FindName("CellGrid"));
        var input = Descendants<TextBox>(grid).First();
        input.Text = "画面からの入力";
        model.SelectCell(0, 0); model.SelectCell(1, 1, true);
        model.MergeCommand.Execute(null);
        model.UndoCommand.Execute(null);
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Assert.Equal(9, grid.Children.OfType<Border>().Count());
        Assert.Equal(4, model.Cells.Count(c => c.IsSelected));
        Assert.Equal("画面からの入力", Descendants<TextBox>(grid).First().Text);
        model.UndoCommand.Execute(null);
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Assert.Equal("", Descendants<TextBox>(grid).First().Text);
        model.ColumnWidths[0].Value = "bad";
        model.UndoCommand.Execute(null);
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Assert.Equal("", model.ColumnWidths[0].Value);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        dialog.Close();
    });

    [Fact]
    public void ExistingTableUndoToOriginalKeepsDocumentHistoryThenRedoAppliesOnce() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load(TableLocatorTests.Table);
        editor.Replace(editor.Text.Length, 0, "\n");
        editor.Undo();
        using var documents = new DocumentSessionController(editor, new Utf8FileService());
        var service = new TableEditingService(editor, () => documents.Session);
        var context = service.BeginEdit().Context!;
        var model = new TableDesignerViewModel(definition: context.Definition, applyEdit: () => service.Apply(context));
        model.Title = "changed";
        model.UndoCommand.Execute(null);
        Assert.Equal(TableLocatorTests.Table, editor.Text);
        model.ConfirmCommand.Execute(null);
        Assert.False(editor.IsModified);
        Assert.True(editor.CanRedo);
        model.RedoCommand.Execute(null);
        model.ConfirmCommand.Execute(null);
        Assert.StartsWith(".changed", editor.Text);
        editor.Undo();
        Assert.Equal(TableLocatorTests.Table, editor.Text);
        Assert.False(editor.CanUndo);
    });

    [Fact]
    public void DesignerHistoryCanBeDiscardedWithoutChangingDocumentState() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load(TableLocatorTests.Table);
        editor.Select(4, 2);
        using var documents = new DocumentSessionController(editor, new Utf8FileService());
        var session = documents.Session;
        var service = new TableEditingService(editor, () => documents.Session);
        var context = service.BeginEdit().Context!;
        var model = new TableDesignerViewModel(definition: context.Definition);
        model.Title = "未適用";
        model.Cells[0].Text = "変更";
        model.UndoCommand.Execute(null);
        model.RedoCommand.Execute(null);
        // Closing/cancelling simply discards the working model. No document operation ran.
        Assert.Equal(TableLocatorTests.Table, editor.Text);
        Assert.Equal(session, documents.Session);
        Assert.False(editor.IsModified);
        Assert.False(editor.CanUndo);
        Assert.Equal(4, editor.SelectionStart);
        Assert.Equal(2, editor.SelectionLength);
    });

    [Fact]
    public void UndoAndRedoCannotBypassStaleDocumentCheckWhenApplying() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load(TableLocatorTests.Table);
        using var documents = new DocumentSessionController(editor, new Utf8FileService());
        var service = new TableEditingService(editor, () => documents.Session);
        var context = service.BeginEdit().Context!;
        var model = new TableDesignerViewModel(definition: context.Definition, applyEdit: () => service.Apply(context));
        var closes = 0;
        model.CloseRequested += (_, _) => closes++;
        model.Title = "変更";
        editor.Replace(editor.Text.Length, 0, "\n別の文書変更");
        var changedDocument = editor.Text;
        model.UndoCommand.Execute(null);
        model.RedoCommand.Execute(null);
        model.ConfirmCommand.Execute(null);
        Assert.Equal(0, closes);
        Assert.Contains("開き直して", model.Error);
        Assert.Equal(changedDocument, editor.Text);
        Assert.True(model.CanUndo);
        model.UndoCommand.Execute(null);
        model.ConfirmCommand.Execute(null);
        Assert.Equal(0, closes);
        Assert.Equal(changedDocument, editor.Text);
    });

    [Fact]
    public void MaximumSizeAndLargeCellSurviveResizeUndoWithBindingsRecreated()
    {
        var table = new TableDefinition(100, 50);
        var text = new string('あ', 128 * 1024);
        table.CellAt(99, 49).Text = text;
        var model = new TableDesignerViewModel(definition: table);
        model.SelectCell(99, 49);
        model.RowsInput = "1"; model.ColumnsInput = "1";
        model.ResizeCommand.Execute(null);
        Assert.Single(model.Cells);
        model.UndoCommand.Execute(null);
        Assert.Equal(5000, model.Cells.Count);
        Assert.Equal(50, model.ColumnWidths.Count);
        Assert.Equal(text, Cell(model, 99, 49).Text);
        Assert.Equal(new TableSelection(99, 49, 1, 1), model.Selection);
        model.RedoCommand.Execute(null);
        Assert.Single(model.Cells);
        Assert.Empty(model.Error);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
