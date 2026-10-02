namespace Syuas.Core.Models;

public enum FileObservationStatus { Present, Missing, Unavailable }

public sealed record FileObservation(
    FileObservationStatus Status, FileBaseline? Baseline = null, string? Error = null);

// Exactly one of Baseline (committed) and Conflict (not written) is populated.
public sealed record FileSaveResult(
    FileBaseline? Baseline = null, FileObservation? Conflict = null, string? BackupPath = null)
{
    public bool Succeeded => Baseline is not null;
}

public enum SaveConflictDecision { Cancel, SaveAs, OverwriteWithBackup, Recreate, Retry }
