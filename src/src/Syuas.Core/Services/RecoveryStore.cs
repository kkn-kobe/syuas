using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

// Calls are serialized by RecoveryService. Other application instances are excluded by leases.
public sealed class RecoveryStore : IRecoveryStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string root;
    private readonly string folder;
    private readonly FileStream lease;
    private bool disposed;
    public Guid SessionId { get; } = Guid.NewGuid();

    public RecoveryStore(string root)
    {
        this.root = Path.GetFullPath(root);
        folder = SessionFolder(SessionId);
        Directory.CreateDirectory(folder);
        lease = AcquireLease(folder);
    }

    public void Write(RecoverySnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Validate(snapshot);
        WriteEntry(folder, new Entry(1, SessionId, snapshot.DocumentId, snapshot));
    }

    public void Retire(Guid documentId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RetireIn(folder, SessionId, documentId);
    }

    public IReadOnlyList<RecoveryCandidate> ListCandidates()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var candidates = new List<RecoveryCandidate>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var sessionId) || sessionId == SessionId) continue;
            // Never traverse a redirected recovery directory.
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            FileStream sessionLease;
            try { sessionLease = AcquireLease(directory); }
            catch (IOException) { continue; } // An active process (or concurrent claimant) owns it.
            using (sessionLease)
            {
                var ids = Directory.EnumerateFiles(directory, "*.json")
                    .Select(p => Path.GetFileName(p).Split('.')[0])
                    .Where(s => Guid.TryParseExact(s, "N", out _)).Select(Guid.Parse).Distinct();
                foreach (var id in ids)
                {
                    var candidate = ReadCandidate(directory, new(sessionId, id));
                    if (candidate is not null) candidates.Add(candidate);
                }
            }
        }
        return candidates.OrderByDescending(c => c.Snapshot?.CapturedAt).ToArray();
    }

    public RecoverySnapshot Claim(RecoveryKey key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var source = ForeignFolder(key);
        using var sourceLease = AcquireLease(source);
        var candidate = ReadCandidate(source, key);
        var snapshot = candidate?.Snapshot ?? throw new IOException("この復元候補は使用中、破棄済み、または破損しています。一覧を更新してください。");
        Write(snapshot);
        RetireIn(source, key.SessionId, key.DocumentId);
        return snapshot;
    }

    public void Discard(RecoveryKey key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var source = ForeignFolder(key);
        using var sourceLease = AcquireLease(source);
        RetireIn(source, key.SessionId, key.DocumentId);
    }

    private string ForeignFolder(RecoveryKey key)
    {
        if (key.SessionId == SessionId || key.SessionId == Guid.Empty || key.DocumentId == Guid.Empty)
            throw new ArgumentException("復元候補の識別子が不正です。");
        var source = SessionFolder(key.SessionId);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("復元フォルダーが別の場所へ転送されています。");
        return source;
    }

    private static RecoveryCandidate? ReadCandidate(string directory, RecoveryKey key)
    {
        var current = FileName(directory, key.DocumentId, "current");
        var previous = FileName(directory, key.DocumentId, "previous");
        try
        {
            var entry = ReadEntry(current, key);
            return entry.Snapshot is null ? null : new(key, entry.Snapshot);
        }
        catch (Exception e) when (IsDataError(e))
        {
            try
            {
                var entry = ReadEntry(previous, key);
                return entry.Snapshot is null ? null : new(key, entry.Snapshot, true, "最新の退避データを読み取れないため、直前の世代から復元します。");
            }
            catch (Exception fallback) when (IsDataError(fallback))
            {
                return new(key, null, Error: "復元データを読み取れません。元データは保持しています。");
            }
        }
    }

    private static Entry ReadEntry(string path, RecoveryKey key)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("転送された復元ファイルです。");
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path, Utf8)) ?? throw new InvalidDataException();
        if (envelope.Payload is null || envelope.Checksum != Hash(envelope.Payload)) throw new InvalidDataException("チェックサムが一致しません。");
        var entry = JsonSerializer.Deserialize<Entry>(envelope.Payload) ?? throw new InvalidDataException();
        if (entry.Version != 1 || entry.SessionId != key.SessionId || entry.DocumentId != key.DocumentId) throw new InvalidDataException("復元データの形式が不正です。");
        if (entry.Snapshot is { } snapshot)
        {
            Validate(snapshot);
            if (snapshot.DocumentId != key.DocumentId) throw new InvalidDataException();
        }
        return entry;
    }

    private static void Validate(RecoverySnapshot snapshot)
    {
        if (snapshot.DocumentId == Guid.Empty || snapshot.Revision < 0 || snapshot.Text is null ||
            snapshot.SelectionStart < 0 || snapshot.SelectionLength < 0 || snapshot.CaretOffset < 0)
            throw new InvalidDataException("復元データの値が不正です。");
        if (snapshot.Baseline is { } baseline &&
            (string.IsNullOrWhiteSpace(baseline.FullPath) || !Path.IsPathFullyQualified(baseline.FullPath) ||
             baseline.Fingerprint is null || baseline.Fingerprint.ByteLength < 0 ||
             baseline.Fingerprint.Sha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit)))
            throw new InvalidDataException("元ファイルの照合基準が不正です。");
        // Fail on malformed UTF-16 rather than persisting replacement characters.
        _ = Utf8.GetByteCount(snapshot.Text);
    }

    private static void RetireIn(string directory, Guid sessionId, Guid documentId)
    {
        if (!File.Exists(FileName(directory, documentId, "current")) && !File.Exists(FileName(directory, documentId, "previous"))) return;
        // A durable tombstone suppresses older generations even if cleanup is interrupted.
        WriteEntry(directory, new Entry(1, sessionId, documentId, null), keepPrevious: false);
        File.Delete(FileName(directory, documentId, "previous"));
    }

    private static void WriteEntry(string directory, Entry entry, bool keepPrevious = true)
    {
        var current = FileName(directory, entry.DocumentId, "current");
        var previous = FileName(directory, entry.DocumentId, "previous");
        var temporary = Path.Combine(directory, $"{entry.DocumentId:N}.{Guid.NewGuid():N}.tmp");
        var payload = JsonSerializer.Serialize(entry);
        var bytes = Utf8.GetBytes(JsonSerializer.Serialize(new Envelope(payload, Hash(payload))));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(current)) File.Replace(temporary, current, keepPrevious ? previous : null);
            else File.Move(temporary, current);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private string SessionFolder(Guid sessionId) => Path.Combine(root, sessionId.ToString("N"));
    private static string FileName(string directory, Guid documentId, string generation) => Path.Combine(directory, $"{documentId:N}.{generation}.json");
    private static FileStream AcquireLease(string directory) => new(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(payload)));
    private static bool IsDataError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
    private sealed record Envelope(string Payload, string Checksum);
    private sealed record Entry(int Version, Guid SessionId, Guid DocumentId, RecoverySnapshot? Snapshot);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lease.Dispose();
    }
}
