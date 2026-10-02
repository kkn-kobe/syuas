using System.Windows.Controls;
using Syuas.App.Adapters;
using Syuas.App.Highlighting;

namespace Syuas.App.Views;

// A view lives for the entire tab lifetime, including while it is hidden.
public partial class DocumentEditorView : UserControl, IDisposable
{
    private double horizontalOffset, verticalOffset;
    public AvalonEditAdapter Adapter { get; }
    public DocumentEditorView()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = AsciiDocHighlighting.Load();
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = false;
        Adapter = new(Editor);
    }
    public void HideEditor()
    {
        horizontalOffset = Editor.HorizontalOffset;
        verticalOffset = Editor.VerticalOffset;
        Visibility = System.Windows.Visibility.Collapsed;
    }
    public void RestoreViewport()
    {
        Editor.Focus();
        Editor.ScrollToHorizontalOffset(horizontalOffset);
        Editor.ScrollToVerticalOffset(verticalOffset);
    }
    public void Dispose() => Adapter.Dispose();
}
