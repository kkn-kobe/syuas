using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class TableEditingUiTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void CommandLoadsExistingModelAppliesOnceAndReturnsFocus(string nl) => Sta.Run(() =>
    {
        var original = "= 文書" + nl + nl + "[[table-id]]" + nl + TableLocatorTests.Table.Replace("\n", nl) + nl;
        using var f = new Fixture(original);
        f.Editor.Select(original.IndexOf("日本語", StringComparison.Ordinal), 1);
        var closed = 0;
        f.TableDialogs.Action = model =>
        {
            Assert.True(f.Model.IsTableEditing);
            Assert.False(f.Model.EditTableCommand.CanExecute(null));
            Assert.True(model.IsEditing);
            Assert.Equal("適用", model.ConfirmLabel);
            Assert.Contains("再編集", model.DialogTitle);
            Assert.Contains("4行目", model.EditHint);
            Assert.Equal("一覧", model.Title);
            Assert.Equal(2, model.Definition.RowCount);
            Assert.Equal("日本語 😀", model.Cells[0].Text);
            model.CloseRequested += (_, _) => closed++;
            model.Title = "更新済み";
            model.SelectCell(0, 0); model.SelectCell(1, 0, true);
            model.MergeCommand.Execute(null);
            model.Cells[0].Text = "更新した本文";
            model.ConfirmCommand.Execute(null);
            Assert.Equal(1, closed);
        };
        f.Model.EditTableCommand.Execute(null);
        Assert.Contains(".2+|更新した本文", f.Editor.Text);
        Assert.StartsWith("= 文書" + nl + nl + "[[table-id]]" + nl + ".更新済み", f.Editor.Text);
        Assert.EndsWith("|===" + nl, f.Editor.Text);
        Assert.True(f.Model.Session.IsModified);
        Assert.Equal(1, f.FocusRequests);
        Assert.False(f.Model.IsTableEditing);
        Assert.True(f.Model.EditTableCommand.CanExecute(null));
        f.Model.UndoCommand.Execute(null);
        Assert.Equal(original, f.Editor.Text);
        Assert.False(f.Editor.CanUndo);
        Assert.False(f.Model.Session.IsModified);
        f.Model.RedoCommand.Execute(null);
        Assert.Contains("更新した本文", f.Editor.Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelAndUnchangedConfirmationPreserveSelectionDirtyStateAndRedo(bool confirm) => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.Editor.Replace(f.Editor.Text.Length, 0, "\n");
        f.Editor.Undo();
        f.Editor.Select(1, 2);
        var before = Snapshot(f.Editor);
        f.TableDialogs.Action = model =>
        {
            model.Title = "temporary";
            if (confirm)
            {
                model.Title = "一覧";
                var closes = 0;
                model.CloseRequested += (_, _) => closes++;
                model.ConfirmCommand.Execute(null);
                Assert.Equal(1, closes);
            }
        };
        f.Model.EditTableCommand.Execute(null);
        Assert.Equal(before, Snapshot(f.Editor));
        Assert.Equal(1, f.FocusRequests);
    });

    [Theory]
    [InlineData("plain text")]
    [InlineData("----\n[cols=\"1*\",options=\"noheader\"]\n|===\n|code\n|===\n----")]
    [InlineData("[cols=\"1*\",options=\"noheader\"]\n|===\na|unsupported\n|===")]
    public void UneditableSourceShowsReasonWithoutOpeningDesigner(string source) => Sta.Run(() =>
    {
        using var f = new Fixture(source);
        var before = Snapshot(f.Editor);
        f.Model.EditTableCommand.Execute(null);
        Assert.Equal(0, f.TableDialogs.Shown);
        var message = Assert.Single(f.Dialogs.Information);
        Assert.Contains("表を再編集できませんでした", message);
        Assert.Contains("場所:", message);
        Assert.Equal(before, Snapshot(f.Editor));
        Assert.False(f.Model.IsTableEditing);
        Assert.Equal(1, f.FocusRequests);
    });

    [Fact]
    public void FailedApplyKeepsWorkingModelAndCanBeCorrectedBeforeClosing() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.TableDialogs.Action = model =>
        {
            var closes = 0;
            model.CloseRequested += (_, _) => closes++;
            model.Cells[0].Text = "include::file.adoc[]";
            model.ConfirmCommand.Execute(null);
            Assert.Equal(0, closes);
            Assert.Contains("include", model.Error);
            Assert.Contains("場所:", model.Error);
            Assert.Equal("include::file.adoc[]", model.Cells[0].Text);
            Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
            Assert.True(f.Model.IsTableEditing);
            model.Cells[0].Text = "修正した内容";
            Assert.Empty(model.Error);
            model.ConfirmCommand.Execute(null);
            Assert.Equal(1, closes);
        };
        f.Model.EditTableCommand.Execute(null);
        Assert.Contains("修正した内容", f.Editor.Text);
        Assert.Empty(f.Dialogs.Information);
    });

    [Fact]
    public void DocumentChangedDuringDialogReportsReopenAndDoesNotCloseOrOverwrite() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.TableDialogs.Action = model =>
        {
            var closes = 0;
            model.CloseRequested += (_, _) => closes++;
            model.Cells[0].Text = "working edit";
            f.Editor.Replace(f.Editor.Text.Length, 0, "\nexternal change to editor");
            var changed = f.Editor.Text;
            model.ConfirmCommand.Execute(null);
            Assert.Equal(0, closes);
            Assert.Contains("開き直して", model.Error);
            Assert.Equal("working edit", model.Cells[0].Text);
            Assert.Equal(changed, f.Editor.Text);
        };
        f.Model.EditTableCommand.Execute(null);
        Assert.False(f.Model.IsTableEditing);
        Assert.Equal(1, f.FocusRequests);
    });

    [Fact]
    public void ReentrantCommandsAndDocumentTransitionsAreBlockedWhileDialogIsOpen() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.TableDialogs.Action = _ =>
        {
            f.Model.EditTableCommand.Execute(null);
            Assert.Equal(1, f.TableDialogs.Shown);
            Assert.False(f.Model.CanClose());
            Assert.False(f.Model.New());
            Assert.False(f.Model.Open("unused.adoc"));
            Assert.False(f.Model.Save());
            Assert.False(f.Model.ReloadFromDisk());
        };
        f.Model.EditTableCommand.Execute(null);
        Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
        Assert.Equal(0, f.Dialogs.Confirmations);
    });

    [Fact]
    public void CommandRequiresDialogProviderAndCannotExecuteAfterDispose() => Sta.Run(() =>
    {
        using var editor = new AvalonEditAdapter(new TextEditor());
        using var model = new MainViewModel(editor, new Utf8FileService(), new Dialogs(), new History());
        Assert.False(model.EditTableCommand.CanExecute(null));
        model.EditTableCommand.Execute(null);
        using var f = new Fixture(TableLocatorTests.Table);
        f.Model.Dispose();
        Assert.False(f.Model.EditTableCommand.CanExecute(null));
        f.Model.EditTableCommand.Execute(null);
        Assert.Equal(0, f.TableDialogs.Shown);
    });

    [Fact]
    public void DialogFailureStillReleasesBusyState() => Sta.Run(() =>
    {
        using var f = new Fixture(TableLocatorTests.Table);
        f.TableDialogs.Action = _ => throw new InvalidOperationException("test dialog failure");
        Assert.Throws<InvalidOperationException>(() => f.Model.EditTableCommand.Execute(null));
        Assert.False(f.Model.IsTableEditing);
        Assert.True(f.Model.EditTableCommand.CanExecute(null));
        Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
    });

    [Fact]
    public void CreateModeStillInsertsAndInvalidInputCannotSubmit()
    {
        var model = new TableDesignerViewModel();
        var closes = 0;
        model.CloseRequested += (_, _) => closes++;
        Assert.False(model.IsEditing);
        Assert.Equal("挿入", model.ConfirmLabel);
        Assert.Equal("表デザイナー", model.DialogTitle);
        model.RowsInput = "invalid";
        Assert.False(model.ConfirmCommand.CanExecute(null));
        model.ConfirmCommand.Execute(null);
        Assert.Equal(0, closes);
        model.RowsInput = "3";
        model.InsertCommand.Execute(null);
        Assert.Equal(1, closes);
        Assert.NotNull(model.Snippet);
    }

    [Fact]
    public void RecoveryCloseDisablesEditingCommandAndRaisesCanExecuteChanged() => Sta.RunAsync(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var f = new Fixture(TableLocatorTests.Table);
            f.Model.EnableRecovery(new RecoveryService(new RecoveryStore(root)));
            var notifications = 0;
            f.Model.EditTableCommand.CanExecuteChanged += (_, _) => notifications++;
            await f.Model.CloseRecoveryAsync();
            Assert.True(f.Model.IsRecoveryBusy);
            Assert.True(notifications > 0);
            Assert.False(f.Model.EditTableCommand.CanExecute(null));
            f.Model.EditTableCommand.Execute(null);
            Assert.Equal(0, f.TableDialogs.Shown);
            Assert.Equal(TableLocatorTests.Table, f.Editor.Text);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    });

    private static object Snapshot(AvalonEditAdapter editor)
        => (editor.Text, editor.ContentRevision, editor.IsModified, editor.SelectionStart, editor.SelectionLength, editor.CaretOffset, editor.CanUndo, editor.CanRedo);

    private sealed class Fixture : IDisposable
    {
        public AvalonEditAdapter Editor { get; } = new(new TextEditor());
        public MainViewModel Model { get; }
        public Dialogs Dialogs { get; } = new();
        public TableDialogs TableDialogs { get; } = new();
        public int FocusRequests { get; private set; }
        public Fixture(string source)
        {
            Editor.Load(source);
            Model = new(Editor, new Utf8FileService(), Dialogs, new History(), tableDialogs: TableDialogs);
            Model.EditorFocusRequested += (_, _) => FocusRequests++;
        }
        public void Dispose() { Model.Dispose(); Editor.Dispose(); }
    }
    private sealed class TableDialogs : ITableEditingDialogs
    {
        public int Shown { get; private set; }
        public Action<TableDesignerViewModel>? Action { get; set; }
        public void Show(TableDesignerViewModel model) { Shown++; Action?.Invoke(model); }
    }
    private sealed class Dialogs : IUserDialogs
    {
        public List<string> Information { get; } = [];
        public int Confirmations { get; private set; }
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => null;
        public SaveDecision ConfirmSave(string documentName) { Confirmations++; return SaveDecision.Cancel; }
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile) => SaveConflictDecision.Cancel;
        public void ShowError(string message) => throw new InvalidOperationException(message);
        public void ShowInformation(string message) => Information.Add(message);
    }
    private sealed class History : IRecentFilesStore
    {
        public IReadOnlyList<string> Load() => [];
        public void Save(IReadOnlyList<string> paths) { }
    }
}
