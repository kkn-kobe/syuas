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
    string Read(string path);
    void Write(string path, string text);
}

public interface IRecentFilesStore
{
    IReadOnlyList<string> Load();
    void Save(IReadOnlyList<string> paths);
}
