using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class RecoveryServiceTests
{
    [Fact]
    public async Task IdleDebounceCapturesOnlyDirtyContentAndIgnoresSelectionNotifications()
    {
        var clock = new ManualClock();
        using var store = new MemoryStore();
        using var service = new RecoveryService(store, clock);
        var session = Session();
        service.Track(session);
        clock.Advance(4);
        await service.TickAsync(() => Capture(session, clock));
        Assert.Empty(store.Writes);
        service.Track(session); // Caret/selection did not change the content revision.
        clock.Advance(1);
        await service.TickAsync(() => Capture(session, clock));
        Assert.Single(store.Writes);
        clock.Advance(90);
        await service.TickAsync(() => throw new Exception("Unchanged content must not be captured again"));
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task ContinuousTypingIsCapturedAfterThirtySeconds()
    {
        var clock = new ManualClock();
        using var store = new MemoryStore();
        using var service = new RecoveryService(store, clock);
        var session = Session();
        service.Track(session);
        for (var i = 1; i <= 30; i++)
        {
            clock.Advance(1);
            session = session with { Revision = i + 1 };
            service.Track(session);
            await service.TickAsync(() => Capture(session, clock));
            Assert.Equal(i == 30 ? 1 : 0, store.Writes.Count);
        }
        Assert.Equal(31, Assert.Single(store.Writes).Revision);
    }

    [Fact]
    public async Task FailedWriteKeepsPreviousDataAndRetriesWithoutFurtherTyping()
    {
        var clock = new ManualClock();
        using var store = new MemoryStore();
        using var service = new RecoveryService(store, clock);
        var session = Session();
        service.Track(session);
        clock.Advance(5);
        await service.TickAsync(() => Capture(session, clock));
        session = session with { Revision = 2 };
        service.Track(session);
        store.FailWrite = true;
        clock.Advance(5);
        await service.TickAsync(() => Capture(session, clock));
        Assert.Contains("更新できません", service.Status);
        Assert.Equal(1, Assert.Single(store.Writes).Revision);
        store.FailWrite = false;
        clock.Advance(5);
        await service.TickAsync(() => Capture(session, clock));
        Assert.Equal(2, store.Writes.Count);
    }

    [Fact]
    public async Task SaveRetirementRunsAfterAnInFlightWrite()
    {
        var clock = new ManualClock();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var store = new MemoryStore { BeforeWrite = () => { started.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); } };
        using var service = new RecoveryService(store, clock);
        var session = Session();
        service.Track(session);
        clock.Advance(5);
        var pending = service.TickAsync(() => Capture(session, clock));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        service.Track(session with { IsModified = false, SavedRevision = session.Revision });
        release.Set();
        await pending;
        await service.DrainAsync();
        Assert.Equal(new[] { "write", "retire" }, store.Operations);
        Assert.Empty(store.Active);
    }

    [Fact]
    public async Task DocumentSwitchCannotBeOverwrittenByAnOldWriteCompletion()
    {
        var clock = new ManualClock();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var store = new MemoryStore { BeforeWrite = () => { started.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); } };
        using var service = new RecoveryService(store, clock);
        var old = Session();
        service.Track(old);
        clock.Advance(5);
        var pending = service.TickAsync(() => Capture(old, clock));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        var next = Session();
        service.Track(next);
        release.Set();
        await pending;
        clock.Advance(5);
        await service.TickAsync(() => Capture(next, clock));
        Assert.Single(store.Active);
        Assert.True(store.Active.ContainsKey(next.DocumentId));
    }

    [Fact]
    public async Task UndoToCleanAndConfirmedCloseRetireOnlyCurrentDocument()
    {
        var clock = new ManualClock();
        using var store = new MemoryStore();
        using var service = new RecoveryService(store, clock);
        var session = Session();
        service.Track(session);
        clock.Advance(5);
        await service.TickAsync(() => Capture(session, clock));
        service.Track(session with { Revision = 2, IsModified = false });
        await service.DrainAsync();
        Assert.Empty(store.Active);
        service.Track(session with { Revision = 3 });
        clock.Advance(5);
        await service.TickAsync(() => Capture(session with { Revision = 3 }, clock));
        await service.CloseAsync();
        Assert.Empty(store.Active);
        Assert.True(store.Disposed);
    }

    [Fact]
    public async Task DisposeWithoutConfirmedClosePreservesDraft()
    {
        var clock = new ManualClock();
        var store = new MemoryStore();
        var service = new RecoveryService(store, clock);
        var session = Session();
        service.Track(session);
        clock.Advance(5);
        await service.TickAsync(() => Capture(session, clock));
        service.Dispose();
        Assert.Single(store.Active);
    }

    private static DocumentSession Session() => new(Guid.NewGuid(), 1, null, null, true);
    private static RecoverySnapshot Capture(DocumentSession session, TimeProvider clock)
        => new(session.DocumentId, session.Revision, clock.GetUtcNow(), "draft", session.Baseline, 0, 0, 0);

    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now += TimeSpan.FromSeconds(seconds);
    }

    private sealed class MemoryStore : IRecoveryStore
    {
        public List<RecoverySnapshot> Writes { get; } = [];
        public Dictionary<Guid, RecoverySnapshot> Active { get; } = [];
        public List<string> Operations { get; } = [];
        public bool FailWrite { get; set; }
        public bool Disposed { get; private set; }
        public Action? BeforeWrite { get; init; }
        public void Write(RecoverySnapshot snapshot)
        {
            BeforeWrite?.Invoke();
            if (FailWrite) throw new IOException("Disk full");
            Writes.Add(snapshot); Active[snapshot.DocumentId] = snapshot; Operations.Add("write");
        }
        public void Retire(Guid documentId) { Active.Remove(documentId); Operations.Add("retire"); }
        public IReadOnlyList<RecoveryCandidate> ListCandidates() => [];
        public RecoverySnapshot Claim(RecoveryKey key) => throw new NotSupportedException();
        public void Discard(RecoveryKey key) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
}
