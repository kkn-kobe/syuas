namespace Syuas.Core.Models;

public enum TableInputKind { Cell, Title, ColumnWidth, Rows, Columns }

public sealed record TableInputTarget(TableInputKind Kind, int Row = 0, int Column = 0);

// Control-independent focus information. Offsets are clamped by the View after restoration.
public sealed record TableInputFocus(TableInputTarget Target, int SelectionStart, int SelectionLength);

public sealed class TableFocusEventArgs(TableInputFocus? focus) : EventArgs
{
    public TableInputFocus? Focus { get; } = focus;
}
