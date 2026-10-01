using System.Text;
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
