namespace Syuas.Core.Services;

public static class PreviewResourcePolicy
{
    public const string DocumentHost = "document.syuas.local";
    public static string? Resolve(string uri, string? documentPath)
    {
        if (documentPath is null || !Uri.TryCreate(uri, UriKind.Absolute, out var address)
            || address.Scheme != "https" || address.Host != DocumentHost || !address.IsDefaultPort) return null;
        var relative = Uri.UnescapeDataString(address.AbsolutePath).TrimStart('/');
        if (relative.Length == 0 || relative.IndexOfAny(['\\', ':', '\0']) >= 0) return null;
        var segments = relative.Split('/');
        if (segments.Any(s => s is ".." or "." || s.TrimEnd(' ', '.') != s)) return null;
        var root = Path.GetDirectoryName(Path.GetFullPath(documentPath))!;
        var path = root;
        // Do not follow junctions/symlinks out of the document folder.
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) return null;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            if (File.Exists(path) || Directory.Exists(path))
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return null;
        }
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }
}
