using Syuas.Core.Models;

namespace Syuas.Core.Services;

public enum SaveDecision { Save, Discard, Cancel }

public interface IUserDialogs
{
    string? ChooseOpenFile();
    string? ChooseSaveFile(string? currentPath);
    SaveDecision ConfirmSave(string documentName);
    void ShowError(string message);
}

public interface IFileService
{
    FileSnapshot ReadSnapshot(string path);
    FileBaseline WriteSnapshot(string path, string text);
    FileComparison Compare(FileBaseline baseline);
}

public interface IRecentFilesStore
{
    IReadOnlyList<string> Load();
    void Save(IReadOnlyList<string> paths);
}
