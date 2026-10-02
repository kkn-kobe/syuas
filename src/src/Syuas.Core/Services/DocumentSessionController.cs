using Syuas.Core.Editor;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Use on the editor's owning thread. Dialog decisions belong to the caller.
public sealed class DocumentSessionController : IDisposable
{
    private readonly IEditorAdapter editor;
    private readonly IFileService files;
    private bool changingDocument;

    public DocumentSessionController(IEditorAdapter editor, IFileService files)
    {
        this.editor = editor;
        this.files = files;
        Session = new(Guid.NewGuid(), editor.ContentRevision, null, null, editor.IsModified);
        editor.ContentChanged += OnEditorChanged;
        editor.StateChanged += OnEditorChanged;
    }

    public DocumentSession Session { get; private set; }
    public event EventHandler? SessionChanged;

    public void New() => Load(null);

    public void Open(string path)
    {
        // Do not replace the session or touch Undo until the read/UTF-8 decoding succeeds.
        var snapshot = files.ReadSnapshot(path);
        Load(snapshot);
    }

    private void Load(FileSnapshot? snapshot)
    {
        changingDocument = true;
        try
        {
            editor.Load(snapshot?.Text ?? "");
            SetSession(new(Guid.NewGuid(), editor.ContentRevision,
                snapshot is null ? null : editor.ContentRevision, snapshot?.Baseline, editor.IsModified));
        }
        finally { changingDocument = false; }
    }

    public FileObservation Observe(string path) => files.Observe(path);

    public void Restore(RecoverySnapshot snapshot)
    {
        changingDocument = true;
        try
        {
            editor.LoadRecovery(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionLength, snapshot.CaretOffset);
            // Preserve the original baseline; adopting today's disk version would bypass conflict checks.
            SetSession(new(snapshot.DocumentId, editor.ContentRevision, null, snapshot.Baseline, editor.IsModified));
        }
        finally { changingDocument = false; }
    }

    public FileSaveResult Save(string path, FileBaseline? expected, bool preserveBackup = false)
    {
        // Synchronous for now: no user edits can interleave with saving on the UI thread.
        var result = files.WriteChecked(path, editor.Text, expected, preserveBackup);
        if (!result.Succeeded) return result;
        changingDocument = true;
        try
        {
            editor.MarkSaved();
            SetSession(Session with
            {
                Baseline = result.Baseline,
                Revision = editor.ContentRevision,
                SavedRevision = editor.ContentRevision,
                IsModified = editor.IsModified
            });
        }
        finally { changingDocument = false; }
        return result;
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (changingDocument) return;
        SetSession(Session with { Revision = editor.ContentRevision, IsModified = editor.IsModified });
    }

    private void SetSession(DocumentSession session)
    {
        if (Session == session) return;
        Session = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        editor.ContentChanged -= OnEditorChanged;
        editor.StateChanged -= OnEditorChanged;
    }
}
