using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class RecoveryIntegrationTests
{
    [Theory]
    [InlineData("draft\r\n日本語", false)]
    [InlineData("", true)]
    public void UntitledAndEmptyDraftsRestoreAsModified(string text, bool deleteAll) => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        if (deleteAll) { f.Editor.Replace(0, 0, "text"); f.Editor.Replace(0, 4, ""); }
        else f.Editor.Replace(0, 0, text);
        var id = f.Model.Session.DocumentId;
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        f.Model.Dispose(); // Simulate loss of the session without a confirmed close.
        using var reopened = f.CreateModel();
        var candidate = Assert.Single(await reopened.ListRecoveryAsync());
        Assert.Equal(text, candidate.Snapshot!.Text);
        Assert.True(await reopened.RestoreRecoveryAsync(candidate.Key));
        Assert.Equal(text, f.Editor.Text);
        Assert.Null(reopened.FilePath);
        Assert.Equal(id, reopened.Session.DocumentId);
        Assert.True(reopened.Session.IsModified);
        Assert.True(f.Editor.IsModified);
        Assert.False(f.Editor.CanUndo);
    });

    [Fact]
    public void RestoredSelectionAndCaretArePreservedAndInvalidOffsetsAreClamped() => Sta.Run(() =>
    {
        using var adapter = new AvalonEditAdapter(new TextEditor());
        adapter.LoadRecovery("abcdef", 1, 3, 4);
        Assert.Equal(1, adapter.SelectionStart);
        Assert.Equal(3, adapter.SelectionLength);
        Assert.Equal(4, adapter.CaretOffset);
        Assert.True(adapter.IsModified);
        adapter.LoadRecovery("", 90, 500, 900);
        Assert.Equal(0, adapter.SelectionStart);
        Assert.Equal(0, adapter.SelectionLength);
        Assert.Equal(0, adapter.CaretOffset);
        Assert.True(adapter.IsModified);
    });

    [Fact]
    public void RestoredTextRemainsRecoverableBeforeFirstTimerTick() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var original = f.AddCandidate("recovered");
        Assert.True(await f.Model.RestoreRecoveryAsync(original.Key));
        f.Model.Dispose();
        using var next = new RecoveryStore(f.RecoveryRoot);
        Assert.Equal("recovered", Assert.Single(next.ListCandidates()).Snapshot!.Text);
    });

    [Theory]
    [InlineData("unchanged", FileComparisonStatus.Unchanged)]
    [InlineData("changed", FileComparisonStatus.Modified)]
    [InlineData("missing", FileComparisonStatus.Missing)]
    public void RestorePreservesOriginalBaselineForSaveTimeConflictChecks(string state, FileComparisonStatus expected) => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var baseline = f.Files.WriteSnapshot(f.DocumentPath, "original");
        f.AddCandidate("recovered", baseline);
        if (state == "changed") File.WriteAllText(f.DocumentPath, "external");
        if (state == "missing") File.Delete(f.DocumentPath);
        var candidate = Assert.Single(await f.Model.ListRecoveryAsync());
        Assert.Equal(expected, candidate.OriginalFile!.Status);
        Assert.True(await f.Model.RestoreRecoveryAsync(candidate.Key));
        Assert.Equal(baseline, f.Model.Session.Baseline);
        f.Editor.Replace(0, 0, "new ");
        f.Editor.Undo();
        Assert.True(f.Editor.IsModified); // Undo cannot return to a fictitious saved recovery point.
        if (state == "unchanged")
        {
            Assert.True(f.Model.Save());
            Assert.Equal("recovered", File.ReadAllText(f.DocumentPath));
            Assert.False(f.Editor.IsModified);
        }
        else
        {
            Assert.False(f.Model.Save());
            Assert.Single(f.Dialogs.Conflicts);
            Assert.Equal(state == "changed" ? FileObservationStatus.Present : FileObservationStatus.Missing, f.Dialogs.Conflicts[0].Status);
            Assert.True(f.Editor.IsModified);
        }
    });

    [Fact]
    public void SaveSuccessRetiresCopyButFailedSaveKeepsIt() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft");
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        f.Dialogs.SavePath = Path.Combine(f.Directory, "missing-folder", "file.adoc");
        Assert.False(f.Model.Save());
        Assert.True(f.Editor.IsModified);
        f.Model.Dispose();
        using var reopened = f.CreateModel();
        var candidate = Assert.Single(await reopened.ListRecoveryAsync());
        Assert.True(await reopened.RestoreRecoveryAsync(candidate.Key));
        f.Dialogs.SavePath = f.DocumentPath;
        Assert.True(reopened.Save());
        await f.Service.DrainAsync();
        reopened.Dispose();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Empty(reader.ListCandidates());
    });

    [Fact]
    public void FailedOpenAfterDiscardDoesNotRetireCurrentCopy() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "current draft");
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        f.Dialogs.SaveDecision = SaveDecision.Discard;
        Assert.False(f.Model.Open(Path.Combine(f.Directory, "missing.adoc")));
        f.Model.Dispose();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Equal("current draft", Assert.Single(reader.ListCandidates()).Snapshot!.Text);
    });

    [Fact]
    public void SuccessfulNewAfterDiscardRetiresCopy() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft");
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        f.Dialogs.SaveDecision = SaveDecision.Discard;
        Assert.True(f.Model.New());
        await f.Service.DrainAsync();
        f.Model.Dispose();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Empty(reader.ListCandidates());
    });

    [Fact]
    public void CancelCloseKeepsCopyAndConfirmedCloseRetiresOnlyCurrentDocument() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var deferred = f.AddCandidate("later");
        f.Editor.Replace(0, 0, "current");
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        Assert.False(f.Model.CanClose());
        f.Dialogs.SaveDecision = SaveDecision.Discard;
        Assert.True(f.Model.CanClose());
        await f.Model.CloseRecoveryAsync();
        using var reader = new RecoveryStore(f.RecoveryRoot);
        var remaining = Assert.Single(reader.ListCandidates());
        Assert.Equal(deferred.Key, remaining.Key);
        Assert.Equal("later", remaining.Snapshot!.Text);
    });

    [Fact]
    public void CancelReplacingCurrentDraftDoesNotClaimRecoveryCandidate() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var candidate = f.AddCandidate("older");
        f.Editor.Replace(0, 0, "current");
        var session = f.Model.Session;
        Assert.False(await f.Model.RestoreRecoveryAsync(candidate.Key));
        Assert.Same(session, f.Model.Session);
        Assert.Equal("current", f.Editor.Text);
        Assert.Equal(candidate.Key, Assert.Single(await f.Model.ListRecoveryAsync()).Key);
    });

    [Fact]
    public void FailedClaimKeepsCurrentDraftAndBothCandidatesAreIndependent() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var first = f.AddCandidate("first");
        var second = f.AddCandidate("second");
        f.Editor.Replace(0, 0, "current");
        f.Dialogs.SaveDecision = SaveDecision.Discard;
        var session = f.Model.Session;
        using (var locked = File.Open(Path.Combine(f.RecoveryRoot, first.Key.SessionId.ToString("N"), "session.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.False(await f.Model.RestoreRecoveryAsync(first.Key));
        Assert.Same(session, f.Model.Session);
        Assert.Equal("current", f.Editor.Text);
        Assert.Equal(2, (await f.Model.ListRecoveryAsync()).Count);
        await f.Model.DiscardRecoveryAsync(first.Key);
        Assert.Equal(second.Key, Assert.Single(await f.Model.ListRecoveryAsync()).Key);
    });

    [Fact]
    public void FailedCloseCleanupShowsNoticeAndCopyRemainsRecoverable() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft to discard");
        f.Clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        var copy = Assert.Single(System.IO.Directory.GetFiles(f.RecoveryRoot, "*.current.json", SearchOption.AllDirectories));
        f.Dialogs.SaveDecision = SaveDecision.Discard;
        Assert.True(f.Model.CanClose());
        using (var locked = File.Open(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
            await f.Model.CloseRecoveryAsync();
        Assert.Contains("再表示", Assert.Single(f.Dialogs.Information));
        using var reader = new RecoveryStore(f.RecoveryRoot);
        Assert.Equal("draft to discard", Assert.Single(reader.ListCandidates()).Snapshot!.Text);
    });

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
        public string RecoveryRoot => Path.Combine(Directory, "Recovery");
        public string DocumentPath => Path.Combine(Directory, "main.adoc");
        public AvalonEditAdapter Editor { get; } = new(new TextEditor());
        public Utf8FileService Files { get; } = new();
        public RecoveryServiceTests.ManualClock Clock { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public RecoveryService Service { get; private set; } = null!;
        public DocumentTabViewModel Model { get; }
        public Fixture() => Model = CreateModel();
        public DocumentTabViewModel CreateModel()
        {
            Editor.Load("");
            var model = new DocumentTabViewModel(Editor, Files, Dialogs, new History());
            Service = new RecoveryService(new RecoveryStore(RecoveryRoot), Clock);
            model.EnableRecovery(Service);
            return model;
        }
        public RecoveryCandidate AddCandidate(string text, FileBaseline? baseline = null)
        {
            using var source = new RecoveryStore(RecoveryRoot);
            var snapshot = new RecoverySnapshot(Guid.NewGuid(), 42, Clock.GetUtcNow(), text, baseline, 0, 0, 0);
            source.Write(snapshot);
            return new(new(source.SessionId, snapshot.DocumentId), snapshot);
        }
        public void Dispose()
        {
            Model.Dispose(); Service.Dispose(); Editor.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class Dialogs : IUserDialogs
    {
        public SaveDecision SaveDecision { get; set; } = SaveDecision.Cancel;
        public string? SavePath { get; set; }
        public List<FileObservation> Conflicts { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Information { get; } = [];
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => SavePath;
        public SaveDecision ConfirmSave(string documentName) => SaveDecision;
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile)
        { Conflicts.Add(observation); return SaveConflictDecision.Cancel; }
        public void ShowError(string message) => Errors.Add(message);
        public void ShowInformation(string message) => Information.Add(message);
    }
    private sealed class History : IRecentFilesStore
    {
        public IReadOnlyList<string> Load() => [];
        public void Save(IReadOnlyList<string> paths) { }
    }
}

