using System.Text.Json;
using Syuas.Core.Models;
using Syuas.Core.Services;

namespace Syuas.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SYUAS-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsDoNotCreateAnyFiles()
    {
        Assert.Equal(new AppSettings(), new SettingsStore(Path.Combine(root, "settings.json")).Load());
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(false, PreviewDataMode.Keep)]
    [InlineData(false, PreviewDataMode.DeleteOnExit)]
    [InlineData(true, PreviewDataMode.Disabled)]
    public void IndependentSettingsPersistAcrossInstances(bool recovery, PreviewDataMode preview)
    {
        var path = Path.Combine(root, "settings.json");
        var settings = new AppSettings(recovery, preview);
        new SettingsStore(path).Save(settings);
        Assert.Equal(settings, new SettingsStore(path).Load());
        Assert.Single(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"RecoveryEnabled\":false}")]
    [InlineData("{\"RecoveryEnabled\":true,\"PreviewData\":\"unknown\"}")]
    public void CorruptSettingsAreReportedInsteadOfSilentlyEnablingStorage(string json)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, json);
        Assert.ThrowsAny<JsonException>(() => new SettingsStore(path).Load());
    }

    [Fact]
    public void CleanupRemovesOnlyAbandonedTemporaryData()
    {
        using var active = new PreviewDataSession(root);
        Directory.CreateDirectory(active.DataFolder);
        File.WriteAllText(Path.Combine(active.DataFolder, "active"), "must stay");
        string abandonedPath;
        using (var abandoned = new PreviewDataSession(root))
        {
            abandonedPath = abandoned.DataFolder;
            Directory.CreateDirectory(abandonedPath);
            File.WriteAllText(Path.Combine(abandonedPath, "cache"), "remove");
        }
        var retained = Path.Combine(root, "WebView2");
        Directory.CreateDirectory(retained);
        File.WriteAllText(Path.Combine(retained, "cache"), "keep");
        var unmarked = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unmarked);
        File.WriteAllText(Path.Combine(unmarked, "unknown"), "keep");
        Assert.Empty(PreviewDataSession.CleanupAbandoned(root));
        Assert.False(Directory.Exists(Path.GetDirectoryName(abandonedPath)));
        Assert.True(File.Exists(Path.Combine(active.DataFolder, "active")));
        Assert.True(File.Exists(Path.Combine(retained, "cache")));
        Assert.True(File.Exists(Path.Combine(unmarked, "unknown")));
    }

    [Fact]
    public void FailedCleanupIsReportedAndCanBeRetried()
    {
        string data;
        using (var session = new PreviewDataSession(root))
        {
            data = session.DataFolder;
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "locked"), "cache");
        }
        using (var locked = new FileStream(Path.Combine(data, "locked"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Single(PreviewDataSession.CleanupAbandoned(root));
        Assert.True(Directory.Exists(data));
        Assert.Empty(PreviewDataSession.CleanupAbandoned(root));
        Assert.False(Directory.Exists(data));
    }

    [Fact]
    public void CurrentSessionDeletionDoesNotRemoveOtherSessions()
    {
        using var first = new PreviewDataSession(root);
        using var other = new PreviewDataSession(root);
        Directory.CreateDirectory(first.DataFolder);
        Directory.CreateDirectory(other.DataFolder);
        File.WriteAllText(Path.Combine(first.DataFolder, "cache"), "remove");
        File.WriteAllText(Path.Combine(other.DataFolder, "cache"), "keep");
        first.DeleteData();
        Assert.False(Directory.Exists(first.DataFolder));
        Assert.True(File.Exists(Path.Combine(other.DataFolder, "cache")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
