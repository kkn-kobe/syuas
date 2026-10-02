namespace Syuas.Core.Models;

/// <summary>
/// An immutable, structurally valid table state. Draft title/width values are preserved
/// without passing through the AsciiDoc generator or the stricter import validation.
/// </summary>
public sealed class TableSnapshot
{
    private TableSnapshot(TableDefinition table)
    {
        table.Validate();
        ArgumentNullException.ThrowIfNull(table.Title);
        if (table.ColumnWidths.Count != table.ColumnCount || table.ColumnWidths.Any(w => w is null)
            || table.Cells.Any(c => c.Text is null))
            throw new ArgumentException("表の本文・列幅を保存できません。", nameof(table));

        RowCount = table.RowCount;
        ColumnCount = table.ColumnCount;
        Title = table.Title;
        HasHeader = table.HasHeader;
        Cells = Array.AsReadOnly(table.Cells.OrderBy(c => c.Row).ThenBy(c => c.Column)
            .Select(c => new TableCellDefinition(c.Row, c.Column, c.RowSpan, c.ColumnSpan, c.Text)).ToArray());
        ColumnWidths = Array.AsReadOnly(table.ColumnWidths.ToArray());
        // Conservative accounting for records, references and UTF-16 strings. Shared strings
        // are intentionally counted again; this is a budget estimate, not measured heap usage.
        EstimatedBytes = 128L + StringBytes(Title)
            + Cells.Sum(c => 64L + StringBytes(c.Text))
            + ColumnWidths.Sum(w => 8L + StringBytes(w));
    }

    public int RowCount { get; }
    public int ColumnCount { get; }
    public string Title { get; }
    public bool HasHeader { get; }
    public IReadOnlyList<TableCellDefinition> Cells { get; }
    public IReadOnlyList<string> ColumnWidths { get; }
    public long EstimatedBytes { get; }

    public static TableSnapshot Capture(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new(table);
    }

    public bool ContentEquals(TableSnapshot? other) => other is not null
        && RowCount == other.RowCount && ColumnCount == other.ColumnCount
        && Title == other.Title && HasHeader == other.HasHeader
        && Cells.SequenceEqual(other.Cells) && ColumnWidths.SequenceEqual(other.ColumnWidths);

    internal static long StringBytes(string value) => 24L + 2L * value.Length;
}
