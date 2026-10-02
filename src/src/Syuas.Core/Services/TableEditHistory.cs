using Syuas.Core.Models;

namespace Syuas.Core.Services;

/// <summary>
/// In-memory history for one designer/table instance; call from its owning thread.
/// The caller groups text edits, captures selection/draft fields, and refreshes the View
/// after restoration. This class never updates an editor document or serializes history.
/// </summary>
public sealed class TableEditHistory
{
    public const int DefaultMaxEntries = 100;
    public const long DefaultMaxEstimatedBytes = 32L * 1024 * 1024;
    private sealed record Entry(string Description, TableDesignerSnapshot Before, TableDesignerSnapshot After)
    {
        public long EstimatedBytes => 64L + TableSnapshot.StringBytes(Description) + Before.EstimatedBytes + After.EstimatedBytes;
    }

    private readonly TableDefinition table;
    private readonly List<Entry> entries = [];
    private int position;
    private bool executing;

    public TableEditHistory(TableDefinition table, int maxEntries = DefaultMaxEntries,
        long maxEstimatedBytes = DefaultMaxEstimatedBytes)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxEstimatedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxEstimatedBytes));
        this.table = table;
        MaxEntries = maxEntries;
        MaxEstimatedBytes = maxEstimatedBytes;
    }

    public int MaxEntries { get; }
    public long MaxEstimatedBytes { get; }
    public long EstimatedBytes { get; private set; }
    public int UndoCount => position;
    public int RedoCount => entries.Count - position;
    public bool CanUndo => !executing && UndoCount > 0;
    public bool CanRedo => !executing && RedoCount > 0;
    public string? UndoDescription => position > 0 ? entries[position - 1].Description : null;
    public string? RedoDescription => position < entries.Count ? entries[position].Description : null;

    /// <summary>
    /// Runs one operation and rolls table data back on failure. The caller must also restore
    /// its selection/draft fields from before and rebind cell references after an exception.
    /// </summary>
    public bool Execute(string description, TableDesignerSnapshot before, Func<TableDesignerSnapshot> change)
    {
        EnsureIdle();
        ValidateBefore(description, before);
        ArgumentNullException.ThrowIfNull(change);
        EnsureTableMatches(before.Table);
        executing = true;
        try
        {
            return RecordCore(description, before, change());
        }
        catch
        {
            table.Restore(before.Table);
            throw;
        }
        finally { executing = false; }
    }

    /// <summary>
    /// Records an already completed edit (e.g. grouped text input). The live table must match
    /// after. A rejected record leaves history unchanged; use Execute for automatic rollback.
    /// </summary>
    public bool Record(string description, TableDesignerSnapshot before, TableDesignerSnapshot after)
    {
        EnsureIdle();
        ValidateBefore(description, before);
        return RecordCore(description, before, after);
    }

    public TableDesignerSnapshot? Undo()
    {
        EnsureIdle();
        if (!CanUndo) return null;
        var entry = entries[position - 1];
        EnsureTableMatches(entry.After.Table);
        table.Restore(entry.Before.Table);
        position--;
        return entry.Before;
    }

    public TableDesignerSnapshot? Redo()
    {
        EnsureIdle();
        if (!CanRedo) return null;
        var entry = entries[position];
        EnsureTableMatches(entry.Before.Table);
        table.Restore(entry.After.Table);
        position++;
        return entry.After;
    }

    public void Clear()
    {
        EnsureIdle();
        entries.Clear();
        position = 0;
        EstimatedBytes = 0;
    }

    private void ValidateBefore(string description, TableDesignerSnapshot before)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(before);
        var current = position > 0 ? entries[position - 1].After : entries.FirstOrDefault()?.Before;
        if (current is not null && !current.ContentEquals(before))
            throw new InvalidOperationException("履歴に記録されていない変更があります。先に入力中の編集を確定してください。");
    }

    private bool RecordCore(string description, TableDesignerSnapshot before, TableDesignerSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(after);
        EnsureTableMatches(after.Table);
        if (before.ContentEquals(after)) return false;
        var entry = new Entry(description, before, after);
        entries.EnsureCapacity(position + 1);
        for (var index = position; index < entries.Count; index++) EstimatedBytes -= entries[index].EstimatedBytes;
        entries.RemoveRange(position, entries.Count - position);
        entries.Add(entry);
        EstimatedBytes += entry.EstimatedBytes;
        position++;
        // Always retain the latest operation, even if that single entry exceeds the budget.
        // The budget includes both Undo and Redo entries and deliberately overcounts sharing.
        while (entries.Count > 1 && (entries.Count > MaxEntries || EstimatedBytes > MaxEstimatedBytes))
        {
            EstimatedBytes -= entries[0].EstimatedBytes;
            entries.RemoveAt(0);
            position--;
        }
        return true;
    }

    private void EnsureTableMatches(TableSnapshot expected)
    {
        if (!expected.ContentEquals(TableSnapshot.Capture(table)))
            throw new InvalidOperationException("表の内容が履歴と一致しません。先に入力中の編集を確定してください。");
    }

    private void EnsureIdle()
    {
        if (executing) throw new InvalidOperationException("履歴操作の実行中に別の履歴操作は開始できません。");
    }
}
