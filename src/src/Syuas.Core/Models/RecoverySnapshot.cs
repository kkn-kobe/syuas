namespace Syuas.Core.Models;

public sealed record RecoverySnapshot(
    Guid DocumentId, long Revision, DateTimeOffset CapturedAt, string Text,
    FileBaseline? Baseline, int SelectionStart, int SelectionLength, int CaretOffset);

public sealed record RecoveryKey(Guid SessionId, Guid DocumentId);

public sealed record RecoveryCandidate(
    RecoveryKey Key, RecoverySnapshot? Snapshot, bool UsedPreviousGeneration = false, string? Error = null)
{
    public FileComparison? OriginalFile { get; init; }
}
