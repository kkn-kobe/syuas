namespace Syuas.Core.Models;

public enum PreviewDataMode { Keep, DeleteOnExit, Disabled }

public sealed record AppSettings(bool RecoveryEnabled = true, PreviewDataMode PreviewData = PreviewDataMode.Keep)
{
    public static AppSettings Restricted { get; } = new(false, PreviewDataMode.Disabled);
}
