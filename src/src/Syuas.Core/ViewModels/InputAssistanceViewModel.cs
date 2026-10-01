using Syuas.Core.Editor;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Core.ViewModels;

public interface IInputAssistanceDialogs
{
    InsertionSnippet? Show(AssistanceKind kind, InsertionContext context);
}

public sealed class InputAssistanceViewModel
{
    private readonly IEditorAdapter editor;
    private readonly Func<string?> documentPath;
    private readonly IInputAssistanceDialogs dialogs;
    private readonly EditorInsertionService insertion;
    public InputAssistanceViewModel(IEditorAdapter editor, Func<string?> documentPath, IInputAssistanceDialogs dialogs)
    {
        this.editor = editor; this.documentPath = documentPath; this.dialogs = dialogs;
        insertion = new(editor);
        InsertCommand = new(parameter =>
        {
            if (Enum.TryParse<AssistanceKind>(parameter?.ToString(), out var kind) && Enum.IsDefined(kind)) Insert(kind);
        });
    }
    public RelayCommand InsertCommand { get; }
    public event EventHandler? FocusRequested;

    public void Insert(AssistanceKind kind)
    {
        var context = insertion.Capture(kind, documentPath());
        InsertionSnippet? snippet;
        if (kind is AssistanceKind.Bold or AssistanceKind.Italic or AssistanceKind.Monospace)
        {
            var format = kind switch { AssistanceKind.Bold => InlineFormat.Bold, AssistanceKind.Italic => InlineFormat.Italic, _ => InlineFormat.Monospace };
            var end = context.Start + context.Length;
            var unconstrained = context.Start > 0 && IsWord(editor.Text[context.Start - 1]) || end < editor.Text.Length && IsWord(editor.Text[end]);
            var leading = context.Text.Length - context.Text.TrimStart().Length;
            var body = context.Text.Trim();
            var trailing = context.Text.Length - leading - body.Length;
            var marker = AsciiDocTextGenerator.Marker(format, unconstrained);
            var text = context.Text[..leading] + AsciiDocTextGenerator.Format(body, format, unconstrained) + context.Text[(context.Text.Length - trailing)..];
            snippet = new(text, leading + marker.Length + body.Length);
        }
        else if (kind is AssistanceKind.UnorderedList or AssistanceKind.OrderedList or AssistanceKind.Checklist)
        {
            var listKind = kind switch { AssistanceKind.OrderedList => ListKind.Ordered, AssistanceKind.Checklist => ListKind.Checklist, _ => ListKind.Unordered };
            var text = AsciiDocTextGenerator.List(context.Text, listKind, context.NewLine);
            snippet = new(text, text.Length, true);
        }
        else if (kind is AssistanceKind.ListingBlock or AssistanceKind.LiteralBlock or AssistanceKind.QuoteBlock or AssistanceKind.ExampleBlock)
        {
            var blockKind = kind switch { AssistanceKind.LiteralBlock => BlockKind.Literal, AssistanceKind.QuoteBlock => BlockKind.Quote, AssistanceKind.ExampleBlock => BlockKind.Example, _ => BlockKind.Listing };
            var text = AsciiDocBlockGenerator.Generate(blockKind, context.Text, context.NewLine);
            snippet = new(text, InputFormViewModel.ContentStart(text, context.NewLine, false), true);
        }
        else snippet = dialogs.Show(kind, context);
        if (snippet is not null) insertion.Apply(context, snippet);
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsWord(char character) => char.IsLetterOrDigit(character) || character == '_';
}
