using System.Text.Json;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
    private static RecoverySnapshot Snapshot(string text = "日本語\r\n本文\n", Guid? id = null, long revision = 1)
        => new(id ?? Guid.NewGuid(), revision, DateTimeOffset.UtcNow, text, null, 2, 3, 5);

    [Fact]
    public void ActiveSessionsAreExcludedAndInactiveCopiesRoundTrip()
    {
        using var first = new RecoveryStore(root);
        using var second = new RecoveryStore(root);
        var snapshot = Snapshot();
        first.Write(snapshot);
        Assert.Empty(first.ListCandidates());
        Assert.Empty(second.ListCandidates());
        first.Dispose();
        var candidate = Assert.Single(second.ListCandidates());
        Assert.Equal(snapshot, candidate.Snapshot);
        Assert.Equal(new(first.SessionId, snapshot.DocumentId), candidate.Key);
        Assert.False(candidate.UsedPreviousGeneration);
    }

    [Fact]
    public void LatestCorruptionFallsBackToPreviousGeneration()
    {
        var first = new RecoveryStore(root);
        var previous = Snapshot("previous");
        first.Write(previous);
        first.Write(previous with { Text = "latest", Revision = 2 });
        var currentPath = FilePath(first.SessionId, previous.DocumentId, "current");
        // Valid JSON with an altered payload must fail its checksum.
        File.WriteAllText(currentPath, File.ReadAllText(currentPath).Replace("latest", "broken", StringComparison.Ordinal));
        first.Dispose();
        using var reader = new RecoveryStore(root);
        var candidate = Assert.Single(reader.ListCandidates());
        Assert.Equal(previous, candidate.Snapshot);
        Assert.True(candidate.UsedPreviousGeneration);
        Assert.NotNull(candidate.Error);
    }

    [Fact]
    public void BothCorruptGenerationsDoNotBlockOtherCandidates()
    {
        var first = new RecoveryStore(root);
        var broken = Snapshot();
        var good = Snapshot("good");
        first.Write(broken);
        first.Write(broken with { Revision = 2 });
        first.Write(good);
        File.WriteAllText(FilePath(first.SessionId, broken.DocumentId, "current"), "{");
        File.WriteAllText(FilePath(first.SessionId, broken.DocumentId, "previous"), "broken");
        first.Dispose();
        using var reader = new RecoveryStore(root);
        var candidates = reader.ListCandidates();
        Assert.Equal(2, candidates.Count);
        Assert.Equal(good, Assert.Single(candidates, c => c.Snapshot is not null).Snapshot);
        var invalid = Assert.Single(candidates, c => c.Snapshot is null);
        Assert.NotNull(invalid.Error);
        reader.Discard(invalid.Key);
        Assert.Equal(good, Assert.Single(reader.ListCandidates()).Snapshot);
    }

    [Fact]
    public void RetiredMarkerSuppressesOldGenerationEvenWhenDeletionFails()
    {
        var first = new RecoveryStore(root);
        var snapshot = Snapshot();
        first.Write(snapshot);
        first.Write(snapshot with { Revision = 2 });
        using (var locked = File.Open(FilePath(first.SessionId, snapshot.DocumentId, "previous"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() => first.Retire(snapshot.DocumentId));
        first.Dispose();
        using var reader = new RecoveryStore(root);
        Assert.Empty(reader.ListCandidates());
    }

    [Fact]
    public void ClaimTransfersDurablyAndSurvivesAnotherImmediateRestart()
    {
        var first = new RecoveryStore(root);
        var snapshot = Snapshot("");
        first.Write(snapshot);
        first.Dispose();
        var second = new RecoveryStore(root);
        var key = Assert.Single(second.ListCandidates()).Key;
        Assert.Equal(snapshot, second.Claim(key));
        using (var competitor = new RecoveryStore(root))
        {
            Assert.Empty(competitor.ListCandidates());
            Assert.Throws<IOException>(() => competitor.Claim(key));
        }
        second.Dispose();
        using var third = new RecoveryStore(root);
        var transferred = Assert.Single(third.ListCandidates());
        Assert.Equal(second.SessionId, transferred.Key.SessionId);
        Assert.Equal(snapshot, transferred.Snapshot);
    }

    [Fact]
    public void CandidateCannotBeClaimedOrDiscardedWhileSourceLeaseIsHeld()
    {
        var source = new RecoveryStore(root);
        var snapshot = Snapshot();
        source.Write(snapshot);
        source.Dispose();
        using var reader = new RecoveryStore(root);
        var key = Assert.Single(reader.ListCandidates()).Key;
        using (var lease = new FileStream(Path.Combine(root, key.SessionId.ToString("N"), "session.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => reader.Claim(key));
            Assert.Throws<IOException>(() => reader.Discard(key));
            Assert.Empty(reader.ListCandidates());
        }
        Assert.Equal(snapshot, Assert.Single(reader.ListCandidates()).Snapshot);
    }

    [Fact]
    public void OnlyTwoGenerationsAreKeptAndTemporaryFilesAreIgnored()
    {
        var first = new RecoveryStore(root);
        var snapshot = Snapshot();
        for (var i = 1; i <= 4; i++) first.Write(snapshot with { Text = $"version {i}", Revision = i });
        var folder = Path.Combine(root, first.SessionId.ToString("N"));
        Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
        File.WriteAllText(Path.Combine(folder, $"{Guid.NewGuid():N}.tmp"), "partial data");
        first.Dispose();
        using var reader = new RecoveryStore(root);
        Assert.Equal("version 4", Assert.Single(reader.ListCandidates()).Snapshot!.Text);
        File.Delete(FilePath(first.SessionId, snapshot.DocumentId, "current"));
        Assert.Equal("version 3", Assert.Single(reader.ListCandidates()).Snapshot!.Text);
    }

    [Fact]
    public void InvalidSnapshotDoesNotDestroyTheLastCopy()
    {
        var first = new RecoveryStore(root);
        var snapshot = Snapshot("valid");
        first.Write(snapshot);
        Assert.Throws<System.Text.EncoderFallbackException>(() => first.Write(snapshot with { Text = "\ud800" }));
        first.Dispose();
        using var reader = new RecoveryStore(root);
        Assert.Equal(snapshot, Assert.Single(reader.ListCandidates()).Snapshot);
    }

    [Fact]
    public void RetiredDocumentCanBeEditedAndBackedUpAgain()
    {
        var first = new RecoveryStore(root);
        var snapshot = Snapshot();
        first.Write(snapshot);
        first.Retire(snapshot.DocumentId);
        var changed = snapshot with { Revision = 8, Text = "new edits after save" };
        first.Write(changed);
        first.Dispose();
        using var reader = new RecoveryStore(root);
        Assert.Equal(changed, Assert.Single(reader.ListCandidates()).Snapshot);
    }

    [Fact]
    public void BaselineAndSelectionSurviveSerialization()
    {
        var first = new RecoveryStore(root);
        var baseline = new FileBaseline(Path.GetFullPath("original.adoc"), FileFingerprint.FromBytes("old"u8));
        var snapshot = Snapshot() with { Baseline = baseline };
        first.Write(snapshot);
        first.Dispose();
        using var reader = new RecoveryStore(root);
        Assert.Equal(snapshot, Assert.Single(reader.ListCandidates()).Snapshot);
    }

    private string FilePath(Guid sessionId, Guid documentId, string generation)
        => Path.Combine(root, sessionId.ToString("N"), $"{documentId:N}.{generation}.json");
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
