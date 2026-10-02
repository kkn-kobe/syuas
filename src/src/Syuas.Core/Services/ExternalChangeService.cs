using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Public methods run on the UI thread. Watcher callbacks only update scheduling under gate.
public sealed class ExternalChangeService : IDisposable
{
    private readonly IFileChangeMonitor monitor;
    private readonly IFileService files;
    private readonly TimeProvider clock;
    private readonly TimeSpan retryDelay;
    private readonly object gate = new();
    private DocumentSession? tracked;
    private long generation;
    private bool pending, immediate, restart, checking, disposed;
    private DateTimeOffset lastSignal, firstSignal, lastCheck, lastRestart;
    private string? monitorError;
    private FileComparison? dismissed;

    public ExternalChangeService(IFileChangeMonitor monitor, IFileService files, TimeProvider? clock = null, TimeSpan? retryDelay = null)
    {
        this.monitor = monitor;
        this.files = files;
        this.clock = clock ?? TimeProvider.System;
        this.retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(150);
        monitor.Changed += OnSignal;
    }

    public FileComparison? Current { get; private set; }
    public bool IsNotificationVisible => Current is { Status: not FileComparisonStatus.Unchanged } && Current != dismissed;
    public string MonitoringStatus { get; private set; } = "";
    public event EventHandler? StateChanged;

    public void Track(DocumentSession session, bool force = false)
    {
        string? oldPath;
        lock (gate)
        {
            if (disposed) return;
            if (!force && tracked?.DocumentId == session.DocumentId && tracked.Baseline == session.Baseline && tracked.SavedRevision == session.SavedRevision) return;
            oldPath = tracked?.FilePath;
            tracked = session;
            generation++;
            pending = immediate = session.Baseline is not null;
            firstSignal = lastSignal = clock.GetUtcNow();
            dismissed = null;
            Current = null;
        }
        if (!string.Equals(oldPath, session.FilePath, StringComparison.OrdinalIgnoreCase)) RestartWatcher(session.FilePath);
        if (session.FilePath is null) MonitoringStatus = "";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCheck()
    {
        lock (gate)
        {
            if (disposed || tracked?.Baseline is null) return;
            pending = immediate = true;
        }
    }

    private void OnSignal(FileChangeSignal signal)
    {
        lock (gate)
        {
            if (disposed || !string.Equals(tracked?.FilePath, signal.Path, StringComparison.OrdinalIgnoreCase)) return;
            var now = clock.GetUtcNow();
            if (!pending) firstSignal = now;
            pending = true;
            lastSignal = now;
            if (signal.RestartRequired)
            {
                restart = immediate = true;
                monitorError = signal.Error;
            }
        }
    }

    public void Dismiss()
    {
        dismissed = Current;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reveal()
    {
        dismissed = null;
        RequestCheck();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task TickAsync()
    {
        DocumentSession session;
        long ticket;
        bool shouldRestart;
        lock (gate)
        {
            if (disposed || checking || tracked?.Baseline is null) return;
            var now = clock.GetUtcNow();
            var periodic = now - lastCheck >= TimeSpan.FromSeconds(10);
            var debounced = pending && (now - lastSignal >= TimeSpan.FromMilliseconds(500) || now - firstSignal >= TimeSpan.FromSeconds(2));
            if (!immediate && !periodic && !debounced) return;
            session = tracked;
            ticket = generation;
            shouldRestart = restart || ((monitorError is not null || Current?.Status == FileComparisonStatus.Missing)
                && now - lastRestart >= TimeSpan.FromSeconds(10));
            restart = pending = immediate = false;
            checking = true;
            lastCheck = now;
        }
        try
        {
            if (shouldRestart) RestartWatcher(session.FilePath);
            var result = await Task.Run(async () =>
            {
                FileComparison comparison = files.Compare(session.Baseline!);
                for (var attempt = 0; attempt < 2 && comparison.Status is FileComparisonStatus.Unavailable or FileComparisonStatus.Missing; attempt++)
                {
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                    comparison = files.Compare(session.Baseline!);
                }
                return comparison;
            });
            lock (gate)
            {
                // Saves, reloads, path switches and disposal invalidate results already in flight.
                if (disposed || generation != ticket) return;
                if (result.Status == FileComparisonStatus.Unchanged) dismissed = null;
                if (Current == result) return;
                Current = result;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { lock (gate) checking = false; }
    }

    private void RestartWatcher(string? path)
    {
        var error = monitor.Watch(path);
        lock (gate) { monitorError = error; lastRestart = clock.GetUtcNow(); }
        MonitoringStatus = error is null ? "" : $"変更監視を再試行しています（10秒ごとの照合は継続）: {error}";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; generation++; }
        monitor.Changed -= OnSignal;
        monitor.Dispose();
    }
}
