using System.Diagnostics.CodeAnalysis;

namespace Syuas.Core.Models;

// Offsets are UTF-16 offsets into the editor document. The closing line break is excluded.
public sealed record TableSourceRange(int StartOffset, int Length, int StartLine, string OriginalSource, string NewLine);

public enum TableEditDiagnosticCode
{
    InvalidSelection, NotInTable, SelectionCrossesBoundary, UnsupportedContext,
    MissingClosingDelimiter, AmbiguousBoundary, InvalidTable, DocumentChanged, ForeignContext
}

// Positions are one-based document positions. ParseCode preserves a parser-specific diagnosis.
public sealed record TableEditDiagnostic(TableEditDiagnosticCode Code, string Message, int Line = 1, int Column = 1,
    TableParseDiagnosticCode? ParseCode = null)
{
    public string DisplayMessage => Code is TableEditDiagnosticCode.DocumentChanged or TableEditDiagnosticCode.ForeignContext
        ? Message : $"{Message}\n場所: {Line}行 {Column}列";
}

public sealed record TableLocationResult(TableSourceRange? Range, TableEditDiagnostic? Diagnostic)
{
    [MemberNotNullWhen(true, nameof(Range))]
    public bool Succeeded => Range is not null && Diagnostic is null;
}

public sealed class TableEditContext
{
    internal TableEditContext(object owner, Guid documentId, long revision, TableSourceRange range, TableDefinition definition)
    { Owner = owner; DocumentId = documentId; Revision = revision; Range = range; Definition = definition; }
    internal object Owner { get; }
    public Guid DocumentId { get; }
    public long Revision { get; }
    public TableSourceRange Range { get; }
    // This detached working model can be discarded to cancel without touching the editor.
    public TableDefinition Definition { get; }
}

public sealed record TableEditStartResult(TableEditContext? Context, TableEditDiagnostic? Diagnostic)
{
    [MemberNotNullWhen(true, nameof(Context))]
    public bool Succeeded => Context is not null && Diagnostic is null;
}

public enum TableEditApplyStatus { Applied, Unchanged, Rejected }
public sealed record TableEditApplyResult(TableEditApplyStatus Status, TableEditDiagnostic? Diagnostic = null)
{
    public bool Succeeded => Status is TableEditApplyStatus.Applied or TableEditApplyStatus.Unchanged;
}
