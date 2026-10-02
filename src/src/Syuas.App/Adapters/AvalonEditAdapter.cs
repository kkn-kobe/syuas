using System.ComponentModel;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Syuas.Core.Editor;

namespace Syuas.App.Adapters;

public sealed class AvalonEditAdapter : IEditorAdapter, IDisposable
{
    private readonly TextEditor editor;

    public AvalonEditAdapter(TextEditor editor)
    {
        this.editor = editor;
        editor.TextChanged += OnContentChanged;
        editor.TextArea.Caret.PositionChanged += OnStateChanged;
        editor.TextArea.SelectionChanged += OnStateChanged;
        editor.Document.UndoStack.PropertyChanged += OnUndoChanged;
        MarkSaved();
    }

    public event EventHandler? StateChanged;
    public event EventHandler? ContentChanged;
    public long ContentRevision { get; private set; }
    public string Text => editor.Text;
    public bool IsModified => !editor.Document.UndoStack.IsOriginalFile;
    public bool CanUndo => editor.CanUndo;
    public bool CanRedo => editor.CanRedo;
    public int SelectionStart => editor.SelectionStart;
    public int SelectionLength => editor.SelectionLength;
    public int CaretOffset => editor.CaretOffset;
    public int Line => editor.TextArea.Caret.Line;
    public int Column => editor.TextArea.Caret.Column;

    public void Load(string text)
    {
        editor.Text = text;
        editor.Document.UndoStack.ClearAll();
        editor.CaretOffset = 0;
        editor.ScrollToHome();
        MarkSaved();
    }

    public void MarkSaved()
    {
        editor.Document.UndoStack.MarkAsOriginalFile();
        OnStateChanged(this, EventArgs.Empty);
    }

    public void LoadRecovery(string text, int selectionStart, int selectionLength, int caretOffset)
    {
        Load(text);
        // Recovered text has never been saved in this process, even when it is empty.
        editor.Document.UndoStack.DiscardOriginalFileMarker();
        var start = Math.Clamp(selectionStart, 0, text.Length);
        Select(start, Math.Clamp(selectionLength, 0, text.Length - start));
        editor.CaretOffset = Math.Clamp(caretOffset, 0, text.Length);
        OnStateChanged(this, EventArgs.Empty);
    }

    public void Select(int start, int length)
    {
        editor.Select(start, length);
        editor.ScrollTo(editor.Document.GetLocation(start).Line, editor.Document.GetLocation(start).Column);
    }

    public void Replace(int start, int length, string text) => editor.Document.Replace(start, length, text);
    public IDisposable BeginUpdate() => editor.Document.RunUpdate();
    public void Undo() => editor.Undo();
    public void Redo() => editor.Redo();
    private void OnContentChanged(object? sender, EventArgs e)
    {
        ContentRevision++;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        OnStateChanged(sender, e);
    }
    private void OnStateChanged(object? sender, EventArgs e) => StateChanged?.Invoke(this, EventArgs.Empty);
    private void OnUndoChanged(object? sender, PropertyChangedEventArgs e) => OnStateChanged(sender, e);

    public void Dispose()
    {
        editor.TextChanged -= OnContentChanged;
        editor.TextArea.Caret.PositionChanged -= OnStateChanged;
        editor.TextArea.SelectionChanged -= OnStateChanged;
        editor.Document.UndoStack.PropertyChanged -= OnUndoChanged;
    }
}
