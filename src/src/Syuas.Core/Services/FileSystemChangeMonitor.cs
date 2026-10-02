namespace Syuas.Core.Services;

public sealed class FileSystemChangeMonitor : IFileChangeMonitor
{
    private volatile FileSystemWatcher? watcher;
    private volatile string? target;
    public event Action<FileChangeSignal>? Changed;

    public string? Watch(string? path)
    {
        Stop();
        target = path;
        if (path is null) return null;
        try
        {
            var next = new FileSystemWatcher(Path.GetDirectoryName(path)!)
            {
                Filter = "*", IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };
            watcher = next;
            next.Changed += OnChanged;
            next.Created += OnChanged;
            next.Deleted += OnChanged;
            next.Renamed += OnRenamed;
            next.Error += OnError;
            next.EnableRaisingEvents = true;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Stop();
            return e.Message;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (ReferenceEquals(sender, watcher) && target is { } path && SamePath(path, e.FullPath))
            Changed?.Invoke(new(path));
    }
    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (ReferenceEquals(sender, watcher) && target is { } path && (SamePath(path, e.FullPath) || SamePath(path, e.OldFullPath)))
            Changed?.Invoke(new(path));
    }
    private void OnError(object sender, ErrorEventArgs e)
    {
        if (ReferenceEquals(sender, watcher) && target is { } path)
            Changed?.Invoke(new(path, true, e.GetException().Message));
    }
    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private void Stop()
    {
        var old = watcher;
        watcher = null;
        if (old is null) return;
        old.Changed -= OnChanged; old.Created -= OnChanged; old.Deleted -= OnChanged;
        old.Renamed -= OnRenamed; old.Error -= OnError;
        old.Dispose();
    }
    public void Dispose() { target = null; Stop(); }
}
