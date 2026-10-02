namespace Syuas.Core.Models;

// Immutable state for one editing session. Save As preserves DocumentId; New/Open replace it.
// Revisions increase for the lifetime of the editor, including Undo/Redo and document loads.
// SavedRevision is the revision at the last successful load/save, not an Undo-stack position.
public sealed record DocumentSession(
    Guid DocumentId,
    long Revision,
    long? SavedRevision,
    FileBaseline? Baseline,
    bool IsModified)
{
    public string? FilePath => Baseline?.FullPath;
}
