namespace Syuas.Core.Services;

// Only session folders with our marker and an exclusive lease are eligible for cleanup.
public sealed class PreviewDataSession : IDisposable
{
    private readonly string folder;
    private FileStream? lease;
    public string DataFolder => Path.Combine(folder, "Data");

    public PreviewDataSession(string root)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        RejectRedirect(root);
        folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        lease = new FileStream(Path.Combine(folder, "session.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try { File.WriteAllText(Path.Combine(folder, "delete-on-exit"), "SYUAS Preview Session v1"); }
        catch { Dispose(); throw; }
    }

    public void DeleteData()
    {
        if (lease is null) throw new ObjectDisposedException(nameof(PreviewDataSession));
        DeleteTree(DataFolder);
        // Keep the empty ownership directory until the next startup, so the lease protects
        // the entire recursive deletion and no other process can race it.
    }

    public static IReadOnlyList<string> CleanupAbandoned(string root)
    {
        var errors = new List<string>();
        try
        {
            root = Path.GetFullPath(root);
            if (!IsDirectory(root)) return errors;
            RejectRedirect(root);
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
                try
                {
                    RejectRedirect(folder);
                    var marker = Path.Combine(folder, "delete-on-exit");
                    if (!File.Exists(marker)) continue;
                    RejectRedirect(marker);
                    if (File.ReadAllText(marker) != "SYUAS Preview Session v1") continue;
                    var lockPath = Path.Combine(folder, "session.lock");
                    RejectRedirect(lockPath);
                    FileStream guard;
                    try { guard = new(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33) { continue; }
                    using (guard) DeleteTree(Path.Combine(folder, "Data"));
                    // Session IDs are never reused. No running owner can reacquire this folder.
                    File.Delete(marker);
                    File.Delete(lockPath);
                    Directory.Delete(folder, recursive: false);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { errors.Add($"{folder}: {e.Message}"); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { errors.Add($"{root}: {e.Message}"); }
        return errors;
    }

    private static void DeleteTree(string path)
    {
        if (!IsDirectory(path)) return;
        // Validate the tree before recursion; never follow a junction out of our session.
        ValidateTree(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ValidateTree(string path)
    {
        RejectRedirect(path);
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            RejectRedirect(child);
            if ((File.GetAttributes(child) & FileAttributes.Directory) != 0) ValidateTree(child);
        }
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
                throw new IOException("プレビューデータの保存先がフォルダーではありません。");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void RejectRedirect(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("転送されたプレビューデータは削除しません。");
    }

    public void Dispose() { lease?.Dispose(); lease = null; }
}
