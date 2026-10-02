using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syuas.Core.Editor;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEditorAdapter editor;
    private readonly DocumentSessionController documents;
    private readonly IUserDialogs dialogs;
    private readonly IRecentFilesStore recentStore;
    private string searchText = "";
    private string replacementText = "";
    private string searchStatus = "";
    private bool matchCase;
    private bool isSearchVisible;
    private bool isPreviewVisible;
    private RecoveryService? recovery;
    private bool isRecoveryBusy;
    private string recoveryStatus = "";

    public MainViewModel(IEditorAdapter editor, IFileService files, IUserDialogs dialogs, IRecentFilesStore recentStore, IInputAssistanceDialogs? inputDialogs = null)
    {
        this.editor = editor;
        documents = new(editor, files);
        documents.SessionChanged += OnSessionChanged;
        this.dialogs = dialogs;
        this.recentStore = recentStore;
        Structure = new(editor);
        if (inputDialogs is not null) Assistance = new(editor, () => FilePath, inputDialogs);
        NewCommand = new(_ => New());
        OpenCommand = new(_ => { var path = dialogs.ChooseOpenFile(); if (path is not null) Open(path); });
        OpenRecentCommand = new(p => { if (p is string path) Open(path); });
        SaveCommand = new(_ => Save());
        SaveAsCommand = new(_ => Save(true));
        UndoCommand = new(_ => editor.Undo(), _ => editor.CanUndo);
        RedoCommand = new(_ => editor.Redo(), _ => editor.CanRedo);
        FindNextCommand = new(_ => FindNext(), _ => SearchText.Length > 0);
        ReplaceCommand = new(_ => Replace(), _ => SearchText.Length > 0);
        ReplaceAllCommand = new(_ => ReplaceAll(), _ => SearchText.Length > 0);
        editor.StateChanged += EditorStateChanged;
        try { foreach (var path in recentStore.Load()) RecentFiles.Add(path); }
        catch (Exception e) when (IsStorageError(e) || e is JsonException) { /* History must not prevent editing. */ }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public InputAssistanceViewModel? Assistance { get; }
    public DocumentStructureViewModel Structure { get; }
    public ObservableCollection<string> RecentFiles { get; } = [];
    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenRecentCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand FindNextCommand { get; }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand ReplaceAllCommand { get; }
    public DocumentSession Session => documents.Session;
    public string? FilePath => Session.FilePath;
    public string DocumentName => FilePath is null ? "無題" : Path.GetFileName(FilePath);
    public string Title => $"{(editor.IsModified ? "* " : "")}{DocumentName} — SYUAS";
    public string Position => $"Ln {editor.Line}, Col {editor.Column}";
    public string DocumentStatus => editor.IsModified ? "未保存の変更" : "保存済み";
    public string SearchText { get => searchText; set { searchText = value; SearchStatus = ""; Changed(); RefreshSearch(); } }
    public string ReplacementText { get => replacementText; set { replacementText = value; Changed(); } }
    public bool MatchCase { get => matchCase; set { matchCase = value; SearchStatus = ""; Changed(); } }
    public bool IsSearchVisible { get => isSearchVisible; set { isSearchVisible = value; Changed(); } }
    public bool IsPreviewVisible { get => isPreviewVisible; set { isPreviewVisible = value; Changed(); } }
    public string SearchStatus { get => searchStatus; private set { searchStatus = value; Changed(); } }
    public bool IsRecoveryBusy { get => isRecoveryBusy; private set { isRecoveryBusy = value; Changed(); } }
    public string RecoveryStatus { get => recoveryStatus; private set { recoveryStatus = value; Changed(); } }
    public bool HasRecovery => recovery is not null;

    public void EnableRecovery(RecoveryService service)
    {
        if (recovery is not null) throw new InvalidOperationException("自動復元は初期化済みです。");
        recovery = service;
        recovery.Track(Session);
        RecoveryStatus = recovery.Status;
    }

    public void ReportRecoveryError(string message) => RecoveryStatus = $"自動復元: {message}";

    public async Task TickRecoveryAsync()
    {
        if (recovery is null || IsRecoveryBusy) return;
        await recovery.TickAsync(() => new(Session.DocumentId, Session.Revision, DateTimeOffset.UtcNow,
            editor.Text, Session.Baseline, editor.SelectionStart, editor.SelectionLength, editor.CaretOffset));
        RecoveryStatus = recovery.Status;
    }

    public Task<IReadOnlyList<RecoveryCandidate>> ListRecoveryAsync() => recovery?.ListAsync()
        ?? Task.FromResult<IReadOnlyList<RecoveryCandidate>>([]);

    public async Task<bool> RestoreRecoveryAsync(RecoveryKey key)
    {
        if (recovery is null || IsRecoveryBusy || !ConfirmDiscard()) return false;
        IsRecoveryBusy = true;
        try
        {
            var snapshot = await recovery.ClaimAsync(key);
            documents.Restore(snapshot);
            DocumentChanged();
            RecoveryStatus = "復元しました。元ファイルへ保存するまで復元用コピーを保持します。";
            return true;
        }
        catch (Exception e) when (IsStorageError(e))
        {
            dialogs.ShowError($"復元できませんでした。現在の文書は保持しています。\n{e.Message}");
            return false;
        }
        finally { IsRecoveryBusy = false; }
    }

    public async Task DiscardRecoveryAsync(RecoveryKey key)
    {
        if (recovery is null || IsRecoveryBusy) return;
        IsRecoveryBusy = true;
        try { await recovery.DiscardAsync(key); }
        finally { IsRecoveryBusy = false; }
    }

    // Call only after closing has been confirmed; CanClose itself must remain non-destructive.
    public async Task CloseRecoveryAsync()
    {
        if (recovery is null) return;
        IsRecoveryBusy = true;
        await recovery.CloseAsync();
    }

    public bool CanClose() => !IsRecoveryBusy && ConfirmDiscard();

    public bool New()
    {
        if (IsRecoveryBusy || !ConfirmDiscard()) return false;
        documents.New();
        DocumentChanged();
        return true;
    }

    public bool Open(string path)
    {
        if (IsRecoveryBusy || !ConfirmDiscard()) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            documents.Open(fullPath);
            Remember(fullPath);
            DocumentChanged();
            return true;
        }
        catch (Exception e) when (IsStorageError(e))
        {
            dialogs.ShowError($"ファイルを開けませんでした。UTF-8のファイルを選択してください。\n{e.Message}");
            return false;
        }
    }

    public bool Save(bool saveAs = false)
    {
        if (IsRecoveryBusy) return false;
        var path = saveAs || FilePath is null ? dialogs.ChooseSaveFile(FilePath) : FilePath;
        if (path is null) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var expected = SamePath(fullPath, FilePath) ? Session.Baseline : null;
            var preserveBackup = false;
            FileObservation? conflict = null;
            while (true)
            {
                if (conflict is null)
                {
                    var result = documents.Save(fullPath, expected, preserveBackup);
                    if (result.Succeeded)
                    {
                        Remember(fullPath);
                        DocumentChanged();
                        if (result.BackupPath is not null)
                            dialogs.ShowInformation($"保存しました。置き換え前のファイルを次の場所へ退避しました。\n{result.BackupPath}");
                        return true;
                    }
                    conflict = result.Conflict!;
                }

                var decision = dialogs.ResolveSaveConflict(fullPath, conflict, SamePath(fullPath, FilePath));
                switch (decision)
                {
                    case SaveConflictDecision.SaveAs:
                        var alternative = dialogs.ChooseSaveFile(fullPath);
                        if (alternative is null) return false;
                        fullPath = Path.GetFullPath(alternative);
                        expected = SamePath(fullPath, FilePath) ? Session.Baseline : null;
                        preserveBackup = false;
                        break;
                    case SaveConflictDecision.OverwriteWithBackup when conflict.Status == FileObservationStatus.Present:
                        expected = conflict.Baseline!;
                        preserveBackup = true;
                        break;
                    case SaveConflictDecision.Recreate when conflict.Status == FileObservationStatus.Missing:
                        expected = null;
                        preserveBackup = false;
                        break;
                    case SaveConflictDecision.Retry when conflict.Status == FileObservationStatus.Unavailable:
                        // Retry the observation, not the approval. If it changed, require a new decision.
                        var current = documents.Observe(fullPath);
                        if (current.Status == FileObservationStatus.Unavailable ||
                            (expected is null ? current.Status != FileObservationStatus.Missing :
                                current.Status != FileObservationStatus.Present || current.Baseline!.Fingerprint != expected.Fingerprint))
                        {
                            conflict = current;
                            continue;
                        }
                        break;
                    default:
                        return false;
                }
                conflict = null;
            }
        }
        catch (Exception e) when (IsStorageError(e))
        {
            dialogs.ShowError($"保存できませんでした。変更内容はエディタに残っています。\n{e.Message}");
            return false;
        }
    }

    private static bool SamePath(string path, string? other) => string.Equals(path, other, StringComparison.OrdinalIgnoreCase);

    private bool ConfirmDiscard() => !editor.IsModified || dialogs.ConfirmSave(DocumentName) switch
    {
        SaveDecision.Save => Save(),
        SaveDecision.Discard => true,
        _ => false
    };

    private void Remember(string path)
    {
        for (var i = RecentFiles.Count - 1; i >= 0; i--)
            if (string.Equals(RecentFiles[i], path, StringComparison.OrdinalIgnoreCase)) RecentFiles.RemoveAt(i);
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10) RecentFiles.RemoveAt(10);
        try { recentStore.Save(RecentFiles.ToArray()); }
        catch (Exception e) when (IsStorageError(e)) { /* Document save succeeded; history is best effort. */ }
    }

    public void FindNext()
    {
        if (SearchText.Length == 0) return;
        var index = TextSearch.FindNext(editor.Text, SearchText, editor.SelectionStart + editor.SelectionLength, MatchCase);
        SearchStatus = index < 0 ? "見つかりません" : "";
        if (index >= 0) editor.Select(index, SearchText.Length);
    }

    private void Replace()
    {
        if (SearchText.Length == 0) return;
        var selected = editor.Text.Substring(editor.SelectionStart, editor.SelectionLength);
        if (string.Equals(selected, SearchText, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            var start = editor.SelectionStart;
            using (editor.BeginUpdate()) editor.Replace(start, editor.SelectionLength, ReplacementText);
            editor.Select(start + ReplacementText.Length, 0);
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        var matches = TextSearch.FindAll(editor.Text, SearchText, MatchCase);
        using (editor.BeginUpdate())
            foreach (var index in matches.Reverse()) editor.Replace(index, SearchText.Length, ReplacementText);
        SearchStatus = $"{matches.Count} 件を置換しました";
    }

    private void EditorStateChanged(object? sender, EventArgs e)
    {
        Changed(nameof(Title)); Changed(nameof(Position)); Changed(nameof(DocumentStatus));
        UndoCommand.Refresh(); RedoCommand.Refresh();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        recovery?.Track(Session);
        Changed(nameof(Session));
    }

    private void DocumentChanged()
    {
        Changed(nameof(FilePath)); Changed(nameof(DocumentName)); Changed(nameof(Title));
        SearchStatus = "";
    }

    private void RefreshSearch() { FindNextCommand.Refresh(); ReplaceCommand.Refresh(); ReplaceAllCommand.Refresh(); }
    private static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        editor.StateChanged -= EditorStateChanged;
        documents.SessionChanged -= OnSessionChanged;
        documents.Dispose();
        recovery?.Dispose();
    }
}
