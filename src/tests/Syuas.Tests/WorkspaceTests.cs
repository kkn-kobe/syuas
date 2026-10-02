using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class WorkspaceTests
{
    [Fact]
    public void BatchOpenKeepsSuccessfulFilesAndReportsFailuresTogether() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var path = f.File("ok.adoc", "text");
        f.Model.OpenMany([Path.Combine(f.Root, "missing1.adoc"), path, Path.Combine(f.Root, "missing2.adoc")]);
        Assert.Equal(2, f.Model.Documents.Count);
        Assert.Equal(path, f.Model.FilePath);
        var error = Assert.Single(f.Dialogs.Errors);
        Assert.Contains("missing1.adoc", error);
        Assert.Contains("missing2.adoc", error);
    });

    [Fact]
    public void RecoveryWithSameDocumentIdDoesNotReplaceAnOpenDraftCopy() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.EnableRecovery();
        f.Editor.Text = "live draft";
        var id = f.Model.Session.DocumentId;
        f.Clock.Advance(6);
        await f.Model.TickRecoveryAsync();
        RecoveryKey key;
        using (var source = new RecoveryStore(f.RecoveryRoot))
        {
            source.Write(new(id, 1, DateTimeOffset.UtcNow, "older recovery", null, 0, 0, 0));
            key = new(source.SessionId, id);
        }
        Assert.True(await f.Model.RestoreRecoveryAsync(key));
        Assert.NotEqual(id, f.Model.Session.DocumentId);
        f.Model.Dispose();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Equal(new[] { "live draft", "older recovery" }, reader.ListCandidates()
            .Select(c => c.Snapshot!.Text).OrderBy(t => t).ToArray());
    });

    [Fact]
    public void NewAndOpenPreserveDirtyTabsAndDoNotAskToDiscard() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var first = f.Model.ActiveDocument;
        f.Editor.Text = "first";
        Assert.True(f.Model.New());
        f.Editor.Text = "second";
        Assert.True(f.Model.Open(f.File("third.adoc", "third")));
        Assert.Equal(3, f.Model.Documents.Count);
        Assert.Empty(f.Dialogs.Confirmations);
        f.Model.ActiveDocument = first;
        Assert.Equal("first", f.Editor.Text);
        Assert.True(first.Session.IsModified);
        Assert.Equal("無題1", first.DocumentName);
    });

    [Fact]
    public void SwitchingPreservesSelectionCaretUndoRedoAndSavePoint() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Editor.Text = "abcdef";
        f.Editor.Document.UndoStack.MarkAsOriginalFile();
        f.Editor.Select(1, 3);
        var first = f.Model.ActiveDocument;
        var caret = f.Editor.CaretOffset;
        f.Model.New();
        f.Editor.Text = "second";
        var second = f.Model.ActiveDocument;
        f.Model.ActiveDocument = first;
        Assert.Equal(1, f.Editor.SelectionStart);
        Assert.Equal(3, f.Editor.SelectionLength);
        Assert.Equal(caret, f.Editor.CaretOffset);
        Assert.False(first.Session.IsModified);
        f.Editor.Document.Insert(6, "!");
        f.Model.ActiveDocument = second;
        f.Editor.Document.Insert(6, "?");
        f.Model.ActiveDocument = first;
        f.Model.UndoCommand.Execute(null);
        Assert.Equal("abcdef", f.Editor.Text);
        Assert.False(first.Session.IsModified);
        f.Model.RedoCommand.Execute(null);
        Assert.Equal("abcdef!", f.Editor.Text);
        f.Model.ActiveDocument = second;
        Assert.Equal("second?", f.Editor.Text);
    });

    [Fact]
    public void DuplicatePathsActivateExistingTabWithoutReloading() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var path = f.File("same.adoc", "disk");
        Assert.True(f.Model.Open(path));
        f.Editor.Text = "local";
        var first = f.Model.ActiveDocument;
        f.Model.New();
        Assert.True(f.Model.Open(Path.Combine(f.Root, ".", "SAME.adoc")));
        Assert.Same(first, f.Model.ActiveDocument);
        Assert.Equal("local", f.Editor.Text);
        Assert.Equal(3, f.Model.Documents.Count);
        Assert.Equal(path, f.Model.RecentFiles[0], ignoreCase: true);
    });

    [Fact]
    public void FailedOpenKeepsSelectionAndOtherTabs() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var first = f.Model.ActiveDocument;
        f.Editor.Text = "draft";
        Assert.False(f.Model.Open(Path.Combine(f.Root, "missing.adoc")));
        Assert.Single(f.Model.Documents);
        Assert.Same(first, f.Model.ActiveDocument);
        Assert.Equal("draft", f.Editor.Text);
        Assert.Single(f.Dialogs.Errors);
        Assert.Single(f.Editors);
    });

    [Fact]
    public void SaveAsCannotOverwriteAnotherOpenTab() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var path = f.File("open.adoc", "original");
        f.Model.Open(path);
        f.Model.New();
        f.Editor.Text = "new";
        var target = Path.Combine(f.Root, "new.adoc");
        f.Dialogs.SavePaths.Enqueue(path);
        f.Dialogs.SavePaths.Enqueue(target);
        Assert.True(f.Model.Save(true));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal("new", File.ReadAllText(target));
        Assert.Single(f.Dialogs.Information);
        Assert.Equal(target, f.Model.ActiveDocument.FilePath);
        Assert.Equal(target, f.Model.RecentFiles[0]);
    });

    [Fact]
    public void ConflictDialogSaveAsAlsoChecksOpenTabs() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var path = f.File("open.adoc", "original");
        f.Model.Open(path);
        f.Model.New();
        f.Editor.Text = "new";
        var conflict = f.File("conflict.adoc", "external");
        f.Dialogs.SavePaths.Enqueue(conflict);
        f.Dialogs.SavePaths.Enqueue(path);
        f.Dialogs.SavePaths.Enqueue(null);
        f.Dialogs.ConflictDecision = SaveConflictDecision.SaveAs;
        Assert.False(f.Model.Save());
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal("external", File.ReadAllText(conflict));
        Assert.True(f.Model.Session.IsModified);
    });

    [Fact]
    public void SaveAllStopsOnCancelledSaveAndKeepsAllTabs() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Editor.Text = "first";
        f.Model.New();
        f.Editor.Text = "second";
        f.Dialogs.SavePaths.Enqueue(Path.Combine(f.Root, "first.adoc"));
        f.Dialogs.SavePaths.Enqueue(null);
        Assert.False(f.Model.SaveAll());
        Assert.Equal(2, f.Model.Documents.Count);
        Assert.False(f.Model.Documents[0].Session.IsModified);
        Assert.True(f.Model.Documents[1].Session.IsModified);
        Assert.Same(f.Model.Documents[1], f.Model.ActiveDocument);
    });

    [Fact]
    public void ExitCancellationRetainsPreviouslyDiscardedDocuments() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Editor.Text = "first";
        f.Model.New();
        f.Editor.Text = "second";
        f.Dialogs.Decisions.Enqueue(SaveDecision.Discard);
        f.Dialogs.Decisions.Enqueue(SaveDecision.Cancel);
        Assert.False(f.Model.CanClose());
        Assert.Equal(2, f.Model.Documents.Count);
        Assert.All(f.Model.Documents, d => Assert.True(d.Session.IsModified));
        Assert.Equal("first", f.Editors[f.Model.Documents[0]].Text);
        Assert.Equal("second", f.Editors[f.Model.Documents[1]].Text);
    });

    [Fact]
    public void ClosingTargetsRequestedTabAndReleasesItsEditor() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Text = "first";
        var first = f.Model.ActiveDocument;
        f.Model.New();
        var second = f.Model.ActiveDocument;
        f.Dialogs.Decisions.Enqueue(SaveDecision.Cancel);
        Assert.False(await f.Model.CloseDocumentAsync(first));
        Assert.Equal(2, f.Model.Documents.Count);
        f.Model.ActiveDocument = second;
        f.Dialogs.Decisions.Enqueue(SaveDecision.Discard);
        Assert.True(await f.Model.CloseDocumentAsync(first));
        Assert.Same(second, f.Model.ActiveDocument);
        Assert.False(f.Editors.ContainsKey(first));
        Assert.True(await f.Model.CloseDocumentAsync(second));
        Assert.Single(f.Model.Documents);
        Assert.NotSame(second, f.Model.ActiveDocument);
        Assert.Equal("", f.Editor.Text);
        Assert.False(f.Model.Session.IsModified);
    });

    [Fact]
    public void SharedSearchAndRelativeSelectionTargetActiveDocument() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Editor.Text = "foo A";
        f.Model.SearchText = "foo";
        f.Model.ReplacementText = "bar";
        f.Model.New();
        f.Editor.Text = "B foo";
        f.Model.ReplaceAllCommand.Execute(null);
        Assert.Equal("B bar", f.Editor.Text);
        f.Model.SelectRelative(1);
        Assert.Equal("foo A", f.Editor.Text);
        Assert.Equal("foo", f.Model.ActiveDocument.SearchText);
        f.Model.SelectRelative(-1);
        Assert.Equal("B bar", f.Editor.Text);
    });

    [Fact]
    public void InactiveDraftsRemainRecoverableAndCloseRetiresOnlyOne() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.EnableRecovery();
        f.Editor.Text = "first";
        var first = f.Model.ActiveDocument;
        f.Model.New();
        f.Editor.Text = "second";
        f.Clock.Advance(6);
        await f.Model.TickRecoveryAsync();
        f.Dialogs.Decisions.Enqueue(SaveDecision.Discard);
        Assert.True(await f.Model.CloseDocumentAsync(first));
        f.Model.Dispose();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        var remaining = Assert.Single(reader.ListCandidates());
        Assert.Equal("second", remaining.Snapshot!.Text);
    });

    [Fact]
    public void CancelledExitPreservesAllRecoveryCopies() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.EnableRecovery();
        f.Editor.Text = "first";
        f.Model.New();
        f.Editor.Text = "second";
        f.Clock.Advance(6);
        await f.Model.TickRecoveryAsync();
        f.Dialogs.Decisions.Enqueue(SaveDecision.Discard);
        f.Dialogs.Decisions.Enqueue(SaveDecision.Cancel);
        Assert.False(f.Model.CanClose());
        f.Model.Dispose();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Equal(2, reader.ListCandidates().Count);
    });

    [Fact]
    public void ConfirmedExitRetiresEveryRecoveryCopy() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.EnableRecovery();
        f.Editor.Text = "first";
        f.Model.New();
        f.Editor.Text = "second";
        f.Clock.Advance(6);
        await f.Model.TickRecoveryAsync();
        Assert.True(f.Model.CanClose());
        await f.Model.CloseRecoveryAsync();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Empty(reader.ListCandidates());
    });

    [Fact]
    public void RestoreAddsTabAndProtectsOpenFileFromRecoveredVersion() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var path = f.File("same.adoc", "disk");
        RecoveryKey key;
        using (var source = new RecoveryStore(f.RecoveryRoot))
        {
            var id = Guid.NewGuid();
            source.Write(new(id, 1, DateTimeOffset.UtcNow, "recovered", f.Files.ReadSnapshot(path).Baseline, 0, 0, 0));
            key = new(source.SessionId, id);
        }
        f.Model.Open(path);
        f.Editor.Text = "local";
        f.EnableRecovery();
        Assert.True(await f.Model.RestoreRecoveryAsync(key));
        Assert.Equal(3, f.Model.Documents.Count);
        Assert.Equal("recovered", f.Editor.Text);
        Assert.True(f.Model.ActiveDocument.RequiresSaveAs);
        f.Dialogs.SavePaths.Enqueue(path);
        f.Dialogs.SavePaths.Enqueue(null);
        Assert.False(f.Model.Save());
        Assert.Equal("disk", File.ReadAllText(path));
        Assert.Equal("local", f.Editors[f.Model.Documents[1]].Text);
        Assert.Empty(f.Dialogs.Confirmations);
    });

    [Fact]
    public void InactiveFileIsMonitoredIndependently() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var path = f.File("watched.adoc", "disk");
        f.Model.Open(path);
        var watched = f.Model.ActiveDocument;
        watched.EnableExternalMonitoring(new(new SilentMonitor(), f.Files));
        f.Model.New();
        File.WriteAllText(path, "external");
        await f.Model.CheckExternalChangesAsync(true);
        Assert.True(watched.HasExternalChange);
        Assert.Contains("⚠", watched.TabTitle);
        Assert.False(f.Model.HasExternalChange);
        f.Model.ActiveDocument = watched;
        Assert.True(f.Model.HasExternalChange);
        Assert.Equal("disk", f.Editor.Text);
    });

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SYUAS.WorkspaceTests", Guid.NewGuid().ToString("N"));
        public string RecoveryRoot => Path.Combine(Root, "Recovery");
        public Utf8FileService Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public Dictionary<DocumentTabViewModel, TextEditor> Editors { get; } = [];
        public RecoveryServiceTests.ManualClock Clock { get; } = new();
        public MainViewModel Model { get; }
        public TextEditor Editor => Editors[Model.ActiveDocument];
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            var history = new RecentFilesStore(Path.Combine(Root, "recent.json"));
            Model = new(() =>
            {
                var editor = new TextEditor();
                var adapter = new AvalonEditAdapter(editor);
                var document = new DocumentTabViewModel(adapter, Files, Dialogs, history);
                Editors.Add(document, editor);
                document.Disposed += (_, _) => { Editors.Remove(document); adapter.Dispose(); };
                return document;
            }, Dialogs, history);
        }
        public string File(string name, string text)
        {
            var path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, text);
            return path;
        }
        public void EnableRecovery() => Model.EnableRecovery(new(new RecoveryStore(RecoveryRoot), Clock));
        public void Dispose() { Model.Dispose(); Directory.Delete(Root, true); }
    }
    private sealed class Dialogs : IUserDialogs
    {
        public Queue<SaveDecision> Decisions { get; } = [];
        public Queue<string?> SavePaths { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Information { get; } = [];
        public List<string> Confirmations { get; } = [];
        public SaveConflictDecision ConflictDecision { get; set; } = SaveConflictDecision.Cancel;
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => SavePaths.TryDequeue(out var path) ? path : null;
        public SaveDecision ConfirmSave(string name) { Confirmations.Add(name); return Decisions.TryDequeue(out var decision) ? decision : SaveDecision.Discard; }
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile) => ConflictDecision;
        public void ShowError(string message) => Errors.Add(message);
        public void ShowInformation(string message) => Information.Add(message);
    }
    private sealed class SilentMonitor : IFileChangeMonitor
    {
        public event Action<FileChangeSignal>? Changed { add { } remove { } }
        public string? Watch(string? path) => null;
        public void Dispose() { }
    }
}
