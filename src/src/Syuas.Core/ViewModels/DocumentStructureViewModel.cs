using Syuas.Core.Editor;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public sealed class DocumentStructureViewModel : ObservableObject
{
    private readonly IEditorAdapter editor;
    private string? lastSource;
    private bool isVisible = true;
    public DocumentStructureViewModel(IEditorAdapter editor)
    {
        this.editor = editor;
        NavigateCommand = new(value =>
        {
            if (value is not OutlineEntry entry) return;
            editor.Select(Math.Clamp(entry.Offset, 0, editor.Text.Length), 0);
            FocusRequested?.Invoke(this, EventArgs.Empty);
        });
        Refresh();
    }
    public event EventHandler? FocusRequested;
    public IReadOnlyList<OutlineEntry> Roots { get; private set; } = [];
    public string Summary { get; private set; } = "見出しはありません";
    public bool IsVisible { get => isVisible; set { isVisible = value; Changed(); } }
    public RelayCommand NavigateCommand { get; }
    public void Refresh()
    {
        var source = editor.Text;
        if (source == lastSource) return;
        lastSource = source;
        var structure = DocumentStructureParser.Parse(source);
        Roots = structure.Roots;
        Summary = structure.Headings.Count == 0 ? "見出しはありません" : $"{structure.Headings.Count} 件の見出し";
        Changed(nameof(Roots)); Changed(nameof(Summary));
    }
}
