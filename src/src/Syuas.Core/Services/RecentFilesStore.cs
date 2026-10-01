using System.Text.Json;

namespace Syuas.Core.Services;

public sealed class RecentFilesStore(string path) : IRecentFilesStore
{
    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(path)) return [];
        return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    public void Save(IReadOnlyList<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        new Utf8FileService().Write(path, JsonSerializer.Serialize(paths.Take(10)));
    }
}
