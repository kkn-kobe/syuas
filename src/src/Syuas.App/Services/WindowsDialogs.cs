using System.IO;
using System.Windows;
using Microsoft.Win32;
using Syuas.Core.Services;
using Syuas.Core.Models;
using Syuas.Core.ViewModels;
using Syuas.App.Views;

namespace Syuas.App.Services;

public sealed class WindowsDialogs(Window owner) : IUserDialogs
{
    private const string Filter = "AsciiDoc / テキスト|*.adoc;*.asciidoc;*.ad;*.asc;*.txt|すべてのファイル|*.*";

    public string? ChooseOpenFile()
    {
        var dialog = new OpenFileDialog { Filter = Filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? ChooseSaveFile(string? currentPath)
    {
        var dialog = new SaveFileDialog
        {
            // The application confirms the exact observed version after the picker closes.
            Filter = Filter, DefaultExt = ".adoc", AddExtension = true, OverwritePrompt = false,
            FileName = currentPath is null ? "無題.adoc" : Path.GetFileName(currentPath),
            InitialDirectory = currentPath is null ? "" : Path.GetDirectoryName(currentPath)
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public SaveDecision ConfirmSave(string documentName) => MessageBox.Show(owner,
        $"「{documentName}」への変更を保存しますか？", "SYUAS", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
    {
        MessageBoxResult.Yes => SaveDecision.Save,
        MessageBoxResult.No => SaveDecision.Discard,
        _ => SaveDecision.Cancel
    };

    public void ShowError(string message) => MessageBox.Show(owner, message, "SYUAS", MessageBoxButton.OK, MessageBoxImage.Error);
    public void ShowInformation(string message) => MessageBox.Show(owner, message, "SYUAS", MessageBoxButton.OK, MessageBoxImage.Information);

    public SaveConflictDecision ResolveSaveConflict(string path, FileObservation observation, bool isCurrentFile)
    {
        var dialog = new SaveConflictDialog(new SaveConflictViewModel(path, observation, isCurrentFile)) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Decision : SaveConflictDecision.Cancel;
    }
}
