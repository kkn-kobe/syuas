using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class ExternalChangeServiceTests
{
    [Fact]
    public async Task SignalsAreDebouncedAndDuplicateContentDoesNotNotifyAgain()
    {
        using var f = new Fixture();
        await f.Service.TickAsync();
        f.Files.Current = "external";
        var notifications = 0;
        f.Service.StateChanged += (_, _) => notifications++;
        f.Monitor.Signal(f.Session.FilePath!);
        f.Clock.Advance(400);
        await f.Service.TickAsync();
        Assert.Equal(1, f.Files.Checks);
        f.Monitor.Signal(f.Session.FilePath!);
        f.Clock.Advance(400);
        await f.Service.TickAsync();
        Assert.Equal(1, f.Files.Checks);
        f.Clock.Advance(100);
        await f.Service.TickAsync();
        Assert.Equal(FileComparisonStatus.Modified, f.Service.Current!.Status);
        Assert.True(f.Service.IsNotificationVisible);
        Assert.Equal(1, notifications);
        f.Monitor.Signal(f.Session.FilePath!);
        f.Clock.Advance(500);
        await f.Service.TickAsync();
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task RepeatedSignalsCannotPostponeCheckingIndefinitely()
    {
        using var f = new Fixture();
        await f.Service.TickAsync();
        f.Files.Current = "changed";
        f.Monitor.Signal(f.Session.FilePath!);
        for (var i = 0; i < 5; i++)
        {
            f.Clock.Advance(400);
            f.Monitor.Signal(f.Session.FilePath!);
            await f.Service.TickAsync();
        }
        Assert.Equal(2, f.Files.Checks);
        Assert.True(f.Service.IsNotificationVisible);
    }

    [Fact]
    public async Task PeriodicAndActivationChecksWorkWithoutWatcherEvents()
    {
        using var f = new Fixture();
        await f.Service.TickAsync();
        f.Files.Current = "periodic";
        f.Clock.Advance(10000);
        await f.Service.TickAsync();
        Assert.True(f.Service.IsNotificationVisible);
        f.Files.Current = "activation";
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.Equal(Fingerprint("activation"), f.Service.Current!.Current!.Fingerprint);
    }

    [Fact]
    public async Task DismissalDoesNotAdoptExternalContentAndNewVersionReappears()
    {
        using var f = new Fixture();
        f.Files.Current = "external";
        await f.Service.TickAsync();
        f.Service.Dismiss();
        Assert.False(f.Service.IsNotificationVisible);
        Assert.Equal(FileComparisonStatus.Modified, f.Service.Current!.Status);
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.False(f.Service.IsNotificationVisible);
        f.Service.Reveal();
        Assert.True(f.Service.IsNotificationVisible);
        f.Service.Dismiss();
        f.Files.Current = "new version";
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.True(f.Service.IsNotificationVisible);
        Assert.All(f.Files.Expected, b => Assert.Equal(f.Session.Baseline, b));
    }

    [Fact]
    public async Task ReturningToBaselineClearsWarningAndResetsDismissal()
    {
        using var f = new Fixture();
        f.Files.Current = "external";
        await f.Service.TickAsync();
        f.Service.Dismiss();
        f.Files.Current = "original";
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.False(f.Service.IsNotificationVisible);
        f.Files.Current = "external";
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.True(f.Service.IsNotificationVisible);
    }

    [Fact]
    public async Task MissingAndUnavailableAreReportedWithoutTouchingBaseline()
    {
        using var f = new Fixture();
        f.Files.Status = FileComparisonStatus.Missing;
        await f.Service.TickAsync();
        Assert.Equal(FileComparisonStatus.Missing, f.Service.Current!.Status);
        Assert.Equal(3, f.Files.Checks);
        f.Files.Status = FileComparisonStatus.Unavailable;
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.Equal(FileComparisonStatus.Unavailable, f.Service.Current!.Status);
        Assert.Equal("locked", f.Service.Current.Error);
    }

    [Fact]
    public async Task TransientReadFailureIsRetriedBeforeShowingWarning()
    {
        using var f = new Fixture();
        f.Files.BeforeCompare = () => { f.Files.Status = f.Files.Checks < 3 ? FileComparisonStatus.Unavailable : null; };
        await f.Service.TickAsync();
        Assert.Equal(3, f.Files.Checks);
        Assert.Equal(FileComparisonStatus.Unchanged, f.Service.Current!.Status);
        Assert.False(f.Service.IsNotificationVisible);
    }

    [Fact]
    public async Task MonitorFailureRechecksAndRetriesWatchingWhilePollingContinues()
    {
        using var f = new Fixture();
        await f.Service.TickAsync();
        f.Monitor.Error = "parent missing";
        f.Monitor.Signal(f.Session.FilePath!, true);
        await f.Service.TickAsync();
        Assert.Contains("parent missing", f.Service.MonitoringStatus);
        Assert.Equal(2, f.Monitor.Watches.Count);
        f.Monitor.Error = null;
        f.Files.Current = "changed while watcher unavailable";
        f.Clock.Advance(10000);
        await f.Service.TickAsync();
        Assert.Equal(3, f.Monitor.Watches.Count);
        Assert.Empty(f.Service.MonitoringStatus);
        Assert.True(f.Service.IsNotificationVisible);
    }

    [Fact]
    public async Task SaveInvalidatesInFlightResultEvenWhenSavedContentIsIdentical() 
    {
        using var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Files.Current = "external";
        f.Files.BeforeCompare = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
        var pending = f.Service.TickAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        // Save kept the same baseline bytes but must discard the older pending observation.
        f.Service.Track(f.Session, force: true);
        release.Set();
        await pending;
        Assert.Null(f.Service.Current);
        f.Files.Current = "original";
        f.Files.BeforeCompare = null;
        await f.Service.TickAsync();
        Assert.False(f.Service.IsNotificationVisible);
    }

    [Fact]
    public async Task DocumentSwitchDropsOldResultsAndOldPathSignals()
    {
        using var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Files.Current = "external";
        f.Files.BeforeCompare = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
        var pending = f.Service.TickAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var next = f.Session with { DocumentId = Guid.NewGuid(), Baseline = f.Session.Baseline! with { FullPath = Path.GetFullPath("next.adoc") } };
        f.Service.Track(next);
        release.Set();
        await pending;
        Assert.Null(f.Service.Current);
        f.Files.BeforeCompare = null;
        f.Files.Current = "original";
        await f.Service.TickAsync();
        var checks = f.Files.Checks;
        f.Monitor.Signal(f.Session.FilePath!);
        f.Clock.Advance(1000);
        await f.Service.TickAsync();
        Assert.Equal(checks, f.Files.Checks);
        Assert.Equal(next.FilePath, f.Monitor.Watches.Last());
    }

    [Fact]
    public async Task UntitledDocumentsAndDisposedServicesDoNotCompare()
    {
        using var f = new Fixture();
        f.Service.Track(f.Session with { DocumentId = Guid.NewGuid(), Baseline = null });
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.Equal(0, f.Files.Checks);
        Assert.Null(f.Monitor.Watches.Last());
        f.Service.Dispose();
        f.Service.Track(f.Session);
        f.Service.RequestCheck();
        await f.Service.TickAsync();
        Assert.Equal(0, f.Files.Checks);
        Assert.True(f.Monitor.Disposed);
    }

    [Fact]
    public async Task DisposalDropsPendingResultWithoutNotifyingUI()
    {
        using var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Files.BeforeCompare = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
        var pending = f.Service.TickAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var notifications = 0;
        f.Service.StateChanged += (_, _) => notifications++;
        f.Service.Dispose();
        release.Set();
        await pending;
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageExceptionIsReportedAndLaterCheckRecovers(bool denied)
    {
        using var f = new Fixture();
        f.Files.BeforeCompare = () => throw (denied ? new UnauthorizedAccessException("denied") : new IOException("offline"));
        await f.Service.TickAsync();
        Assert.Equal(3, f.Files.Checks);
        Assert.Equal(FileComparisonStatus.Unavailable, f.Service.Current!.Status);
        Assert.True(f.Service.IsNotificationVisible);
        f.Files.BeforeCompare = null;
        f.Clock.Advance(10000);
        await f.Service.TickAsync();
        Assert.Equal(FileComparisonStatus.Unchanged, f.Service.Current!.Status);
        Assert.False(f.Service.IsNotificationVisible);
    }

    internal static FileFingerprint Fingerprint(string text) => FileFingerprint.FromBytes(System.Text.Encoding.UTF8.GetBytes(text));
    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int milliseconds) => now += TimeSpan.FromMilliseconds(milliseconds);
    }
    internal sealed class FakeMonitor : IFileChangeMonitor
    {
        public event Action<FileChangeSignal>? Changed;
        public List<string?> Watches { get; } = [];
        public string? Error { get; set; }
        public bool Disposed { get; private set; }
        public string? Watch(string? path) { Watches.Add(path); return Error; }
        public void Signal(string path, bool error = false) => Changed?.Invoke(new(path, error, error ? "watcher overflow" : null));
        public void Dispose() => Disposed = true;
    }
    private sealed class Fixture : IDisposable
    {
        public FakeMonitor Monitor { get; } = new();
        public Files Files { get; } = new();
        public ManualClock Clock { get; } = new();
        public DocumentSession Session { get; } = new(Guid.NewGuid(), 0, 0, new(Path.GetFullPath("main.adoc"), Fingerprint("original")), false);
        public ExternalChangeService Service { get; }
        public Fixture() { Service = new(Monitor, Files, Clock, TimeSpan.Zero); Service.Track(Session); }
        public void Dispose() => Service.Dispose();
    }
    private sealed class Files : IFileService
    {
        public int Checks { get; private set; }
        public string Current { get; set; } = "original";
        public FileComparisonStatus? Status { get; set; }
        public Action? BeforeCompare { get; set; }
        public List<FileBaseline> Expected { get; } = [];
        public FileComparison Compare(FileBaseline baseline)
        {
            Checks++;
            Expected.Add(baseline);
            BeforeCompare?.Invoke();
            if (Status is { } status) return new(status, Error: status == FileComparisonStatus.Unavailable ? "locked" : null);
            var current = baseline with { Fingerprint = Fingerprint(Current) };
            return new(current.Fingerprint == baseline.Fingerprint ? FileComparisonStatus.Unchanged : FileComparisonStatus.Modified, current);
        }
        public FileSnapshot ReadSnapshot(string path) => throw new NotSupportedException();
        public FileBaseline WriteSnapshot(string path, string text) => throw new NotSupportedException();
        public FileObservation Observe(string path) => throw new NotSupportedException();
        public FileSaveResult WriteChecked(string path, string text, FileBaseline? expected, bool preserveBackup = false) => throw new NotSupportedException();
    }
}
