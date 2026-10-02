using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class FileSystemChangeMonitorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
    public FileSystemChangeMonitorTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("change")]
    [InlineData("delete")]
    [InlineData("rename-away")]
    [InlineData("rename-into")]
    [InlineData("replace")]
    [InlineData("create")]
    public async Task RealWatcherDetectsEditorSaveAndFileLifecycleOperations(string operation)
    {
        var path = Path.Combine(directory, "main.adoc");
        var other = Path.Combine(directory, "other.adoc");
        if (operation is not "create" and not "rename-into") File.WriteAllText(path, "original");
        File.WriteAllText(other, "replacement");
        using var monitor = new FileSystemChangeMonitor();
        var changed = new TaskCompletionSource<FileChangeSignal>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += s => changed.TrySetResult(s);
        Assert.Null(monitor.Watch(path));
        switch (operation)
        {
            case "change": File.WriteAllText(path, "changed"); break;
            case "delete": File.Delete(path); break;
            case "rename-away": File.Move(path, Path.Combine(directory, "renamed.adoc")); break;
            case "rename-into": File.Move(other, path); break;
            case "replace": File.Replace(other, path, null); break;
            case "create": File.WriteAllText(path, "created"); break;
        }
        var signal = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(path, signal.Path);
        Assert.False(signal.RestartRequired);
    }

    [Fact]
    public async Task UnrelatedFilesDoNotTriggerTargetNotification()
    {
        using var monitor = new FileSystemChangeMonitor();
        var path = Path.Combine(directory, "main.adoc");
        var signals = 0;
        monitor.Changed += _ => Interlocked.Increment(ref signals);
        Assert.Null(monitor.Watch(path));
        File.WriteAllText(Path.Combine(directory, "other.adoc"), "unrelated");
        await Task.Delay(150);
        Assert.Equal(0, signals);
    }

    [Fact]
    public void MissingParentCanBeWatchedAfterItReappears()
    {
        using var monitor = new FileSystemChangeMonitor();
        var parent = Path.Combine(directory, "missing");
        var path = Path.Combine(parent, "main.adoc");
        Assert.NotNull(monitor.Watch(path));
        Directory.CreateDirectory(parent);
        Assert.Null(monitor.Watch(path));
        Assert.Null(monitor.Watch(null));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
