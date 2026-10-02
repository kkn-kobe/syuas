using System.Diagnostics.CodeAnalysis;

namespace Syuas.Core.Models;

public enum TableParseDiagnosticCode
{
    InvalidNewLine,
    UnsupportedAttributes,
    InvalidColumns,
    MissingOpeningDelimiter,
    MissingClosingDelimiter,
    UnexpectedContent,
    UnsupportedSyntax,
    EmptyTable,
    InvalidSpan,
    TableTooLarge,
    OverlappingCells,
    MissingCells,
    HeaderSpan,
    NonCanonicalLayout
}

// Line and Column are one-based, relative to the supplied table source (not the document).
public sealed record TableParseDiagnostic(TableParseDiagnosticCode Code, string Message, int Line, int Column);

public sealed class TableParseResult
{
    private TableParseResult(TableDefinition? definition, string? newLine, bool hasTrailingNewLine,
        IReadOnlyList<TableParseDiagnostic> diagnostics)
    {
        Definition = definition;
        NewLine = newLine;
        HasTrailingNewLine = hasTrailingNewLine;
        Diagnostics = diagnostics;
    }

    [MemberNotNullWhen(true, nameof(Definition), nameof(NewLine))]
    public bool Succeeded => Definition is not null && NewLine is not null;
    public TableDefinition? Definition { get; }
    public string? NewLine { get; }
    public bool HasTrailingNewLine { get; }
    public IReadOnlyList<TableParseDiagnostic> Diagnostics { get; }

    internal static TableParseResult Success(TableDefinition definition, string newLine, bool trailingNewLine)
        => new(definition, newLine, trailingNewLine, Array.Empty<TableParseDiagnostic>());
    internal static TableParseResult Failure(TableParseDiagnosticCode code, string message, int line, int column = 1)
        => new(null, null, false, Array.AsReadOnly(new[] { new TableParseDiagnostic(code, message, line, column) }));
}
