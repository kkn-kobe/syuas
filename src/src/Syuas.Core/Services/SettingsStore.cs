using System.Text.Json;
using System.Text.Json.Serialization;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<PreviewDataMode>(allowIntegerValues: false) }
    };

    public AppSettings Load()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            // Missing fields in an existing file must not silently re-enable storage.
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("RecoveryEnabled", out _) ||
                !root.TryGetProperty("PreviewData", out _)) throw new JsonException("設定項目が不足しています。");
            var settings = root.Deserialize<AppSettings>(Options) ?? throw new JsonException("設定が空です。");
            if (!Enum.IsDefined(settings.PreviewData)) throw new JsonException("プレビューの設定が不正です。");
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
    }

    public void Save(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.PreviewData)) throw new ArgumentOutOfRangeException(nameof(settings));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        new Utf8FileService().Write(path, JsonSerializer.Serialize(settings, Options));
    }
}
