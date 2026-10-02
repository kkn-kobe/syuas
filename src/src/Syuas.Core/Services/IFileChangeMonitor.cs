namespace Syuas.Core.Services;

public sealed record FileChangeSignal(string Path, bool RestartRequired = false, string? Error = null);

public interface IFileChangeMonitor : IDisposable
{
    // Signals may arrive on worker threads. They only request a fresh content comparison.
    event Action<FileChangeSignal>? Changed;
    string? Watch(string? path);
}
