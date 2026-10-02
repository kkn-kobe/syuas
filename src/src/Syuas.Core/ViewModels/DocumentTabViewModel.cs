using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syuas.Core.Editor;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public sealed class DocumentTabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEditorAdapter editor;
    private readonly DocumentSessionController documents;
    private readonly IFileService files;
    private readonly IUserDialogs dialogs;
    private readonly IRecentFilesStore recentStore;
    private readonly ITableEditingDialogs? tableDialogs;
    private readonly TableEditingService tableEditing;
    private bool isTableEditing;
    private string searchText = "";
    private string replacementText = "";
    private string searchStatus = "";
    private bool matchCase;
    private bool isSearchVisible;
    private bool isPreviewVisible;
    private RecoveryService? recovery;
    private bool isRecoveryBusy;
    private string recoveryStatus = "";
    private ExternalChangeService? externalChanges;
    private bool disposed;

    public DocumentTabViewModel(IEditorAdapter editor, IFileService files, IUserDialogs dialogs, IRecentFilesStore recentStore,
        IInputAssistanceDialogs? inputDialogs = null, ITableEditingDialogs? tableDialogs = null)
    {
        this.editor = editor;
        this.files = files;
        documents = new(editor, files);
        documents.SessionChanged += OnSessionChanged;
        this.dialogs = dialogs;
        this.recentStore = recentStore;
        this.tableDialogs = tableDialogs;
        tableEditing = new(editor, () => Session);
        EditTableCommand = new(_ => EditTable(), _ => tableDialogs is not null && !IsRecoveryBusy && !IsTableEditing && !disposed);
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
        DismissExternalChangeCommand = new(_ => externalChanges?.Dismiss());
        RevealExternalChangeCommand = new(_ => externalChanges?.Reveal());
        editor.StateChanged += EditorStateChanged;
        try { foreach (var path in recentStore.Load()) RecentFiles.Add(path); }
        catch (Exception e) when (IsStorageError(e) || e is JsonException) { /* History must not prevent editing. */ }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? EditorFocusRequested;
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
    public RelayCommand EditTableCommand { get; }
    public RelayCommand FindNextCommand { get; }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand ReplaceAllCommand { get; }
    public RelayCommand DismissExternalChangeCommand { get; }
    public RelayCommand RevealExternalChangeCommand { get; }
    public DocumentSession Session => documents.Session;
    public string? FilePath => Session.FilePath;
    public string UntitledName { get; set; } = "無題";
    public string DocumentName => FilePath is null ? UntitledName : Path.GetFileName(FilePath);
    private string tabName = "";
    public string TabName { get => tabName; internal set { tabName = value; Changed(); Changed(nameof(TabTitle)); } }
    public string TabTitle => $"{(Session.IsModified ? "* " : "")}{TabName}{(HasExternalChange ? " ⚠" : "")}";
    public string ToolTip => FilePath ?? DocumentName;
    public Func<string, bool>? CanSavePath { get; set; }
    public Action<string>? RememberFile { get; set; }
    public bool RequiresSaveAs { get; set; }
    public event EventHandler? Disposed;
    public RecoverySnapshot CaptureRecovery() => new(Session.DocumentId, Session.Revision, DateTimeOffset.UtcNow,
        editor.Text, Session.Baseline, editor.SelectionStart, editor.SelectionLength, editor.CaretOffset);
    public void RestoreSnapshot(RecoverySnapshot snapshot)
    {
        documents.Restore(snapshot);
        DocumentChanged();
    }
    public string Title => $"{(editor.IsModified ? "* " : "")}{DocumentName} — SYUAS";
    public string Position => $"Ln {editor.Line}, Col {editor.Column}";
    public string DocumentStatus => editor.IsModified ? "未保存の変更" : "保存済み";
    public string SearchText { get => searchText; set { searchText = value; SearchStatus = ""; Changed(); RefreshSearch(); } }
    public string ReplacementText { get => replacementText; set { replacementText = value; Changed(); } }
    public bool MatchCase { get => matchCase; set { matchCase = value; SearchStatus = ""; Changed(); } }
    public bool IsSearchVisible { get => isSearchVisible; set { isSearchVisible = value; Changed(); } }
    public bool IsPreviewVisible { get => isPreviewVisible; set { isPreviewVisible = value; Changed(); } }
    public string SearchStatus { get => searchStatus; private set { searchStatus = value; Changed(); } }
    public bool IsRecoveryBusy { get => isRecoveryBusy; private set { isRecoveryBusy = value; Changed(); EditTableCommand.Refresh(); } }
    public bool IsTableEditing { get => isTableEditing; private set { isTableEditing = value; Changed(); EditTableCommand.Refresh(); } }
    public string RecoveryStatus { get => recoveryStatus; private set { recoveryStatus = value; Changed(); } }
    public bool HasRecovery => recovery is not null;

    private void EditTable()
    {
        if (!EditTableCommand.CanExecute(null)) return;
        IsTableEditing = true;
        try
        {
            var started = tableEditing.BeginEdit();
            if (!started.Succeeded)
            {
                dialogs.ShowInformation($"表を再編集できませんでした。\n{started.Diagnostic?.DisplayMessage}");
                return;
            }
            var context = started.Context;
            var model = new TableDesignerViewModel(context.Range.NewLine, context.Definition,
                () => tableEditing.Apply(context), context.Range.StartLine);
            tableDialogs!.Show(model);
        }
        finally
        {
            IsTableEditing = false;
            if (!disposed) EditorFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool IsExternalChangeVisible => externalChanges?.IsNotificationVisible == true;
    public bool HasExternalChange => externalChanges?.Current is { Status: not FileComparisonStatus.Unchanged };
    public bool CanReadExternalFile => externalChanges?.Current?.Status == FileComparisonStatus.Modified;
    public string ExternalChangeMessage => externalChanges?.Current?.Status switch
    {
        FileComparisonStatus.Modified => "このファイルは外部で変更されています。編集中の内容は保持しています。",
        FileComparisonStatus.Missing => "元ファイルが見つかりません。削除または移動された可能性があります。編集中の内容は保持しています。",
        FileComparisonStatus.Unavailable => "元ファイルの状態を確認できません。ロックやアクセス権を確認してください。",
        _ => ""
    };
    public string ExternalChangeDetail => externalChanges?.Current?.Error ?? FilePath ?? "";
    public string MonitoringStatus => externalChanges?.MonitoringStatus ?? "";
    public string ExternalChangeStatus => externalChanges?.Current?.Status switch
    {
        FileComparisonStatus.Modified => "外部変更あり",
        FileComparisonStatus.Missing => "元ファイルなし",
        FileComparisonStatus.Unavailable => "確認不能",
        _ => ""
    };

    public void EnableExternalMonitoring(ExternalChangeService service)
    {
        if (externalChanges is not null) throw new InvalidOperationException("外部変更の監視は開始済みです。");
        externalChanges = service;
        externalChanges.StateChanged += OnExternalChangeState;
        externalChanges.Track(Session);
    }

    public async Task CheckExternalChangesAsync(bool force = false)
    {
        if (disposed || externalChanges is null || IsRecoveryBusy) return;
        if (force) externalChanges.RequestCheck();
        await externalChanges.TickAsync();
    }

    public bool ReloadFromDisk()
    {
        var path = FilePath;
        if (path is null || IsRecoveryBusy || IsTableEditing || !ConfirmDiscard()) return false;
        try
        {
            // Read successfully before changing text, Undo history or the recovery copy.
            documents.Reload(path);
            Remember(path);
            DocumentChanged();
            return true;
        }
        catch (Exception e) when (IsStorageError(e))
        {
            dialogs.ShowError($"再読み込みできませんでした。現在の文書は保持しています。\n{e.Message}");
            externalChanges?.RequestCheck();
            return false;
        }
    }

    public async Task<DocumentComparison?> ReadComparisonAsync()
    {
        var path = FilePath;
        if (path is null || IsRecoveryBusy || disposed) return null;
        var id = Session.DocumentId;
        var text = editor.Text;
        try
        {
            var disk = await Task.Run(() => files.ReadSnapshot(path));
            if (disposed || Session.DocumentId != id || !SamePath(path, FilePath)) return null;
            return new(path, text, disk, DateTimeOffset.Now);
        }
        catch (Exception e) when (IsStorageError(e))
        {
            if (!disposed && Session.DocumentId == id)
                dialogs.ShowError($"比較用のファイルを読み込めませんでした。現在の文書は保持しています。\n{e.Message}");
            externalChanges?.RequestCheck();
            return null;
        }
    }

    private void OnExternalChangeState(object? sender, EventArgs e)
    {
        Changed(nameof(IsExternalChangeVisible)); Changed(nameof(HasExternalChange));
        Changed(nameof(CanReadExternalFile)); Changed(nameof(ExternalChangeMessage));
        Changed(nameof(ExternalChangeDetail)); Changed(nameof(MonitoringStatus));
        Changed(nameof(ExternalChangeStatus)); Changed(nameof(TabTitle));
    }

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
        if (recovery is null || IsRecoveryBusy || IsTableEditing || !ConfirmDiscard()) return false;
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
        if (recovery is null || IsRecoveryBusy || IsTableEditing) return;
        IsRecoveryBusy = true;
        try { await recovery.DiscardAsync(key); }
        finally { IsRecoveryBusy = false; }
    }

    // Call only after closing has been confirmed; CanClose itself must remain non-destructive.
    public async Task CloseRecoveryAsync()
    {
        if (recovery is null) return;
        IsRecoveryBusy = true;
        if (!await recovery.CloseAsync())
            dialogs.ShowInformation("復元用コピーの整理を完了できませんでした。\n次回起動時に、保存済み・破棄済みの文書が復元候補に再表示される場合があります。内容を確認して不要な候補を破棄してください。\n元ファイルへの保存結果は変わりません。詳しくは「ヘルプ → 自動復元と外部変更の使い方」を参照してください。");
    }

    public bool CanClose() => !IsRecoveryBusy && !IsTableEditing && ConfirmDiscard();

    public bool New()
    {
        if (IsRecoveryBusy || IsTableEditing || !ConfirmDiscard()) return false;
        documents.New();
        DocumentChanged();
        return true;
    }

    public string? OpenError { get; private set; }
    public bool Open(string path, bool reportError = true)
    {
        OpenError = null;
        if (IsRecoveryBusy || IsTableEditing || !ConfirmDiscard()) return false;
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
            OpenError = $"ファイルを開けませんでした。UTF-8のファイルを選択してください。\n{path}\n{e.Message}";
            if (reportError) dialogs.ShowError(OpenError);
            return false;
        }
    }

    public bool Save(bool saveAs = false)
    {
        if (IsRecoveryBusy || IsTableEditing) return false;
        var path = saveAs || RequiresSaveAs || FilePath is null ? dialogs.ChooseSaveFile(FilePath) : FilePath;
        if (path is null) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var expected = SamePath(fullPath, FilePath) ? Session.Baseline : null;
            var preserveBackup = false;
            FileObservation? conflict = null;
            while (true)
            {
                if (CanSavePath?.Invoke(fullPath) == false)
                {
                    dialogs.ShowInformation("この保存先は別のタブで開かれています。別の保存先を選択してください。");
                    var alternativePath = dialogs.ChooseSaveFile(fullPath);
                    if (alternativePath is null) return false;
                    fullPath = Path.GetFullPath(alternativePath);
                    expected = SamePath(fullPath, FilePath) ? Session.Baseline : null;
                    preserveBackup = false;
                    conflict = null;
                    continue;
                }
                if (conflict is null)
                {
                    var result = documents.Save(fullPath, expected, preserveBackup);
                    if (result.Succeeded)
                    {
                        RequiresSaveAs = false;
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
        if (RememberFile is not null) { RememberFile(path); return; }
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
        externalChanges?.Track(Session);
        Changed(nameof(Session)); Changed(nameof(TabTitle));
    }

    private void DocumentChanged()
    {
        externalChanges?.Track(Session, force: true);
        Changed(nameof(FilePath)); Changed(nameof(DocumentName)); Changed(nameof(Title)); Changed(nameof(ToolTip));
        SearchStatus = "";
    }

    private void RefreshSearch() { FindNextCommand.Refresh(); ReplaceCommand.Refresh(); ReplaceAllCommand.Refresh(); }
    private static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EditTableCommand.Refresh();
        if (externalChanges is not null)
        {
            externalChanges.StateChanged -= OnExternalChangeState;
            externalChanges.Dispose();
        }
        editor.StateChanged -= EditorStateChanged;
        documents.SessionChanged -= OnSessionChanged;
        documents.Dispose();
        recovery?.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}

