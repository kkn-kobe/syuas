using Syuas.Core.Models;

namespace Syuas.Core.Services;

public enum SaveDecision { Save, Discard, Cancel }

public interface IUserDialogs
{
    string? ChooseOpenFile();
    IReadOnlyList<string> ChooseOpenFiles() => ChooseOpenFile() is { } path ? [path] : [];
    string? ChooseSaveFile(string? currentPath);
    SaveDecision ConfirmSave(string documentName);
    SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile);
    void ShowError(string message);
    void ShowInformation(string message);
}

public interface IFileService
{
    FileSnapshot ReadSnapshot(string path);
    FileBaseline WriteSnapshot(string path, string text);
    FileComparison Compare(FileBaseline baseline);
    FileObservation Observe(string path);
    // null expected means the destination must not exist. Never silently adopt a newer version.
    FileSaveResult WriteChecked(string path, string text, FileBaseline? expected, bool preserveBackup = false);
}

public interface IRecentFilesStore
{
    IReadOnlyList<string> Load();
    void Save(IReadOnlyList<string> paths);
}
