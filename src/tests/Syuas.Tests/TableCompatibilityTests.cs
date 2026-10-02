using System.Text;
using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class TableCompatibilityTests
{
    public static IEnumerable<object[]> SavedVariants() =>
        from example in TableCompatibilityFixtures.All
        from newLine in new[] { "\n", "\r\n" }
        from bom in new[] { false, true }
        select new object[] { example.Name, newLine, bom };

    [Theory]
    [MemberData(nameof(SavedVariants))]
    public void SavedFixturesReopenEditSaveAndUndoWithoutChangingSurroundings(string name, string newLine, bool bom) => Sta.Run(() =>
    {
        var example = Assert.Single(TableCompatibilityFixtures.All, e => e.Name == name);
        var table = TableCompatibilityFixtures.Read(name).Replace("\n", newLine);
        TableCompatibilityFixtures.ParseAndVerify(example, table);
        var prefix = "= 保存サンプル" + newLine + newLine + "[[table-id]]" + newLine + "// 保持するコメント" + newLine;
        var suffix = newLine + "表の後の文章" + newLine;
        var source = prefix + table + suffix;
        using var f = new Fixture(source, bom);
        f.Edit(model => model.ConfirmCommand.Execute(null));
        Assert.False(f.Editor.IsModified);
        Assert.False(f.Editor.CanUndo);
        Assert.Equal(source, f.Editor.Text);
        f.Edit(model =>
        {
            Assert.False(model.CanUndo); Assert.False(model.CanRedo);
            var original = model.Cells[0].Text;
            model.BeginTextEdit(new(new(TableInputKind.Cell), original.Length, 0));
            model.Cells[0].Text += " 更"; model.Cells[0].Text += "新";
            model.AddRowCommand.Execute(null);
            model.UndoCommand.Execute(null); // Discard the extra row, retain grouped input.
            model.UndoCommand.Execute(null);
            Assert.Equal(original, model.Cells[0].Text);
            model.RedoCommand.Execute(null);
            Assert.Equal(1, model.UndoCount); Assert.Equal(1, model.RedoCount);
            model.ConfirmCommand.Execute(null);
        });
        var updated = f.Editor.Text;
        Assert.StartsWith(prefix, updated);
        Assert.EndsWith(suffix, updated);
        Assert.Equal(source, File.ReadAllText(f.Path));
        f.Editor.Undo();
        Assert.Equal(source, f.Editor.Text);
        Assert.False(f.Editor.IsModified);
        f.Editor.Redo();
        Assert.Equal(updated, f.Editor.Text);
        Assert.True(f.Model.Save());
        Assert.False(f.Editor.IsModified);
        Assert.Equal(updated, File.ReadAllText(f.Path));
        Assert.False(File.ReadAllBytes(f.Path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        f.Restart();
        Assert.True(f.Model.Open(f.Path));
        f.Edit(model =>
        {
            Assert.False(model.CanUndo); Assert.False(model.CanRedo);
            Assert.Equal(example.Rows, model.Definition.RowCount);
            Assert.Equal(example.Columns, model.Definition.ColumnCount);
            Assert.Equal(example.Header, model.HasHeader);
            Assert.Equal(example.Cells[0].Source + " 更新", model.Cells[0].Text);
            Assert.Equal(updated, f.Editor.Text);
        });
        Assert.Empty(f.Dialogs.Information);
    });

    [Theory]
    [InlineData("implicit-header")]
    [InlineData("csv")]
    [InlineData("styled-cell")]
    [InlineData("include")]
    [InlineData("multiple-cells")]
    public void UnsupportedSavedFixturesLeaveBytesAndEditorUntouched(string name) => Sta.Run(() =>
    {
        var source = TableCompatibilityFixtures.Read("unsupported/" + name);
        Assert.False(AsciiDocTableParser.Parse(source).Succeeded);
        using var f = new Fixture(source, true);
        var bytes = File.ReadAllBytes(f.Path);
        var revision = f.Editor.ContentRevision;
        f.Edit(_ => Assert.Fail("Unsupported input must not open the designer."));
        Assert.Equal(source, f.Editor.Text);
        Assert.Equal(revision, f.Editor.ContentRevision);
        Assert.False(f.Editor.CanUndo);
        Assert.False(f.Editor.IsModified);
        Assert.Equal(bytes, File.ReadAllBytes(f.Path));
        Assert.Contains("場所:", Assert.Single(f.Dialogs.Information));
    });

    [Theory]
    [InlineData("unchanged")]
    [InlineData("modified")]
    [InlineData("missing")]
    public void AppliedTableSurvivesRecoveryAndCanBeEditedAgainWithOriginalSaveBaseline(string diskState) => Sta.RunAsync(async () =>
    {
        using var f = new Fixture(TableCompatibilityFixtures.Read("header"));
        var baseline = f.Model.Session.Baseline;
        var documentId = f.Model.Session.DocumentId;
        f.Edit(model =>
        {
            model.Cells[3].Text = "復元する表";
            model.DeleteRowCommand.Execute(null);
            model.UndoCommand.Execute(null);
            model.UndoCommand.Execute(null); model.RedoCommand.Execute(null);
            model.ConfirmCommand.Execute(null);
        });
        var draft = f.Editor.Text;
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        if (diskState == "modified") File.WriteAllText(f.Path, "外部の文書");
        if (diskState == "missing") File.Delete(f.Path);
        f.Restart(); // Dispose without confirmed close; a new editor has no old Undo history.
        var candidate = Assert.Single(await f.Model.ListRecoveryAsync());
        Assert.Equal(draft, candidate.Snapshot!.Text);
        Assert.True(await f.Model.RestoreRecoveryAsync(candidate.Key));
        Assert.Equal(documentId, f.Model.Session.DocumentId);
        Assert.Equal(baseline, f.Model.Session.Baseline);
        Assert.False(f.Editor.CanUndo);
        f.Edit(model =>
        {
            Assert.False(model.CanUndo); Assert.False(model.CanRedo);
            Assert.Equal("復元する表", model.Cells[3].Text);
            Assert.Equal(2, model.Cells[3].RowSpan);
            model.Cells[3].Text = "復元後に再編集";
            model.UndoCommand.Execute(null);
            Assert.Equal("復元する表", model.Cells[3].Text);
            model.RedoCommand.Execute(null);
            model.ConfirmCommand.Execute(null);
        });
        f.Editor.Undo();
        Assert.Equal(draft, f.Editor.Text);
        Assert.True(f.Editor.IsModified);
        f.Editor.Redo();
        Assert.Equal(baseline, f.Model.Session.Baseline);
        Assert.Equal(diskState == "unchanged", f.Model.Save());
        if (diskState == "unchanged")
        {
            Assert.Equal(f.Editor.Text, File.ReadAllText(f.Path));
            await f.Recovery.DrainAsync();
            f.Restart();
            Assert.Empty(await f.Model.ListRecoveryAsync());
        }
        else
        {
            Assert.Single(f.Dialogs.Conflicts);
            Assert.True(f.Editor.IsModified);
            Assert.Equal(diskState != "missing", File.Exists(f.Path));
            if (diskState == "modified") Assert.Equal("外部の文書", File.ReadAllText(f.Path));
        }
    });

    [Fact]
    public void CancelledDesignerContentDoesNotEnterRecoveryCopy() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture(TableCompatibilityFixtures.Read("basic"));
        f.Editor.Replace(f.Editor.Text.Length, 0, "\n退避対象の本文\n");
        var draft = f.Editor.Text;
        Task? backup = null;
        f.Clock.Advance(5);
        f.Edit(model =>
        {
            model.Cells[0].Text = "未適用の内容";
            model.SelectCell(0, 0); model.SelectCell(1, 1, true);
            model.MergeCommand.Execute(null);
            model.UndoCommand.Execute(null); model.RedoCommand.Execute(null);
            Assert.True(f.Model.IsTableEditing);
            backup = f.Model.TickRecoveryAsync(); // Capture while the designer is still open.
        });
        Assert.Equal(draft, f.Editor.Text);
        Assert.NotNull(backup);
        await backup;
        f.Restart();
        var candidate = Assert.Single(await f.Model.ListRecoveryAsync());
        Assert.Equal(draft, candidate.Snapshot!.Text);
        Assert.DoesNotContain("未適用の内容", candidate.Snapshot.Text);
    });

    [Theory]
    [InlineData("cancel")]
    [InlineData("save-as")]
    [InlineData("backup")]
    public void ExternalWriteDuringDesignerIsDetectedAndResolvedThroughNormalSave(string decision) => Sta.RunAsync(async () =>
    {
        using var f = new Fixture(TableCompatibilityFixtures.Read("spans"));
        var baseline = f.Model.Session.Baseline;
        const string external = "外部で更新した内容\n";
        f.Edit(model =>
        {
            File.WriteAllText(f.Path, external);
            model.Cells[0].Text = "ローカルで再編集";
            model.AddColumnCommand.Execute(null);
            model.UndoCommand.Execute(null);
            model.UndoCommand.Execute(null); model.RedoCommand.Execute(null);
            model.ConfirmCommand.Execute(null);
        });
        var local = f.Editor.Text;
        f.Editor.Undo();
        Assert.Equal(TableCompatibilityFixtures.Read("spans"), f.Editor.Text);
        f.Editor.Redo();
        Assert.Equal(local, f.Editor.Text);
        Assert.Equal(baseline, f.Model.Session.Baseline);
        await f.Model.CheckExternalChangesAsync(true);
        Assert.True(f.Model.IsExternalChangeVisible);
        Assert.Equal(local, f.Editor.Text);
        f.Model.DismissExternalChangeCommand.Execute(null);
        Assert.False(f.Model.Save()); // Dismissing a notification does not bypass the save guard.
        Assert.Single(f.Dialogs.Conflicts);
        Assert.Equal(external, File.ReadAllText(f.Path));
        Assert.Equal(local, f.Editor.Text);
        if (decision == "cancel") return;
        if (decision == "save-as")
        {
            f.Dialogs.SavePath = Path.Combine(f.Root, "別名.adoc");
            Assert.True(f.Model.Save(true));
            Assert.Equal(external, File.ReadAllText(f.Path));
            Assert.Equal(local, File.ReadAllText(f.Dialogs.SavePath));
        }
        else
        {
            f.Dialogs.ConflictDecision = SaveConflictDecision.OverwriteWithBackup;
            Assert.True(f.Model.Save());
            Assert.Equal(local, File.ReadAllText(f.Path));
            var backup = Assert.Single(Directory.GetFiles(f.Root), path => path != f.Path);
            Assert.Equal(external, File.ReadAllText(backup));
        }
        Assert.False(f.Editor.IsModified);
        Assert.False(f.Model.HasExternalChange);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CancelOrUndoToOriginalPreservesDocumentRedoBaselineAndRecovery(bool dirty, bool confirm) => Sta.RunAsync(async () =>
    {
        var original = TableCompatibilityFixtures.Read("basic");
        using var f = new Fixture(original);
        if (dirty) f.Editor.Replace(f.Editor.Text.Length, 0, "\n退避する本文\n");
        var expected = f.Editor.Text;
        f.Editor.Replace(f.Editor.Text.Length, 0, "やり直しで戻す本文");
        f.Editor.Undo();
        var revision = f.Editor.ContentRevision;
        var baseline = f.Model.Session.Baseline;
        var documentId = f.Model.Session.DocumentId;
        f.Edit(model =>
        {
            var selection = (f.Editor.SelectionStart, f.Editor.SelectionLength);
            model.Title = "未適用のタイトル";
            model.DeleteColumnCommand.Execute(null);
            model.UndoCommand.Execute(null); model.RedoCommand.Execute(null);
            if (confirm)
            {
                while (model.CanUndo) model.UndoCommand.Execute(null);
                model.ConfirmCommand.Execute(null);
            }
            Assert.Equal(selection, (f.Editor.SelectionStart, f.Editor.SelectionLength));
        });
        Assert.Equal(expected, f.Editor.Text);
        Assert.Equal(revision, f.Editor.ContentRevision);
        Assert.Equal(baseline, f.Model.Session.Baseline);
        Assert.Equal(documentId, f.Model.Session.DocumentId);
        Assert.Equal(dirty, f.Editor.IsModified);
        Assert.True(f.Editor.CanRedo);
        f.Clock.Advance(5); await f.Model.TickRecoveryAsync();
        f.Edit(model => { Assert.False(model.CanUndo); Assert.False(model.CanRedo); Assert.Empty(model.Title); });
        f.Editor.Redo();
        Assert.Equal(expected + "やり直しで戻す本文", f.Editor.Text);
        Assert.Equal(original, File.ReadAllText(f.Path));
        f.Restart(); // Recovery candidates exclude the session that is still running.
        var candidates = await f.Model.ListRecoveryAsync();
        if (dirty) Assert.Equal(expected, Assert.Single(candidates).Snapshot!.Text);
        else Assert.Empty(candidates);
    });

    [Theory]
    [InlineData("applied")]
    [InlineData("undo")]
    [InlineData("redo")]
    public void DocumentUndoToSavedTableRemovesRecoveryAndRedoCreatesOnlyAppliedCopy(string state) => Sta.RunAsync(async () =>
    {
        using var f = new Fixture(TableCompatibilityFixtures.Read("basic"));
        f.Edit(model =>
        {
            model.Title = "適用した表";
            model.DeleteRowCommand.Execute(null);
            model.UndoCommand.Execute(null); model.RedoCommand.Execute(null);
            model.ConfirmCommand.Execute(null);
        });
        var applied = f.Editor.Text;
        f.Clock.Advance(5); await f.Model.TickRecoveryAsync();
        if (state != "applied")
        {
            f.Editor.Undo();
            Assert.False(f.Editor.IsModified);
            await f.Recovery.DrainAsync();
        }
        if (state == "redo")
        {
            f.Editor.Redo();
            f.Clock.Advance(5); await f.Model.TickRecoveryAsync();
        }
        f.Restart();
        var candidates = await f.Model.ListRecoveryAsync();
        if (state == "undo") Assert.Empty(candidates);
        else Assert.Equal(applied, Assert.Single(candidates).Snapshot!.Text);
    });

    [Fact]
    public void CancelledDesignerHistoryDoesNotAcknowledgeExternalChanges() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture(TableCompatibilityFixtures.Read("basic"));
        f.Editor.Replace(f.Editor.Text.Length, 0, "\nローカルの本文\n");
        var local = f.Editor.Text;
        var baseline = f.Model.Session.Baseline;
        Task? detection = null;
        f.Edit(model =>
        {
            File.WriteAllText(f.Path, "外部の文書");
            model.Title = "未適用";
            model.UndoCommand.Execute(null); model.RedoCommand.Execute(null);
            detection = f.Model.CheckExternalChangesAsync(true);
            Assert.False(f.Model.Save());
            Assert.False(f.Model.ReloadFromDisk());
        });
        await detection!;
        Assert.True(f.Model.HasExternalChange);
        Assert.Equal(baseline, f.Model.Session.Baseline);
        Assert.Equal(local, f.Editor.Text);
        f.Model.DismissExternalChangeCommand.Execute(null);
        Assert.False(f.Model.Save());
        Assert.Single(f.Dialogs.Conflicts);
        Assert.Equal("外部の文書", File.ReadAllText(f.Path));
        f.Clock.Advance(5); await f.Model.TickRecoveryAsync();
        f.Restart();
        Assert.Equal(local, Assert.Single(await f.Model.ListRecoveryAsync()).Snapshot!.Text);
    });

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "日本語.adoc");
        public AvalonEditAdapter Editor { get; private set; } = null!;
        public MainViewModel Model { get; private set; } = null!;
        public RecoveryService Recovery { get; private set; } = null!;
        public RecoveryServiceTests.ManualClock Clock { get; } = new();
        public Dialogs Dialogs { get; } = new();
        private readonly TableDialogs tables = new();
        public Fixture(string source, bool bom = false)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path, source, new UTF8Encoding(bom));
            Restart();
            Assert.True(Model.Open(Path));
        }
        public void Restart()
        {
            Model?.Dispose(); Editor?.Dispose();
            Editor = new(new TextEditor());
            var files = new Utf8FileService();
            Model = new(Editor, files, Dialogs, new History(), tableDialogs: tables);
            Recovery = new(new RecoveryStore(System.IO.Path.Combine(Root, "recovery")), Clock);
            Model.EnableRecovery(Recovery);
            Model.EnableExternalMonitoring(new(new ExternalChangeServiceTests.FakeMonitor(), files));
        }
        public void Edit(Action<TableDesignerViewModel> action)
        {
            var called = false;
            tables.Action = model => { called = true; action(model); };
            var informationCount = Dialogs.Information.Count;
            Editor.Select(Editor.Text.IndexOf("|===", StringComparison.Ordinal), 0);
            Model.EditTableCommand.Execute(null);
            Assert.True(called || Dialogs.Information.Count > informationCount, "Expected a designer or an unsupported-input diagnostic.");
        }
        public void Dispose() { Model.Dispose(); Editor.Dispose(); Directory.Delete(Root, true); }
    }
    private sealed class TableDialogs : ITableEditingDialogs
    {
        public Action<TableDesignerViewModel>? Action { get; set; }
        public void Show(TableDesignerViewModel model) => Action?.Invoke(model);
    }
    private sealed class Dialogs : IUserDialogs
    {
        public string? SavePath { get; set; }
        public SaveConflictDecision ConflictDecision { get; set; } = SaveConflictDecision.Cancel;
        public List<FileObservation> Conflicts { get; } = [];
        public List<string> Information { get; } = [];
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => SavePath;
        public SaveDecision ConfirmSave(string documentName) => SaveDecision.Cancel;
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile)
        { Conflicts.Add(observation); return ConflictDecision; }
        public void ShowError(string message) => Assert.Fail(message);
        public void ShowInformation(string message) => Information.Add(message);
    }
    private sealed class History : IRecentFilesStore
    {
        public IReadOnlyList<string> Load() => [];
        public void Save(IReadOnlyList<string> paths) { }
    }
}
