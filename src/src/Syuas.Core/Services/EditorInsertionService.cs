using Syuas.Core.Editor;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public sealed class EditorInsertionService(IEditorAdapter editor)
{
    public InsertionContext Capture(AssistanceKind kind, string? documentPath)
    {
        var text = editor.Text;
        var start = editor.SelectionStart;
        var end = start + editor.SelectionLength;
        if (kind is AssistanceKind.Heading or AssistanceKind.UnorderedList or AssistanceKind.OrderedList or AssistanceKind.Checklist)
        {
            if (end > start && text[end - 1] == '\n') end--;
            if (end > start && text[end - 1] == '\r') end--;
            start = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
            var next = text.IndexOf('\n', end);
            end = next < 0 ? text.Length : next;
            if (end > start && text[end - 1] == '\r') end--;
        }
        var firstLf = text.IndexOf('\n');
        var newLine = firstLf < 0 ? Environment.NewLine : firstLf > 0 && text[firstLf - 1] == '\r' ? "\r\n" : "\n";
        return new(start, end - start, text[start..end], newLine, documentPath);
    }

    public void Apply(InsertionContext context, InsertionSnippet snippet)
    {
        var prefix = "";
        var suffix = "";
        if (snippet.IsBlock)
        {
            var before = editor.Text[..context.Start];
            var after = editor.Text[(context.Start + context.Length)..];
            prefix = Separation(before, context.NewLine, true);
            suffix = Separation(after, context.NewLine, false);
        }
        using (editor.BeginUpdate()) editor.Replace(context.Start, context.Length, prefix + snippet.Text + suffix);
        editor.Select(context.Start + prefix.Length + snippet.CaretOffset, 0);
    }

    private static string Separation(string text, string newLine, bool before)
    {
        if (text.Length == 0) return "";
        var normalized = text.Replace("\r\n", "\n");
        if (before ? normalized.EndsWith("\n\n") : normalized.StartsWith("\n\n")) return "";
        return (before ? normalized.EndsWith('\n') : normalized.StartsWith('\n')) ? newLine : newLine + newLine;
    }
}
