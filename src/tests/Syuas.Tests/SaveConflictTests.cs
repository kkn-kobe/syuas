using System.Text;
using ICSharpCode.AvalonEdit;
using Syuas.App.Adapters;
using Syuas.Core.Models;
using Syuas.Core.Services;
using Syuas.Core.ViewModels;

namespace Syuas.Tests;

public sealed class SaveConflictTests
{
    [Fact]
    public void OrdinarySaveChecksDiskUpdatesBaselineAndRemovesTransientBackup() => Sta.Run(() =>
    {
        using var f = new Fixture();
        var id = f.Model.Session.DocumentId;
        f.Edit();
        Assert.True(f.Model.Save());
        Assert.Equal("edited", File.ReadAllText(f.Path));
        Assert.Equal(id, f.Model.Session.DocumentId);
        Assert.False(f.Model.Session.IsModified);
        Assert.Equal(FileComparisonStatus.Unchanged, f.RealFiles.Compare(f.Model.Session.Baseline!).Status);
        Assert.Single(Directory.GetFiles(f.Directory));
        Assert.Empty(f.Dialogs.Conflicts);
        f.Editor.Undo();
        Assert.True(f.Model.Session.IsModified);
        f.Editor.Redo();
        Assert.False(f.Model.Session.IsModified);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedFileCancelPreservesEditorDiskSessionAndHistory(bool saveAsSamePath) => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        var historyWrites = f.History.Writes;
        var time = File.GetLastWriteTimeUtc(f.Path);
        File.WriteAllText(f.Path, "external"); // Same byte count as the original.
        File.SetLastWriteTimeUtc(f.Path, time);
        if (saveAsSamePath) f.Dialogs.SavePaths.Enqueue(f.Path);
        Assert.False(f.Model.Save(saveAsSamePath));
        Assert.Same(session, f.Model.Session);
        Assert.Equal("edited", f.Editor.Text);
        Assert.True(f.Editor.CanUndo);
        Assert.Equal("external", File.ReadAllText(f.Path));
        Assert.Equal(historyWrites, f.History.Writes);
        Assert.Single(f.Dialogs.Conflicts);
        Assert.Equal(FileObservationStatus.Present, f.Dialogs.Conflicts[0].Observation.Status);
        Assert.True(f.Dialogs.Conflicts[0].IsCurrentFile);
        Assert.Empty(f.Dialogs.Errors);
    });

