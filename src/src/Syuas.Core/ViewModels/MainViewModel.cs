using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

// Owns the workspace; each child owns one editor and its document services.
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<DocumentTabViewModel> createDocument;
    private readonly IUserDialogs dialogs;
    private readonly IRecentFilesStore recentStore;
    private readonly ObservableCollection<DocumentTabViewModel> documents = [];
    private readonly Dictionary<DocumentTabViewModel, Guid> trackedIds = [];
    private DocumentTabViewModel activeDocument = null!;
    private RecoveryService? recovery;
    private int untitledNumber;
    private bool disposed, busy, ticking, previewVisible, searchVisible;
    private string recoveryStatus = "";
    private string searchText = "", replacementText = "";
    private bool matchCase;
    private bool reordering;

    public MainViewModel(Func<DocumentTabViewModel> createDocument, IUserDialogs dialogs, IRecentFilesStore recentStore)
    {
        this.createDocument = createDocument;
        this.dialogs = dialogs;
        this.recentStore = recentStore;
        Documents = new(documents);
        NewCommand = new(_ => New(), _ => !IsBusy);
        OpenCommand = new(_ => OpenMany(dialogs.ChooseOpenFiles()), _ => !IsBusy);
        OpenRecentCommand = new(p => { if (p is string path) Open(path); }, _ => !IsBusy);
        SaveCommand = new(_ => Save(), _ => !IsBusy);
        SaveAsCommand = new(_ => Save(true), _ => !IsBusy);
        SaveAllCommand = new(_ => SaveAll(), _ => !IsBusy);
        CloseDocumentCommand = new(async p => await CloseDocumentAsync(p as DocumentTabViewModel ?? ActiveDocument), _ => !IsBusy);
        try { foreach (var path in recentStore.Load()) RecentFiles.Add(path); }
        catch (Exception e) when (IsStorageError(e) || e is JsonException) { }
        Add(Create());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ActiveDocumentChanged;
    public ReadOnlyObservableCollection<DocumentTabViewModel> Documents { get; }
    public ObservableCollection<string> RecentFiles { get; } = [];
    public DocumentTabViewModel ActiveDocument
    {
        get => activeDocument;
        set
        {
            if (ReferenceEquals(value, activeDocument) || value is null || !documents.Contains(value) || IsBusy || reordering) return;
            activeDocument = value;
            value.SearchText = searchText;
            value.ReplacementText = replacementText;
            value.MatchCase = matchCase;
            value.Structure.Refresh();
            Changed("");
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool IsBusy => busy || disposed || documents.Any(d => d.IsTableEditing || d.IsRecoveryBusy);
    public bool IsRecoveryBusy => busy;
    public bool HasRecovery => recovery is not null;
    public string RecoveryStatus { get => recoveryStatus; private set { recoveryStatus = value; Changed(); } }
    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenRecentCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand SaveAllCommand { get; }
    public RelayCommand CloseDocumentCommand { get; }
    public RelayCommand UndoCommand => ActiveDocument.UndoCommand;
    public RelayCommand RedoCommand => ActiveDocument.RedoCommand;
    public RelayCommand EditTableCommand => ActiveDocument.EditTableCommand;
    public RelayCommand FindNextCommand => ActiveDocument.FindNextCommand;
    public RelayCommand ReplaceCommand => ActiveDocument.ReplaceCommand;
    public RelayCommand ReplaceAllCommand => ActiveDocument.ReplaceAllCommand;
    public RelayCommand DismissExternalChangeCommand => ActiveDocument.DismissExternalChangeCommand;
    public RelayCommand RevealExternalChangeCommand => ActiveDocument.RevealExternalChangeCommand;
    public InputAssistanceViewModel? Assistance => ActiveDocument.Assistance;
    public DocumentStructureViewModel Structure => ActiveDocument.Structure;
    public DocumentSession Session => ActiveDocument.Session;
    public string? FilePath => ActiveDocument.FilePath;
    public string Title => ActiveDocument.Title;
    public string Position => ActiveDocument.Position;
    public string DocumentStatus => ActiveDocument.DocumentStatus;
    public string SearchStatus => ActiveDocument.SearchStatus;
    public bool IsExternalChangeVisible => ActiveDocument.IsExternalChangeVisible;
    public bool HasExternalChange => ActiveDocument.HasExternalChange;
    public bool CanReadExternalFile => ActiveDocument.CanReadExternalFile;
    public string ExternalChangeMessage => ActiveDocument.ExternalChangeMessage;
    public string ExternalChangeDetail => ActiveDocument.ExternalChangeDetail;
    public string ExternalChangeStatus => ActiveDocument.ExternalChangeStatus;
    public string MonitoringStatus => ActiveDocument.MonitoringStatus;
    public bool IsSearchVisible { get => searchVisible; set { searchVisible = value; Changed(); } }
    public bool IsPreviewVisible { get => previewVisible; set { previewVisible = value; Changed(); } }
    public string SearchText { get => searchText; set { searchText = value; ActiveDocument.SearchText = value; Changed(); } }
    public string ReplacementText { get => replacementText; set { replacementText = value; ActiveDocument.ReplacementText = value; Changed(); } }
    public bool MatchCase { get => matchCase; set { matchCase = value; ActiveDocument.MatchCase = value; Changed(); } }

    private DocumentTabViewModel Create()
    {
        var document = createDocument();
        document.UntitledName = $"無題{++untitledNumber}";
        document.RememberFile = Remember;
        document.CanSavePath = path => !documents.Any(other => other != document && SamePath(path, other.FilePath));
        return document;
    }
    private void Add(DocumentTabViewModel document)
    {
        documents.Add(document);
        document.PropertyChanged += OnDocumentChanged;
        Track(document);
        RefreshNames();
        ActiveDocument = document;
    }
    public bool New()
    {
        if (IsBusy) return false;
        Add(Create());
        return true;
    }
    public bool Open(string path) => OpenCore(path, null);
    private bool OpenCore(string path, List<string>? errors)
    {
        if (IsBusy) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var existing = documents.FirstOrDefault(d => !d.RequiresSaveAs && SamePath(d.FilePath, fullPath))
                ?? documents.FirstOrDefault(d => SamePath(d.FilePath, fullPath));
            if (existing is not null) { ActiveDocument = existing; Remember(fullPath); return true; }
            var document = Create();
            if (!document.Open(fullPath, reportError: errors is null))
            {
                if (document.OpenError is { } error) errors?.Add(error);
                document.Dispose();
                return false;
            }
            Add(document);
            return true;
        }
        catch (Exception e) when (IsStorageError(e))
        {
            var error = $"ファイルを開けませんでした。\n{path}\n{e.Message}";
            if (errors is null) dialogs.ShowError(error); else errors.Add(error);
            return false;
        }
    }
    public void OpenMany(IEnumerable<string> paths)
    {
        // Each successful open remains available even if another file fails.
        var errors = new List<string>();
        foreach (var path in paths) OpenCore(path, errors);
        if (errors.Count > 0) dialogs.ShowError(string.Join("\n\n", errors));
    }
    public bool Save(bool saveAs = false) => !IsBusy && ActiveDocument.Save(saveAs);
    public bool SaveAll()
    {
        if (IsBusy) return false;
        var selected = ActiveDocument;
        foreach (var document in documents.Where(d => d.Session.IsModified).ToArray())
        {
            ActiveDocument = document;
            if (!document.Save()) return false;
        }
        ActiveDocument = selected;
        return true;
    }
    public void SelectRelative(int offset)
    {
        var index = documents.IndexOf(ActiveDocument);
        ActiveDocument = documents[(index + offset + documents.Count) % documents.Count];
    }
    // insertionIndex is a boundary in the original list: 0 is before the first tab, Count is after the last.
    public bool MoveDocument(DocumentTabViewModel document, int insertionIndex)
    {
        if (IsBusy || reordering || insertionIndex < 0 || insertionIndex > documents.Count) return false;
        var oldIndex = documents.IndexOf(document);
        if (oldIndex < 0) return false;
        var newIndex = insertionIndex > oldIndex ? insertionIndex - 1 : insertionIndex;
        if (newIndex == oldIndex) return true;
        reordering = true;
        try { documents.Move(oldIndex, newIndex); }
        finally { reordering = false; }
        // WPF may update selection while processing the collection notification. Keep the same document active.
        Changed(nameof(ActiveDocument));
        return true;
    }
    public async Task<bool> CloseDocumentAsync(DocumentTabViewModel document)
    {
        if (IsBusy || !documents.Contains(document)) return false;
        var selected = ActiveDocument;
        ActiveDocument = document;
        if (!document.CanClose()) return false;
        SetBusy(true);
        try
        {
            if (recovery is not null)
            {
                if (!await recovery.RemoveDocumentAsync(document.Session.DocumentId))
                    dialogs.ShowInformation("復元用コピーを整理できませんでした。次回起動時に再表示される場合があります。");
            }
            if (disposed) return false;
            var index = documents.IndexOf(document);
            document.PropertyChanged -= OnDocumentChanged;
            trackedIds.Remove(document);
            documents.Remove(document);
            document.Dispose();
            if (documents.Count == 0) Add(Create());
            RefreshNames();
            // Selection must be updated while mutation is still protected from re-entry.
            activeDocument = documents.Contains(selected) ? selected : documents[Math.Min(index, documents.Count - 1)];
        }
        finally { SetBusy(false); }
        ActiveDocument.SearchText = searchText;
        ActiveDocument.ReplacementText = replacementText;
        ActiveDocument.MatchCase = matchCase;
        ActiveDocument.Structure.Refresh();
        Changed("");
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    // Confirmation does not remove tabs or retire discarded drafts. Commit only after all agree.
    public bool CanClose()
    {
        if (IsBusy) return false;
        foreach (var document in documents.ToArray())
        {
            if (!document.Session.IsModified) continue;
            ActiveDocument = document;
            if (!document.CanClose()) return false;
        }
        return true;
    }
    public void FindNext() => ActiveDocument.FindNext();
    public bool ReloadFromDisk() => !IsBusy && ActiveDocument.ReloadFromDisk();
    public Task<DocumentComparison?> ReadComparisonAsync() => ActiveDocument.ReadComparisonAsync();
    public async Task CheckExternalChangesAsync(bool force = false)
    {
        if (IsBusy) return;
        foreach (var document in documents.ToArray())
        {
            if (disposed) return;
            await document.CheckExternalChangesAsync(force);
        }
    }
    public void EnableRecovery(RecoveryService service)
    {
        if (recovery is not null) throw new InvalidOperationException("自動復元は初期化済みです。");
        recovery = service;
        foreach (var document in documents) Track(document);
        RecoveryStatus = service.Status;
    }
    private void Track(DocumentTabViewModel document)
    {
        if (trackedIds.TryGetValue(document, out var previous) && previous != document.Session.DocumentId)
            _ = recovery?.RemoveDocumentAsync(previous);
        trackedIds[document] = document.Session.DocumentId;
        recovery?.TrackDocument(document.Session);
    }
    public async Task TickRecoveryAsync()
    {
        if (recovery is null || IsBusy || ticking) return;
        ticking = true;
        try
        {
            foreach (var document in documents.ToArray())
            {
                if (disposed) return;
                if (documents.Contains(document))
                    await recovery.TickDocumentAsync(document.Session.DocumentId, document.CaptureRecovery);
            }
            RecoveryStatus = recovery.Status;
        }
        finally { ticking = false; }
    }
    public Task<IReadOnlyList<RecoveryCandidate>> ListRecoveryAsync() =>
        recovery?.ListAsync() ?? Task.FromResult<IReadOnlyList<RecoveryCandidate>>([]);
    public async Task<bool> RestoreRecoveryAsync(RecoveryKey key)
    {
        if (recovery is null || IsBusy) return false;
        SetBusy(true);
        DocumentTabViewModel? document = null;
        try
        {
            document = Create();
            var snapshot = await recovery.ClaimAsync(key);
            document.RequiresSaveAs = documents.Any(d => SamePath(d.FilePath, snapshot.Baseline?.FullPath));
            document.RestoreSnapshot(snapshot);
            if (disposed) { document.Dispose(); return false; }
            Add(document);
            activeDocument = document;
            RecoveryStatus = "復元しました。保存するまで復元用コピーを保持します。";
        }
        catch (Exception e) when (IsStorageError(e))
        {
            document?.Dispose();
            dialogs.ShowError($"復元できませんでした。現在の文書は保持しています。\n{e.Message}");
            return false;
        }
        finally { SetBusy(false); }
        ActiveDocument.SearchText = searchText;
        ActiveDocument.ReplacementText = replacementText;
        ActiveDocument.MatchCase = matchCase;
        Changed("");
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    public async Task DiscardRecoveryAsync(RecoveryKey key)
    {
        if (recovery is null || IsBusy) return;
        SetBusy(true);
        try { await recovery.DiscardAsync(key); }
        finally { SetBusy(false); }
    }
    public async Task CloseRecoveryAsync()
    {
        if (recovery is null) return;
        SetBusy(true);
        if (!await recovery.CloseAsync())
            dialogs.ShowInformation("復元用コピーを整理できませんでした。次回起動時に再表示される場合があります。");
    }
    public void ReportRecoveryError(string message) => RecoveryStatus = $"自動復元: {message}";
    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTabViewModel document) return;
        if (e.PropertyName == nameof(DocumentTabViewModel.Session)) Track(document);
        if (e.PropertyName == nameof(DocumentTabViewModel.FilePath)) RefreshNames();
        if (e.PropertyName is nameof(DocumentTabViewModel.IsRecoveryBusy) or nameof(DocumentTabViewModel.IsTableEditing))
            RefreshCommands();
        if (ReferenceEquals(document, ActiveDocument)) Changed(e.PropertyName);
    }
    private void RefreshNames()
    {
        foreach (var document in documents)
        {
            var duplicate = document.FilePath is not null && documents.Count(d =>
                string.Equals(d.DocumentName, document.DocumentName, StringComparison.OrdinalIgnoreCase)) > 1;
            document.TabName = document.DocumentName
                + (duplicate ? $" — {Path.GetDirectoryName(document.FilePath)}" : "")
                + (document.RequiresSaveAs ? "（復元）" : "");
        }
    }
    private void Remember(string path)
    {
        for (var i = RecentFiles.Count - 1; i >= 0; i--)
            if (SamePath(RecentFiles[i], path)) RecentFiles.RemoveAt(i);
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10) RecentFiles.RemoveAt(10);
        try { recentStore.Save(RecentFiles.ToArray()); }
        catch (Exception e) when (IsStorageError(e)) { }
    }
    private void SetBusy(bool value) { busy = value; Changed(nameof(IsRecoveryBusy)); RefreshCommands(); }
    private void RefreshCommands()
    {
        Changed(nameof(IsBusy));
        NewCommand.Refresh(); OpenCommand.Refresh(); OpenRecentCommand.Refresh();
        SaveCommand.Refresh(); SaveAsCommand.Refresh(); SaveAllCommand.Refresh(); CloseDocumentCommand.Refresh();
    }
    private static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool IsStorageError(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var document in documents)
        {
            document.PropertyChanged -= OnDocumentChanged;
            document.Dispose();
        }
        recovery?.Dispose();
    }
}
