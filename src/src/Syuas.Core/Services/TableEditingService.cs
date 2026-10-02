using Syuas.Core.Editor;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Call on the editor's owning thread. BeginEdit only reads; discard its context to cancel.
public sealed class TableEditingService(IEditorAdapter editor, Func<DocumentSession> getSession)
{
    private readonly object owner = new();

    public TableEditStartResult BeginEdit()
    {
        var session = getSession();
        var revision = editor.ContentRevision;
        if (session.Revision != revision) return new(null, Stale());
        var source = editor.Text;
        var start = editor.SelectionLength == 0 ? editor.CaretOffset : editor.SelectionStart;
        var located = AsciiDocTableLocator.Locate(source, start, editor.SelectionLength);
        if (!located.Succeeded) return new(null, located.Diagnostic);
        var parsed = AsciiDocTableParser.Parse(located.Range.OriginalSource);
        if (!parsed.Succeeded) return new(null, ParserError(parsed.Diagnostics[0], located.Range.StartLine));
        // Capture is synchronous, but reject an inconsistent adapter/session provider as well.
        if (getSession().DocumentId != session.DocumentId || editor.ContentRevision != revision || editor.Text != source)
            return new(null, Stale());
        return new(new(owner, session.DocumentId, revision, located.Range, parsed.Definition), null);
    }

    public TableEditApplyResult Apply(TableEditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(owner, context.Owner))
            return new(TableEditApplyStatus.Rejected, new(TableEditDiagnosticCode.ForeignContext, "この表は別の編集操作で開かれています。表を開き直してください。"));
        var session = getSession();
        var range = context.Range;
        var source = editor.Text;
        if (session.DocumentId != context.DocumentId || session.Revision != context.Revision || editor.ContentRevision != context.Revision ||
            range.StartOffset > source.Length || range.Length > source.Length - range.StartOffset ||
            !source.AsSpan(range.StartOffset, range.Length).SequenceEqual(range.OriginalSource.AsSpan()))
            return new(TableEditApplyStatus.Rejected, Stale());
        string replacement;
        try { replacement = AsciiDocTableGenerator.Generate(context.Definition, range.NewLine); }
        catch (ArgumentException e)
        { return new(TableEditApplyStatus.Rejected, new(TableEditDiagnosticCode.InvalidTable, e.Message, range.StartLine)); }
        if (replacement == range.OriginalSource) return new(TableEditApplyStatus.Unchanged);
        // Ensure edits remain readable by the supported source format before touching the editor.
        var parsed = AsciiDocTableParser.Parse(replacement);
        if (!parsed.Succeeded) return new(TableEditApplyStatus.Rejected, ParserError(parsed.Diagnostics[0], range.StartLine));
        var located = AsciiDocTableLocator.Locate(replacement, 0);
        if (!located.Succeeded || located.Range.StartOffset != 0 || located.Range.Length != replacement.Length)
            return new(TableEditApplyStatus.Rejected, new(TableEditDiagnosticCode.InvalidTable,
                "編集後の表の範囲を確定できません。タイトルやセル末尾の属性行を確認してください。",
                range.StartLine + (located.Diagnostic?.Line ?? 1) - 1, located.Diagnostic?.Column ?? 1));
        using (editor.BeginUpdate()) editor.Replace(range.StartOffset, range.Length, replacement);
        // Place the caret in the first cell rather than at a stale absolute offset.
        var opening = replacement.IndexOf(range.NewLine + "|===" + range.NewLine, StringComparison.Ordinal) + range.NewLine.Length;
        var firstCell = replacement.IndexOf('|', opening + 4 + range.NewLine.Length);
        editor.Select(range.StartOffset + firstCell + 1, 0);
        return new(TableEditApplyStatus.Applied);
    }

    private static TableEditDiagnostic ParserError(TableParseDiagnostic diagnostic, int startLine)
        => new(TableEditDiagnosticCode.InvalidTable, diagnostic.Message, startLine + diagnostic.Line - 1, diagnostic.Column, diagnostic.Code);
    private static TableEditDiagnostic Stale()
        => new(TableEditDiagnosticCode.DocumentChanged, "表を開いた後に文書が変更されています。適用せずに表を開き直してください。");
}
