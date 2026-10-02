using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class TableEditHistoryTests
{
    private static TableDesignerSnapshot Capture(TableDefinition table) =>
        TableDesignerSnapshot.Capture(table, new(0, 0, table.RowCount, table.ColumnCount), 0, 0);

    private static bool Change(TableEditHistory history, TableDefinition table, string description, Action action) =>
        history.Execute(description, Capture(table), () => { action(); return Capture(table); });

    [Fact]
    public void EmptyHistoryAndClearDoNotChangeTheTable()
    {
        var table = new TableDefinition();
        var history = new TableEditHistory(table);
        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
        Assert.Null(history.UndoDescription);
        Assert.Null(history.RedoDescription);
        Change(history, table, "入力", () => table.Title = "saved");
        history.Clear();
        Assert.Equal("saved", table.Title);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.EstimatedBytes);
        Change(history, table, "再開", () => table.Title = "new");
        history.Undo();
        Assert.Equal("saved", table.Title);
    }

    [Fact]
    public void MixedEditsRestoreAllStatesInReverseAndForwardOrder()
    {
        var table = new TableDefinition(4, 4) { HasHeader = true, Title = "original" };
        foreach (var cell in table.Cells) cell.Text = $"{cell.Row}:{cell.Column} 日本語😀\r\n ";
        table.SetColumnWidth(2, "3");
        var history = new TableEditHistory(table);
        var states = new List<TableSnapshot> { TableSnapshot.Capture(table) };
        Action[] actions =
        [
            () => table.CellAt(1, 1).Text = "追加本文",
            () => table.Merge(new(1, 1, 2, 2)),
            () => table.DeleteRow(2),
            () => table.DeleteColumn(1),
            () => table.InsertRow(1),
            () => table.InsertColumn(1),
            () => table.Resize(2, 2),
            () => table.HasHeader = false,
            () => table.Title = "changed",
            () => table.SetColumnWidth(0, "5")
        ];
        foreach (var action in actions)
        {
            Assert.True(Change(history, table, "変更", action));
            states.Add(TableSnapshot.Capture(table));
        }
        var bytes = history.EstimatedBytes;
        for (var i = states.Count - 2; i >= 0; i--)
        {
            Assert.NotNull(history.Undo());
            Assert.True(states[i].ContentEquals(TableSnapshot.Capture(table)));
            table.Validate();
        }
        Assert.False(history.CanUndo);
        Assert.Equal(actions.Length, history.RedoCount);
        for (var i = 1; i < states.Count; i++)
        {
            Assert.NotNull(history.Redo());
            Assert.True(states[i].ContentEquals(TableSnapshot.Capture(table)));
        }
        Assert.Equal(bytes, history.EstimatedBytes);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoMergeRecoversIndividualTextsAndSelectionAnchor()
    {
        var table = new TableDefinition(2, 2);
        table.CellAt(0, 0).Text = "A";
        table.CellAt(0, 1).Text = "B";
        table.CellAt(1, 0).Text = "C";
        table.CellAt(1, 1).Text = "D";
        var history = new TableEditHistory(table);
        var before = TableDesignerSnapshot.Capture(table, new(0, 0, 2, 2), 1, 1);
        history.Execute("セル結合", before, () => { table.Merge(before.Selection); return Capture(table); });
        Assert.Equal("A\nB\nC\nD", table.CellAt(0, 0).Text);
        Assert.Equal("セル結合", history.UndoDescription);
        Assert.Same(before, history.Undo());
        Assert.Equal(new[] { "A", "B", "C", "D" }, table.Cells.Select(c => c.Text));
        Assert.Equal("セル結合", history.RedoDescription);
        history.Redo();
        Assert.Single(table.Cells);
        Assert.Equal("A\nB\nC\nD", table.Cells[0].Text);
    }

    [Fact]
    public void UndoUnmergeRestoresSpansAndText()
    {
        var table = new TableDefinition(3, 2);
        table.Merge(new(0, 0, 3, 2));
        table.CellAt(0, 0).Text = "結合済み";
        var history = new TableEditHistory(table);
        Change(history, table, "結合解除", () => table.Unmerge(new(0, 0, 3, 2)));
        Assert.Equal(6, table.Cells.Count);
        history.Undo();
        Assert.Single(table.Cells);
        Assert.Equal(3, table.Cells[0].RowSpan);
        Assert.Equal(2, table.Cells[0].ColumnSpan);
        Assert.Equal("結合済み", table.Cells[0].Text);
        history.Redo();
        Assert.Equal(5, table.Cells.Count(c => c.Text == ""));
    }

    [Fact]
    public void NoOpAndNavigationPreserveRedoButNewEditBranches()
    {
        var table = new TableDefinition(2, 2);
        var history = new TableEditHistory(table);
        Change(history, table, "A", () => table.Title = "A");
        Change(history, table, "B", () => table.Title = "B");
        history.Undo();
        var bytes = history.EstimatedBytes;
        Assert.False(Change(history, table, "同じ値", () => table.Title = "A"));
        var moved = TableDesignerSnapshot.Capture(table, new(1, 1, 1, 1), 1, 1);
        Assert.False(history.Record("選択移動", Capture(table), moved));
        Assert.Equal(bytes, history.EstimatedBytes);
        Assert.Equal("B", history.RedoDescription);
        history.Execute("C", moved, () => { table.Title = "C"; return moved = Capture(table); });
        Assert.False(history.CanRedo);
        Assert.Equal(2, history.UndoCount);
        history.Undo();
        Assert.Equal("A", table.Title);
        history.Redo();
        Assert.Equal("C", table.Title);
    }

    [Fact]
    public void FailedCompositeOperationRollsBackAndKeepsBothHistoryBranches()
    {
        var table = new TableDefinition(2, 2) { HasHeader = true };
        var history = new TableEditHistory(table);
        Change(history, table, "タイトル", () => table.Title = "A");
        Change(history, table, "タイトル", () => table.Title = "B");
        history.Undo();
        var before = Capture(table);
        var bytes = history.EstimatedBytes;
        Assert.Throws<ArgumentException>(() => Change(history, table, "複合変更", () =>
        {
            table.Resize(3, 3);
            table.CellAt(0, 0).Text = "partial";
            table.Merge(new(0, 0, 2, 2)); // Header boundary error after earlier changes.
        }));
        Assert.True(before.ContentEquals(Capture(table)));
        Assert.Equal(bytes, history.EstimatedBytes);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
        history.Redo();
        Assert.Equal("B", table.Title);
    }

    [Fact]
    public void DraftMetadataAndSizeInputHaveUndoEvenWhenAsciiDocCannotBeGenerated()
    {
        var table = new TableDefinition(2, 2);
        var history = new TableEditHistory(table);
        var before = Capture(table);
        table.SetColumnWidth(1, "bad");
        var draft = TableDesignerSnapshot.Capture(table, new(0, 0, 2, 2), 0, 0, "", "abc");
        Assert.True(history.Record("入力", before, draft));
        Assert.Throws<ArgumentException>(() => AsciiDocTableGenerator.Generate(table));
        Assert.Same(before, history.Undo());
        Assert.Equal("", table.ColumnWidths[1]);
        Assert.Same(draft, history.Redo());
        Assert.Equal("bad", table.ColumnWidths[1]);
        Assert.Equal("", draft.RowsInput);
        Assert.Equal("abc", draft.ColumnsInput);
        table.SetColumnWidth(1, "2");
        Assert.True(history.Record("入力修正", draft, Capture(table)));
        Assert.NotEmpty(AsciiDocTableGenerator.Generate(table));
    }

    [Fact]
    public void SizeDraftAloneIsAnEditAndCanBeUndoneBeforeApplyingResize()
    {
        var table = new TableDefinition();
        var history = new TableEditHistory(table);
        var before = Capture(table);
        var draft = TableDesignerSnapshot.Capture(table, before.Selection, 0, 0, "4", "3");
        Assert.True(history.Record("行数入力", before, draft));
        history.Execute("サイズ変更", draft, () => { table.Resize(4, 3); return Capture(table); });
        var restored = history.Undo();
        Assert.Equal(3, table.RowCount);
        Assert.Equal("4", restored!.RowsInput);
        Assert.Equal("3", history.Undo()!.RowsInput);
    }

    [Fact]
    public void UnrecordedChangesCannotBeSilentlyOverwrittenByUndoOrRedo()
    {
        var table = new TableDefinition();
        var history = new TableEditHistory(table);
        Change(history, table, "タイトル", () => table.Title = "A");
        table.Title = "unrecorded";
        Assert.Throws<InvalidOperationException>(() => history.Undo());
        Assert.Equal("unrecorded", table.Title);
        Assert.Equal(1, history.UndoCount);
        table.Title = "A";
        history.Undo();
        table.Title = "another edit";
        Assert.Throws<InvalidOperationException>(() => history.Redo());
        Assert.Equal("another edit", table.Title);
        Assert.Equal(1, history.RedoCount);
    }

    [Fact]
    public void DiscontinuousHistoryAndIncorrectAfterStateAreRejectedWithoutLosingRedo()
    {
        var table = new TableDefinition();
        var history = new TableEditHistory(table);
        var initial = Capture(table);
        Change(history, table, "A", () => table.Title = "A");
        var stateA = Capture(table);
        history.Undo();
        Assert.Throws<InvalidOperationException>(() => history.Record("不一致", stateA, initial));
        Assert.Throws<InvalidOperationException>(() => history.Record("不一致", initial, stateA));
        Assert.Equal(1, history.RedoCount);
        history.Redo();
        Assert.Equal("A", table.Title);
    }

    [Fact]
    public void IncorrectCaptureOrReentrantOperationRollsBackExecute()
    {
        var table = new TableDefinition();
        var history = new TableEditHistory(table);
        var before = Capture(table);
        Assert.Throws<InvalidOperationException>(() => history.Execute("不正な取得", before, () =>
        {
            table.Title = "mutated";
            return before;
        }));
        Assert.True(before.ContentEquals(Capture(table)));
        Assert.Throws<InvalidOperationException>(() => history.Execute("再入", before, () =>
        {
            Assert.False(history.CanUndo);
            Assert.False(history.CanRedo);
            table.Resize(1, 1);
            history.Clear();
            return Capture(table);
        }));
        Assert.True(before.ContentEquals(Capture(table)));
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void EntryLimitDropsOldestEditsAndBranchingAfterAllUndosStillWorks()
    {
        var table = new TableDefinition(1, 1);
        var history = new TableEditHistory(table, maxEntries: 2);
        foreach (var title in new[] { "A", "B", "C" })
            Change(history, table, title, () => table.Title = title);
        Assert.Equal(2, history.UndoCount);
        history.Undo(); history.Undo();
        Assert.Equal("A", table.Title);
        Assert.Null(history.Undo());
        Change(history, table, "D", () => table.Title = "D");
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        history.Undo();
        Assert.Equal("A", table.Title);
    }

    [Fact]
    public void ByteBudgetCountsUndoAndRedoAndTrimsOldestEntries()
    {
        var table = new TableDefinition(1, 1) { Title = "A" };
        var probe = new TableEditHistory(table);
        Change(probe, table, "title", () => table.Title = "B");
        var entryBytes = probe.EstimatedBytes;
        var history = new TableEditHistory(table, maxEstimatedBytes: entryBytes * 2);
        foreach (var title in new[] { "C", "D", "E" })
            Change(history, table, "title", () => table.Title = title);
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(entryBytes * 2, history.EstimatedBytes);
        history.Undo(); history.Undo();
        Assert.Equal("C", table.Title);
        Assert.Equal(entryBytes * 2, history.EstimatedBytes);
        history.Clear();
        Assert.Equal(0, history.EstimatedBytes);
    }

    [Fact]
    public void OversizedEntryRetainsLatestUndoAndCanBeReplacedBySmallerHistory()
    {
        var table = new TableDefinition(100, 50);
        table.CellAt(99, 49).Text = new string('あ', 128 * 1024);
        var initial = TableSnapshot.Capture(table);
        var history = new TableEditHistory(table, maxEstimatedBytes: 4096);
        Change(history, table, "大きな表の削除", () => table.Resize(1, 1));
        Assert.True(history.EstimatedBytes > history.MaxEstimatedBytes);
        Assert.Equal(1, history.UndoCount);
        history.Undo();
        Assert.True(initial.ContentEquals(TableSnapshot.Capture(table)));
        table.Validate();
        history.Redo();
        Change(history, table, "タイトル", () => table.Title = "small");
        Assert.Equal(1, history.UndoCount);
        Assert.True(history.EstimatedBytes <= history.MaxEstimatedBytes);
    }

    [Fact]
    public void DefaultLimitIsOneHundredOperations()
    {
        var table = new TableDefinition(1, 1);
        var history = new TableEditHistory(table);
        for (var i = 1; i <= 105; i++) Change(history, table, "入力", () => table.Title = i.ToString());
        Assert.Equal(100, history.UndoCount);
        Assert.Equal(32L * 1024 * 1024, history.MaxEstimatedBytes);
        while (history.CanUndo) history.Undo();
        Assert.Equal("5", table.Title);
    }

    [Fact]
    public void InvalidArgumentsDoNotMutateTableOrHistory()
    {
        var table = new TableDefinition();
        Assert.Throws<ArgumentNullException>(() => new TableEditHistory(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableEditHistory(table, maxEntries: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableEditHistory(table, maxEstimatedBytes: 0));
        var history = new TableEditHistory(table);
        Assert.Throws<ArgumentException>(() => Change(history, table, " ", () => table.Title = "changed"));
        Assert.Throws<ArgumentNullException>(() => history.Execute("操作", Capture(table), null!));
        Assert.Equal("", table.Title);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void RestoredWorkingModelRemainsTheModelUsedByTableEditingService() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        editor.Load(TableLocatorTests.Table);
        using var documents = new DocumentSessionController(editor, new Utf8FileService());
        var service = new TableEditingService(editor, () => documents.Session);
        var context = service.BeginEdit().Context!;
        var working = context.Definition;
        var history = new TableEditHistory(working);
        Change(history, working, "タイトル", () => working.Title = "changed");
        history.Undo();
        Assert.Same(working, context.Definition);
        Assert.Equal(TableEditApplyStatus.Unchanged, service.Apply(context).Status);
        Assert.False(editor.CanUndo);
        history.Redo();
        Assert.Equal(TableEditApplyStatus.Applied, service.Apply(context).Status);
        Assert.StartsWith(".changed", editor.Text);
        editor.Undo();
        Assert.Equal(TableLocatorTests.Table, editor.Text);
        Assert.False(editor.CanUndo);
    });
}
