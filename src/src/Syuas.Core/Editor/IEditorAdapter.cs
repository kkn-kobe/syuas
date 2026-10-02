namespace Syuas.Core.Editor;

// No editor-component types cross this boundary.
public interface IEditorAdapter
{
    event EventHandler? StateChanged;
    event EventHandler? ContentChanged;
    long ContentRevision { get; }
    string Text { get; }
    bool IsModified { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    int SelectionStart { get; }
    int SelectionLength { get; }
    int CaretOffset { get; }
    int Line { get; }
    int Column { get; }
    void Load(string text);
    void LoadRecovery(string text, int selectionStart, int selectionLength, int caretOffset);
    void MarkSaved();
    void Select(int start, int length);
    void Replace(int start, int length, string text);
    IDisposable BeginUpdate();
    void Undo();
    void Redo();
}