    [Fact]
    public void AcceptedOverwritePreservesExternalBytesIncludingInvalidUtf8() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        byte[] external = [0xef, 0xbb, 0xbf, 0xff, 0x0d, 0x0a];
        File.WriteAllBytes(f.Path, external);
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        Assert.True(f.Model.Save());
        Assert.Equal("edited", File.ReadAllText(f.Path));
        var backup = Assert.Single(Directory.GetFiles(f.Directory, "*.bak"));
        Assert.Equal(external, File.ReadAllBytes(backup));
        Assert.Contains(backup, Assert.Single(f.Dialogs.Information));
        Assert.False(f.Model.Session.IsModified);
        Assert.Empty(Directory.GetFiles(f.Directory, "*.tmp"));
        Assert.Equal(FileComparisonStatus.Unchanged, f.RealFiles.Compare(f.Model.Session.Baseline!).Status);
    });

    [Fact]
    public void ChangeDuringConfirmationRequiresAnotherDecision() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        File.WriteAllText(f.Path, "external one");
        f.Dialogs.OnConflict = count => { if (count == 1) File.WriteAllText(f.Path, "external two"); };
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.Cancel);
        Assert.False(f.Model.Save());
        Assert.Equal(2, f.Dialogs.Conflicts.Count);
        Assert.NotEqual(f.Dialogs.Conflicts[0].Observation.Baseline, f.Dialogs.Conflicts[1].Observation.Baseline);
        Assert.Equal("external two", File.ReadAllText(f.Path));
        Assert.Same(session, f.Model.Session);
        Assert.Empty(Directory.GetFiles(f.Directory, "*.bak"));
    });

    [Fact]
    public void ReconfirmedChangeBacksUpTheLatestExternalVersion() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        File.WriteAllText(f.Path, "external one");
        f.Dialogs.OnConflict = count => { if (count == 1) File.WriteAllText(f.Path, "external two"); };
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        Assert.True(f.Model.Save());
        Assert.Equal(2, f.Dialogs.Conflicts.Count);
        Assert.Equal("external two", File.ReadAllText(Assert.Single(Directory.GetFiles(f.Directory, "*.bak"))));
    });

    [Fact]
    public void DeletedFileNeedsExplicitRecreateAndKeepsDocumentIdentity() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        File.Delete(f.Path);
        Assert.False(f.Model.Save());
        Assert.False(File.Exists(f.Path));
        Assert.Same(session, f.Model.Session);
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.Recreate);
        Assert.True(f.Model.Save());
        Assert.Equal("edited", File.ReadAllText(f.Path));
        Assert.Equal(session.DocumentId, f.Model.Session.DocumentId);
        Assert.False(f.Model.Session.IsModified);
        Assert.All(f.Dialogs.Conflicts, c => Assert.Equal(FileObservationStatus.Missing, c.Observation.Status));
    });

    [Fact]
    public void FileAppearingDuringRecreateConfirmationIsNotOverwritten() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        File.Delete(f.Path);
        f.Dialogs.OnConflict = count => { if (count == 1) File.WriteAllText(f.Path, "appeared"); };
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.Recreate);
        Assert.False(f.Model.Save());
        Assert.Equal("appeared", File.ReadAllText(f.Path));
        Assert.Equal(FileObservationStatus.Missing, f.Dialogs.Conflicts[0].Observation.Status);
        Assert.Equal(FileObservationStatus.Present, f.Dialogs.Conflicts[1].Observation.Status);
        Assert.True(f.Model.Session.IsModified);
    });

    [Fact]
    public void DeletionDuringOverwriteConfirmationNeedsRecreateDecision() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        File.WriteAllText(f.Path, "external");
        f.Dialogs.OnConflict = count => { if (count == 1) File.Delete(f.Path); };
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        Assert.False(f.Model.Save());
        Assert.False(File.Exists(f.Path));
        Assert.Equal(FileObservationStatus.Missing, f.Dialogs.Conflicts[1].Observation.Status);
    });

    [Fact]
    public void LockedFileCanBeRetriedAfterUnlockingWithoutLosingBaseline() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        using var locked = new FileStream(f.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        f.Dialogs.OnConflict = _ => locked.Dispose();
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.Retry);
        Assert.True(f.Model.Save());
        Assert.Equal(FileObservationStatus.Unavailable, Assert.Single(f.Dialogs.Conflicts).Observation.Status);
        Assert.Equal("edited", File.ReadAllText(f.Path));
    });

    [Fact]
    public void RetryAfterUnlockingDoesNotApproveAnExternalChange() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        File.WriteAllText(f.Path, "external");
        using var locked = new FileStream(f.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        f.Dialogs.OnConflict = _ => locked.Dispose();
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.Retry);
        Assert.False(f.Model.Save());
        Assert.Equal(2, f.Dialogs.Conflicts.Count);
        Assert.Equal(FileObservationStatus.Present, f.Dialogs.Conflicts[1].Observation.Status);
        Assert.Equal("external", File.ReadAllText(f.Path));
        Assert.Same(session, f.Model.Session);
    });

    [Fact]
    public void SaveAsFromConflictKeepsExternalFileAndSavesDraftElsewhere() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var id = f.Model.Session.DocumentId;
        File.WriteAllText(f.Path, "external");
        var alternative = System.IO.Path.Combine(f.Directory, "alternative.adoc");
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.SaveAs);
        f.Dialogs.SavePaths.Enqueue(alternative);
        Assert.True(f.Model.Save());
        Assert.Equal("external", File.ReadAllText(f.Path));
        Assert.Equal("edited", File.ReadAllText(alternative));
        Assert.Equal(alternative, f.Model.FilePath);
        Assert.Equal(id, f.Model.Session.DocumentId);
        Assert.Equal(alternative, f.History.Paths[0]);
        Assert.Empty(f.Dialogs.Information);
    });

    [Fact]
    public void ExistingSaveAsDestinationAlsoRequiresApprovalAndBackup() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var alternative = System.IO.Path.Combine(f.Directory, "existing.adoc");
        File.WriteAllText(alternative, "existing content");
        f.Dialogs.SavePaths.Enqueue(alternative);
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        Assert.True(f.Model.Save(true));
        Assert.False(Assert.Single(f.Dialogs.Conflicts).IsCurrentFile);
        Assert.Equal("existing content", File.ReadAllText(Assert.Single(Directory.GetFiles(f.Directory, "*.bak"))));
        Assert.Equal("original", File.ReadAllText(f.Path));
        Assert.Equal("edited", File.ReadAllText(alternative));
    });

    [Fact]
    public void CancellingAlternatePickerDoesNotLoseDraftOrChangeSession() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        File.WriteAllText(f.Path, "external");
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.SaveAs);
        Assert.False(f.Model.Save());
        Assert.Same(session, f.Model.Session);
        Assert.Equal("external", File.ReadAllText(f.Path));
    });

    [Theory]
    [InlineData("close")]
    [InlineData("new")]
    [InlineData("open")]
    public void ConflictCancelStopsTransitionsThatRequestedSave(string action) => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        File.WriteAllText(f.Path, "external");
        var result = action switch
        {
            "close" => f.Model.CanClose(),
            "new" => f.Model.New(),
            _ => f.Model.Open(System.IO.Path.Combine(f.Directory, "next.adoc"))
        };
        Assert.False(result);
        Assert.Same(session, f.Model.Session);
        Assert.Equal("edited", f.Editor.Text);
    });

    [Fact]
    public void BackupFailureDoesNotMarkSavedOrUpdateHistory() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        var historyWrites = f.History.Writes;
        File.WriteAllText(f.Path, "external");
        f.Files.FailBackup = true;
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.OverwriteWithBackup);
        Assert.False(f.Model.Save());
        Assert.Same(session, f.Model.Session);
        Assert.Equal(historyWrites, f.History.Writes);
        Assert.Equal("external", File.ReadAllText(f.Path));
        Assert.Single(f.Dialogs.Errors);
        Assert.Empty(f.Dialogs.Information);
    });

    [Fact]
    public void UnmodifiedEditorAlsoChecksBeforeExplicitSave() => Sta.Run(() =>
    {
        using var f = new Fixture();
        File.WriteAllText(f.Path, "external");
        Assert.False(f.Model.Save());
        Assert.Single(f.Dialogs.Conflicts);
        Assert.Equal("external", File.ReadAllText(f.Path));
    });

    [Fact]
    public void UntitledDocumentCanSaveToNewPathWithoutConflict() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Model.New();
        f.Edit();
        var id = f.Model.Session.DocumentId;
        var newPath = System.IO.Path.Combine(f.Directory, "new.adoc");
        f.Dialogs.SavePaths.Enqueue(newPath);
        Assert.True(f.Model.Save());
        Assert.Equal(id, f.Model.Session.DocumentId);
        Assert.Equal(newPath, f.Model.FilePath);
        Assert.Empty(f.Dialogs.Conflicts);
    });

    [Fact]
    public void MissingParentFailsRecreateWithoutChangingDocument() => Sta.Run(() =>
    {
        using var f = new Fixture();
        f.Edit();
        var session = f.Model.Session;
        File.Delete(f.Path);
        System.IO.Directory.Delete(f.Directory);
        f.Dialogs.Decisions.Enqueue(SaveConflictDecision.Recreate);
        Assert.False(f.Model.Save());
        Assert.Same(session, f.Model.Session);
        Assert.Single(f.Dialogs.Errors);
    });

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "main.adoc");
        public AvalonEditAdapter Editor { get; } = new(new TextEditor());
        public Utf8FileService RealFiles { get; } = new();
        public InterceptFiles Files { get; }
        public Dialogs Dialogs { get; } = new();
        public History History { get; } = new();
        public MainViewModel Model { get; }
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path, "original", new UTF8Encoding(false));
            Files = new(RealFiles);
            Model = new(Editor, Files, Dialogs, History);
            Assert.True(Model.Open(Path));
        }
        public void Edit() => Editor.Replace(0, Editor.Text.Length, "edited");
        public void Dispose()
        {
            Model.Dispose(); Editor.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class InterceptFiles(Utf8FileService files) : IFileService
    {
        public bool FailBackup { get; set; }
        public FileSnapshot ReadSnapshot(string path) => files.ReadSnapshot(path);
        public FileBaseline WriteSnapshot(string path, string text) => files.WriteSnapshot(path, text);
        public FileComparison Compare(FileBaseline baseline) => files.Compare(baseline);
        public FileObservation Observe(string path) => files.Observe(path);
        public FileSaveResult WriteChecked(string path, string text, FileBaseline? expected, bool preserveBackup = false)
        {
            if (FailBackup && preserveBackup) throw new IOException("Backup failed");
            return files.WriteChecked(path, text, expected, preserveBackup);
        }
    }

    private sealed class Dialogs : IUserDialogs
    {
        public Queue<SaveConflictDecision> Decisions { get; } = new();
        public Queue<string?> SavePaths { get; } = new();
        public List<(string Path, FileObservation Observation, bool IsCurrentFile)> Conflicts { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Information { get; } = [];
        public Action<int>? OnConflict { get; set; }
        public string? ChooseOpenFile() => null;
        public string? ChooseSaveFile(string? currentPath) => SavePaths.Count > 0 ? SavePaths.Dequeue() : null;
        public SaveDecision ConfirmSave(string documentName) => SaveDecision.Save;
        public void ShowError(string message) => Errors.Add(message);
        public void ShowInformation(string message) => Information.Add(message);
        public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile)
        {
            Conflicts.Add((path, observation, isCurrentFile));
            OnConflict?.Invoke(Conflicts.Count);
            return Decisions.Count > 0 ? Decisions.Dequeue() : SaveConflictDecision.Cancel;
        }
    }

    private sealed class History : IRecentFilesStore
    {
        public int Writes { get; private set; }
        public IReadOnlyList<string> Paths { get; private set; } = [];
        public IReadOnlyList<string> Load() => Paths;
        public void Save(IReadOnlyList<string> paths) { Paths = paths; Writes++; }
    }
}
