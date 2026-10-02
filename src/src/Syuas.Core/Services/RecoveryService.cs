using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Track/Tick/Capture are called on the UI thread. Only immutable snapshots cross to the worker.
public sealed class RecoveryService : IDisposable
{
    private readonly IRecoveryStore store;
    private readonly TimeProvider clock;
    private readonly IFileService files;
    private Task tail = Task.CompletedTask;
    private DocumentSession? tracked;
    private DateTimeOffset? firstUnwritten, lastChange, lastAttempt;
    private long? writtenRevision;
    private bool writing, stopped, disposed;
    // Accessed only by the serialized storage queue; retry failed cleanup at confirmed close.
    private readonly HashSet<Guid> pendingRetirements = [];
    private bool closeSucceeded = true;
    private volatile string status = "自動復元: 待機中";
    public string Status => status;
    public RecoveryService(IRecoveryStore store, TimeProvider? clock = null, IFileService? files = null)
    {
        this.store = store;
        this.clock = clock ?? TimeProvider.System;
        this.files = files ?? new Utf8FileService();
    }

    public void Track(DocumentSession session)
    {
        if (stopped) return;
        var old = tracked;
        tracked = session;
        if (old?.DocumentId != session.DocumentId)
        {
            if (old is not null) Retire(old.DocumentId);
            firstUnwritten = lastChange = lastAttempt = null;
            writtenRevision = null;
        }
        if (!session.IsModified)
        {
            if (old?.DocumentId == session.DocumentId && old.IsModified) Retire(session.DocumentId);
            firstUnwritten = lastChange = null;
            writtenRevision = null;
        }
        else if (old?.DocumentId != session.DocumentId || old.Revision != session.Revision || !old.IsModified)
        {
            var now = clock.GetUtcNow();
            firstUnwritten ??= now;
            lastChange = now;
        }
    }

    public async Task TickAsync(Func<RecoverySnapshot> capture)
    {
        if (stopped || writing || tracked is not { IsModified: true } session || writtenRevision == session.Revision) return;
        var now = clock.GetUtcNow();
        if (lastAttempt is { } attempt && now - attempt < TimeSpan.FromSeconds(5)) return;
        if (now - (lastChange ?? now) < TimeSpan.FromSeconds(5) && now - (firstUnwritten ?? now) < TimeSpan.FromSeconds(30)) return;
        var snapshot = capture();
        writing = true;
        lastAttempt = now;
        try
        {
            var success = await Enqueue(() =>
            {
                store.Write(snapshot);
                // New edits supersede an earlier failed cleanup for this document.
                pendingRetirements.Remove(snapshot.DocumentId);
                status = $"復元用コピー: {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
            });
            if (success && tracked?.DocumentId == snapshot.DocumentId)
            {
                writtenRevision = snapshot.Revision;
                if (tracked.Revision == snapshot.Revision) firstUnwritten = null;
                else firstUnwritten = clock.GetUtcNow();
            }
        }
        finally { writing = false; }
    }

    private void Retire(Guid id) => _ = Enqueue(() => RetireCore(id));

    private void RetireCore(Guid id)
    {
        pendingRetirements.Add(id);
        store.Retire(id);
        pendingRetirements.Remove(id);
        status = "自動復元: 待機中";
    }

    // Queue all mutations, including retirement, behind any in-flight write.
    private Task<bool> Enqueue(Action action)
    {
        var previous = tail;
        var next = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            try { action(); return true; }
            catch (Exception e) when (IsStorageError(e))
            {
                status = $"復元用データを更新できません: {e.Message}";
                return false;
            }
        });
        tail = next;
        return next;
    }

    public async Task<IReadOnlyList<RecoveryCandidate>> ListAsync()
    {
        IReadOnlyList<RecoveryCandidate> result = [];
        if (!await Enqueue(() => result = store.ListCandidates().Select(c => c.Snapshot?.Baseline is { } baseline
            ? c with { OriginalFile = files.Compare(baseline) } : c).ToArray()))
            throw new IOException(status);
        return result;
    }

    public async Task<RecoverySnapshot> ClaimAsync(RecoveryKey key)
    {
        RecoverySnapshot? snapshot = null;
        if (!await Enqueue(() => snapshot = store.Claim(key))) throw new IOException(status);
        return snapshot!;
    }

    public async Task DiscardAsync(RecoveryKey key)
    {
        if (!await Enqueue(() => store.Discard(key))) throw new IOException(status);
    }

    public Task DrainAsync() => tail;
    public async Task<bool> CloseAsync()
    {
        if (stopped) { await tail; return closeSucceeded; }
        stopped = true;
        await Enqueue(() =>
        {
            if (tracked is not null) pendingRetirements.Add(tracked.DocumentId);
            foreach (var id in pendingRetirements.ToArray())
            {
                try { RetireCore(id); }
                catch (Exception e) when (IsStorageError(e))
                {
                    status = $"復元用データを整理できません: {e.Message}";
                }
            }
            closeSucceeded = pendingRetirements.Count == 0;
            if (!closeSucceeded) status = "一部の復元用コピーを整理できませんでした。次回起動時に再表示される場合があります。";
        });
        await tail;
        Dispose();
        return closeSucceeded;
    }

    public void Dispose()
    {
        if (disposed) return;
        stopped = disposed = true;
        // No continuation on this queue needs the UI thread. Dispose without Close preserves drafts.
        tail.GetAwaiter().GetResult();
        store.Dispose();
    }

    private static bool IsStorageError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
