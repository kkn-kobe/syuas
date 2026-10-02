using System.Text;
using System.Security.Cryptography;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class FileAndSearchTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "SYUAS.Tests", Guid.NewGuid().ToString("N"));
    public FileAndSearchTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("日本語\r\n== 見出し\r\n", false)]
    [InlineData("日本語\n== 見出し\n", true)]
    [InlineData("", false)]
    public void Utf8RoundTripsWithOriginalNewlines(string text, bool bom)
    {
        var path = Path.Combine(directory, "test.adoc");
        File.WriteAllText(path, text, new UTF8Encoding(bom));
        var service = new Utf8FileService();
        Assert.Equal(text, service.Read(path));
        service.Write(path, text);
        Assert.Equal(new UTF8Encoding(false).GetBytes(text), File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void InvalidUtf8IsRejectedInsteadOfReplacingCharacters()
    {
        var path = Path.Combine(directory, "invalid.adoc");
        File.WriteAllBytes(path, [0xff, 0xfe, 0x61]);
        Assert.Throws<DecoderFallbackException>(() => new Utf8FileService().Read(path));
    }

    [Fact]
    public void NewFileCanBeSaved()
    {
        var path = Path.Combine(directory, "new.adoc");
        new Utf8FileService().Write(path, "= 新規");
        Assert.Equal("= 新規", File.ReadAllText(path));
    }

    [Fact]
    public void FailedEncodingPreservesExistingFileAndCleansTemporaryFile()
    {
        var path = Path.Combine(directory, "existing.adoc");
        File.WriteAllText(path, "original");
        Assert.Throws<EncoderFallbackException>(() => new Utf8FileService().Write(path, "\ud800"));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("日本語\r\n", true)]
    [InlineData("日本語\n", false)]
    [InlineData("", true)]
    [InlineData("", false)]
    public void SnapshotTextAndHashDescribeTheOriginalBytes(string text, bool bom)
    {
        var path = Path.Combine(directory, "snapshot.adoc");
        File.WriteAllText(path, text, new UTF8Encoding(bom));
        var bytes = File.ReadAllBytes(path);
        var snapshot = new Utf8FileService().ReadSnapshot(path);
        File.WriteAllText(path, "later external change");

        Assert.Equal(text, snapshot.Text);
        Assert.Equal(Path.GetFullPath(path), snapshot.Baseline.FullPath);
        Assert.Equal(bytes.Length, snapshot.Baseline.Fingerprint.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.Baseline.Fingerprint.Sha256);
    }

    [Fact]
    public void ComparisonDetectsChangesWithIdenticalLengthAndTimestamp()
    {
        var path = Path.Combine(directory, "same-metadata.adoc");
        var files = new Utf8FileService();
        var baseline = files.WriteSnapshot(path, "first");
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "other");
        File.SetLastWriteTimeUtc(path, timestamp);

        var comparison = files.Compare(baseline);
        Assert.Equal(FileComparisonStatus.Modified, comparison.Status);
        Assert.Equal(baseline.Fingerprint.ByteLength, comparison.Current!.Fingerprint.ByteLength);
        Assert.NotEqual(baseline.Fingerprint.Sha256, comparison.Current.Fingerprint.Sha256);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void TimestampChangeAloneIsNotAContentChange()
    {
        var path = Path.Combine(directory, "timestamp.adoc");
        var files = new Utf8FileService();
        var baseline = files.WriteSnapshot(path, "same");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(FileComparisonStatus.Unchanged, files.Compare(baseline).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ComparisonIncludesBomAndNewlineChanges(bool changeBom)
    {
        var path = Path.Combine(directory, "format.adoc");
        var files = new Utf8FileService();
        var baseline = files.WriteSnapshot(path, "line\n");
        File.WriteAllText(path, changeBom ? "line\n" : "line\r\n", new UTF8Encoding(changeBom));
        Assert.Equal(FileComparisonStatus.Modified, files.Compare(baseline).Status);
    }

    [Fact]
    public void InvalidExternalUtf8IsAChangeButCannotBeOpened()
    {
        var path = Path.Combine(directory, "invalid-external.adoc");
        var files = new Utf8FileService();
        var baseline = files.WriteSnapshot(path, "valid");
        File.WriteAllBytes(path, [0xff]);
        Assert.Equal(FileComparisonStatus.Modified, files.Compare(baseline).Status);
        Assert.Throws<DecoderFallbackException>(() => files.ReadSnapshot(path));
    }

    [Fact]
    public void ComparisonDistinguishesMissingFileAndMissingParent()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "deleted.adoc");
        var baseline = files.WriteSnapshot(path, "text");
        File.Delete(path);
        Assert.Equal(FileComparisonStatus.Missing, files.Compare(baseline).Status);
        var missingParent = baseline with { FullPath = Path.Combine(directory, "missing", "file.adoc") };
        Assert.Equal(FileComparisonStatus.Missing, files.Compare(missingParent).Status);
    }

    [Fact]
    public void LockedFileIsUnavailableAndCanBeComparedAfterUnlocking()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "locked.adoc");
        var baseline = files.WriteSnapshot(path, "text");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = files.Compare(baseline);
            Assert.Equal(FileComparisonStatus.Unavailable, result.Status);
            Assert.NotEmpty(result.Error!);
            Assert.Null(result.Current);
            Assert.Throws<IOException>(() => files.ReadSnapshot(path));
            Assert.Throws<IOException>(() => files.WriteSnapshot(path, "new text"));
        }
        Assert.Equal(FileComparisonStatus.Unchanged, files.Compare(baseline).Status);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void DirectoryInPlaceOfFileIsUnavailableNotMissing()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "replaced.adoc");
        var baseline = files.WriteSnapshot(path, "text");
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.Equal(FileComparisonStatus.Unavailable, files.Compare(baseline).Status);
    }

    [Fact]
    public void SaveBaselineDescribesCommittedBomlessBytesAndDetectsSubsequentEdits()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "saved.adoc");
        File.WriteAllText(path, "old", new UTF8Encoding(true));
        var baseline = files.WriteSnapshot(path, "本文\r\n");
        Assert.Equal(FileFingerprint.FromBytes(File.ReadAllBytes(path)), baseline.Fingerprint);
        Assert.Equal(FileComparisonStatus.Unchanged, files.Compare(baseline).Status);
        File.WriteAllText(path, "external");
        Assert.Equal(FileComparisonStatus.Modified, files.Compare(baseline).Status);
    }

    [Fact]
    public void CheckedWriteNeverOverwritesAnUnexpectedExistingDestination()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "appeared.adoc");
        File.WriteAllText(path, "existing");
        var result = files.WriteChecked(path, "draft", null);
        Assert.False(result.Succeeded);
        Assert.Equal(FileObservationStatus.Present, result.Conflict!.Status);
        Assert.Equal("existing", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void CheckedWriteRejectsABaselineForAnotherPath()
    {
        var files = new Utf8FileService();
        var first = Path.Combine(directory, "first.adoc");
        var second = Path.Combine(directory, "second.adoc");
        var baseline = files.WriteSnapshot(first, "original");
        File.WriteAllText(second, "another");
        Assert.Throws<ArgumentException>(() => files.WriteChecked(second, "draft", baseline));
        Assert.Equal("another", File.ReadAllText(second));
    }

    [Fact]
    public void CheckedWriteEncodingFailurePreservesDestination()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "encoding.adoc");
        var baseline = files.WriteSnapshot(path, "original");
        Assert.Throws<EncoderFallbackException>(() => files.WriteChecked(path, "\ud800", baseline, true));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void ReadOnlyDestinationFailureDoesNotFallBackToDestructiveWriting()
    {
        var files = new Utf8FileService();
        var path = Path.Combine(directory, "readonly.adoc");
        var baseline = files.WriteSnapshot(path, "original");
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        try
        {
            var error = Record.Exception(() => files.WriteChecked(path, "draft", baseline, true));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { File.SetAttributes(path, attributes); }
    }

    [Fact]
    public void HistoryPersistsAndDeduplicates()
    {
        var store = new RecentFilesStore(Path.Combine(directory, "settings", "recent.json"));
        Assert.Empty(store.Load());
        store.Save(["C:/Docs/a.adoc", "C:/Docs/A.adoc", "C:/Docs/b.adoc"]);
        Assert.Equal(new[] { "C:/Docs/a.adoc", "C:/Docs/b.adoc" }, store.Load());
    }

    [Theory]
    [InlineData("Alpha alpha", "alpha", 0, true, 6)]
    [InlineData("Alpha alpha", "alpha", 0, false, 0)]
    [InlineData("Alpha alpha", "Alpha", 11, true, 0)]
    [InlineData("abc", "", 0, false, -1)]
    [InlineData("abc", "z", 0, false, -1)]
    [InlineData("日本語と日本語", "日本語", 3, true, 4)]
    public void SearchSupportsCaseWrapAndJapanese(string text, string query, int start, bool matchCase, int expected)
        => Assert.Equal(expected, TextSearch.FindNext(text, query, start, matchCase));

    [Fact]
    public void FindAllUsesNonOverlappingLiteralMatches()
    {
        Assert.Equal(new[] { 0, 2 }, TextSearch.FindAll("aaaaa", "aa", true));
        Assert.Equal(new[] { 1, 3 }, TextSearch.FindAll("a.a.", ".", true));
        Assert.Empty(TextSearch.FindAll("text", "", false));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
