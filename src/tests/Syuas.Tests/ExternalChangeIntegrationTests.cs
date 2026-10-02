using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class ExternalChangeIntegrationTests
{
    [Fact]
    public void NotificationAndDismissalPreserveDraftBaselineAndSaveConflictCheck() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft ");
        var session = f.Model.Session;
        File.WriteAllText(f.Path, "external");
        await f.Model.CheckExternalChangesAsync(true);
        Assert.True(f.Model.IsExternalChangeVisible);
        Assert.True(f.Model.CanReadExternalFile);
        Assert.Equal("draft original", f.Editor.Text);
        Assert.Same(session, f.Model.Session);
        f.Model.DismissExternalChangeCommand.Execute(null);
        Assert.False(f.Model.IsExternalChangeVisible);
        Assert.True(f.Model.HasExternalChange);
        await f.Model.CheckExternalChangesAsync(true);
        Assert.False(f.Model.IsExternalChangeVisible);
        Assert.False(f.Model.Save());
        Assert.Single(f.Dialogs.Conflicts);
        Assert.Equal("external", File.ReadAllText(f.Path));
        Assert.Same(session, f.Model.Session);
        f.Model.RevealExternalChangeCommand.Execute(null);
        Assert.True(f.Model.IsExternalChangeVisible);
    });

    [Fact]
    public void UnmodifiedDocumentIsNotAutomaticallyReloaded() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        File.WriteAllText(f.Path, "external");
        await f.Model.CheckExternalChangesAsync(true);
        Assert.Equal("original", f.Editor.Text);
        Assert.False(f.Editor.IsModified);
        Assert.True(f.Model.IsExternalChangeVisible);
    });

    [Fact]
    public void CancelReloadKeepsUndoHistorySessionAndNotification() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft ");
        var session = f.Model.Session;
        File.WriteAllText(f.Path, "external");
        await f.Model.CheckExternalChangesAsync(true);
        Assert.False(f.Model.ReloadFromDisk());
        Assert.Same(session, f.Model.Session);
        Assert.True(f.Editor.CanUndo);
        Assert.True(f.Model.IsExternalChangeVisible);
    });

    [Fact]
    public void ConfirmedReloadUsesLatestDiskContentAndClearsUndoAndWarning() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft ");
        f.Editor.Select(f.Editor.Text.Length, 0);
        File.WriteAllText(f.Path, "external");
        await f.Model.CheckExternalChangesAsync(true);
        // Re-read at the time of the action, not from the notification's older snapshot.
        File.WriteAllText(f.Path, "new");
        f.Dialogs.Decision = SaveDecision.Discard;
        Assert.True(f.Model.ReloadFromDisk());
        Assert.Equal("new", f.Editor.Text);
        Assert.Equal(3, f.Editor.CaretOffset);
        Assert.False(f.Editor.IsModified);
        Assert.False(f.Editor.CanUndo);
        Assert.False(f.Model.IsExternalChangeVisible);
        await f.Model.CheckExternalChangesAsync(true);
        Assert.False(f.Model.HasExternalChange);
        Assert.Equal(FileComparisonStatus.Unchanged, f.Files.Compare(f.Model.Session.Baseline!).Status);
    });

    [Theory]
    [InlineData("invalid")]
    [InlineData("missing")]
    [InlineData("locked")]
    public void FailedReloadKeepsEditorAndRecoveryCopy(string failure) => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var recoveryRoot = System.IO.Path.Combine(f.Directory, "recovery");
        var clock = new RecoveryServiceTests.ManualClock();
        f.Model.EnableRecovery(new RecoveryService(new RecoveryStore(recoveryRoot), clock));
        f.Editor.Replace(0, 0, "draft ");
        var session = f.Model.Session;
        clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        f.Dialogs.Decision = SaveDecision.Discard;
        if (failure == "invalid") File.WriteAllBytes(f.Path, [0xff]);
        if (failure == "missing") File.Delete(f.Path);
        using (var locked = failure == "locked" ? File.Open(f.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null)
            Assert.False(f.Model.ReloadFromDisk());
        Assert.Same(session, f.Model.Session);
        Assert.Equal("draft original", f.Editor.Text);
        Assert.True(f.Editor.CanUndo);
        Assert.Single(f.Dialogs.Errors);
        f.Model.Dispose();
        using var reader = new RecoveryStore(recoveryRoot);
        Assert.Equal("draft original", Assert.Single(reader.ListCandidates()).Snapshot!.Text);
    });

    [Fact]
    public void SuccessfulReloadRetiresOldRecoveryCopyOnlyAfterReading() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var recoveryRoot = System.IO.Path.Combine(f.Directory, "recovery");
        var clock = new RecoveryServiceTests.ManualClock();
        using var recovery = new RecoveryService(new RecoveryStore(recoveryRoot), clock);
        f.Model.EnableRecovery(recovery);
        f.Editor.Replace(0, 0, "draft ");
        clock.Advance(5);
        await f.Model.TickRecoveryAsync();
        File.WriteAllText(f.Path, "external");
        f.Dialogs.Decision = SaveDecision.Discard;
        Assert.True(f.Model.ReloadFromDisk());
        await recovery.DrainAsync();
        f.Model.Dispose();
        using var reader = new RecoveryStore(recoveryRoot);
        Assert.Empty(reader.ListCandidates());
    });

    [Fact]
    public void SaveAsDuringReloadPreservesDraftThenLoadsTheOriginallyRequestedPath() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft ");
        File.WriteAllText(f.Path, "external");
        f.Dialogs.Decision = SaveDecision.Save;
        f.Dialogs.ConflictDecision = SaveConflictDecision.SaveAs;
        f.Dialogs.SavePath = System.IO.Path.Combine(f.Directory, "copy.adoc");
        Assert.True(f.Model.ReloadFromDisk());
        Assert.Equal("draft original", File.ReadAllText(f.Dialogs.SavePath));
        Assert.Equal("external", f.Editor.Text);
        Assert.Equal(f.Path, f.Model.FilePath);
        Assert.Equal(f.Path, f.Model.RecentFiles[0]);
        await f.Model.CheckExternalChangesAsync(true);
        Assert.False(f.Model.HasExternalChange);
    });

    [Fact]
    public void ComparisonDoesNotChangeEditorOrBaselineAndRepresentsCapturedText() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft ");
        File.WriteAllText(f.Path, "external");
        var session = f.Model.Session;
        var comparison = await f.Model.ReadComparisonAsync();
        Assert.NotNull(comparison);
        Assert.Equal("draft original", comparison.EditorText);
        Assert.Equal("external", comparison.DiskSnapshot.Text);
        Assert.Same(session, f.Model.Session);
        File.WriteAllText(f.Path, "new external");
        Assert.Equal("external", comparison.DiskSnapshot.Text);
        Assert.True(f.Editor.IsModified);
        Assert.True(f.Editor.CanUndo);
    });

    [Fact]
    public void InvalidUtf8ComparisonFailsWithoutChangingDocument() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        var session = f.Model.Session;
        File.WriteAllBytes(f.Path, [0xff]);
        Assert.Null(await f.Model.ReadComparisonAsync());
        Assert.Same(session, f.Model.Session);
        Assert.Equal("original", f.Editor.Text);
        Assert.Single(f.Dialogs.Errors);
    });

    [Fact]
    public void OwnSaveAndSaveAsDoNotProduceExternalChangeWarnings() => Sta.RunAsync(async () =>
    {
        using var f = new Fixture();
        f.Editor.Replace(0, 0, "draft ");
        Assert.True(f.Model.Save());
        f.Monitor.Signal(f.Path);
        await f.Model.CheckExternalChangesAsync(true);
        Assert.False(f.Model.HasExternalChange);
        f.Dialogs.SavePath = System.IO.Path.Combine(f.Directory, "new.adoc");
        Assert.True(f.Model.Save(true));
        Assert.Equal(f.Dialogs.SavePath, f.Monitor.Watches.Last());
        f.Monitor.Signal(f.Path);
        await f.Model.CheckExternalChangesAsync(true);
        Assert.False(f.Model.HasExternalChange);
    });

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "main.adoc");
        public AvalonEditAdapter Editor { get; } = new(new TextEditor());
        public Utf8FileService Files { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public ExternalChangeServiceTests.FakeMonitor Monitor { get; } = new();
        public DocumentTabViewModel Model { get; }
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path, "original");
            Model = new(Editor, Files, Dialogs, new History());
            Model.EnableExternalMonitoring(new ExternalChangeService(Monitor, Files, retryDelay: TimeSpan.Zero));
            Assert.True(Model.Open(Path));
        }
        public void Dispose()
        {
            Model.Dispose(); Editor.Dispose();
            System.IO.Directory.Delete(Directory, true);
        }
    }
    private sealed class Dialogs : IUserDialogs
    {
        public SaveDecision Decision { get; set; } = SaveDecision.Cancel;
        public SaveConflictDecision ConflictDecision { get; set; } = SaveConflictDecision.Cancel;
        public string? SavePath { get; set; }
        public List<FileObservation> Conflicts { get; } = [];
        public List<string> Errors { get; } = [];
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => SavePath;
        public SaveDecision ConfirmSave(string documentName) => Decision;
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile)
        { Conflicts.Add(observation); return ConflictDecision; }
        public void ShowError(string message) => Errors.Add(message);
        public void ShowInformation(string message) { }
    }
    private sealed class History : IRecentFilesStore
    {
        public IReadOnlyList<string> Load() => [];
        public void Save(IReadOnlyList<string> paths) { }
    }
}

