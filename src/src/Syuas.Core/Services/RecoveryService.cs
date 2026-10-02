using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Track/Tick/Capture are called on the UI thread. Only immutable snapshots cross to the worker.
public sealed class RecoveryService : IDisposable
{
    private readonly IRecoveryStore store;
    private readonly TimeProvider clock;
    private readonly IFileService files;
    private Task tail = Task.CompletedTask;
    private Guid? currentDocument;
    private readonly Dictionary<Guid, TrackingState> trackedDocuments = [];
    private sealed class TrackingState
    {
        public DocumentSession Session { get; set; } = null!;
        public DateTimeOffset? FirstUnwritten, LastChange, LastAttempt;
        public long? WrittenRevision;
        public bool Writing;
    }
    private bool stopped, disposed;
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

    // Single-document caller compatibility: replacing a document retires the old draft.
    public void Track(DocumentSession session)
    {
        if (stopped) return;
        if (currentDocument is { } old && old != session.DocumentId) _ = RemoveDocumentAsync(old);
        currentDocument = session.DocumentId;
        TrackDocument(session);
    }

    // Workspace callers track every open document, regardless of the selected tab.
    public void TrackDocument(DocumentSession session)
    {
        if (stopped) return;
        if (!trackedDocuments.TryGetValue(session.DocumentId, out var state))
            trackedDocuments.Add(session.DocumentId, state = new());
        var old = state.Session;
        state.Session = session;
        if (!session.IsModified)
        {
            if (old?.IsModified == true) Retire(session.DocumentId);
            state.FirstUnwritten = state.LastChange = null;
            state.WrittenRevision = null;
        }
        else if (old is null || old.Revision != session.Revision || !old.IsModified)
        {
            var now = clock.GetUtcNow();
            state.FirstUnwritten ??= now;
            state.LastChange = now;
        }
    }

    public Task TickAsync(Func<RecoverySnapshot> capture) => currentDocument is { } id
        ? TickDocumentAsync(id, capture) : Task.CompletedTask;

    public async Task TickDocumentAsync(Guid id, Func<RecoverySnapshot> capture)
    {
        if (stopped || !trackedDocuments.TryGetValue(id, out var state) || state.Writing ||
            !state.Session.IsModified || state.WrittenRevision == state.Session.Revision) return;
        var now = clock.GetUtcNow();
        if (state.LastAttempt is { } attempt && now - attempt < TimeSpan.FromSeconds(5)) return;
        if (now - (state.LastChange ?? now) < TimeSpan.FromSeconds(5) &&
            now - (state.FirstUnwritten ?? now) < TimeSpan.FromSeconds(30)) return;
        var snapshot = capture();
        if (snapshot.DocumentId != id) throw new InvalidOperationException("復元対象の文書が一致しません。");
        state.Writing = true;
        state.LastAttempt = now;
        try
        {
            var success = await Enqueue(() =>
            {
                store.Write(snapshot);
                pendingRetirements.Remove(snapshot.DocumentId);
                status = $"復元用コピー: {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
            });
            if (success && trackedDocuments.TryGetValue(id, out var current) && ReferenceEquals(current, state))
            {
                state.WrittenRevision = snapshot.Revision;
                state.FirstUnwritten = state.Session.Revision == snapshot.Revision ? null : clock.GetUtcNow();
            }
        }
        finally { state.Writing = false; }
    }

    public Task<bool> RemoveDocumentAsync(Guid id)
    {
        if (stopped) return Task.FromResult(false);
        trackedDocuments.Remove(id);
        return Enqueue(() => RetireCore(id));
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
        var id = trackedDocuments.ContainsKey(key.DocumentId) ? Guid.NewGuid() : key.DocumentId;
        if (!await Enqueue(() => snapshot = store.Claim(key, id))) throw new IOException(status);
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
        var ids = trackedDocuments.Keys.ToArray();
        await Enqueue(() =>
        {
            foreach (var id in ids) pendingRetirements.Add(id);
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

