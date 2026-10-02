using System.Text;
using Syuas.Core.Models;

namespace Syuas.Core.Services;

public sealed class Utf8FileService : IFileService
{
    private static readonly UTF8Encoding Encoding = new(false, true);

    public string Read(string path) => ReadSnapshot(path).Text;

    public FileSnapshot ReadSnapshot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = ReadBytes(fullPath);
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return new(Encoding.GetString(bytes, offset, bytes.Length - offset),
            new(fullPath, FileFingerprint.FromBytes(bytes)));
    }

    public void Write(string path, string text) => WriteSnapshot(path, text);

    public FileBaseline WriteSnapshot(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = Encoding.GetBytes(text);
        // Derive the baseline before committing. Re-reading afterwards could adopt an external edit.
        var baseline = new FileBaseline(fullPath, FileFingerprint.FromBytes(bytes));
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            // Replace only after the complete UTF-8 document has been written.
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, null);
            else
                File.Move(temporary, fullPath);
            return baseline;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public FileComparison Compare(FileBaseline baseline)
    {
        try
        {
            var fullPath = Path.GetFullPath(baseline.FullPath);
            // Do not decode: an external non-UTF-8 edit is still a content change.
            var current = new FileBaseline(fullPath, FileFingerprint.FromBytes(ReadBytes(fullPath)));
            return new(current.Fingerprint == baseline.Fingerprint
                ? FileComparisonStatus.Unchanged : FileComparisonStatus.Modified, current);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(FileComparisonStatus.Missing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(FileComparisonStatus.Unavailable, Error: e.Message);
        }
    }

    private static byte[] ReadBytes(string path)
    {
        // A writer/deleter cannot change the file during this read. Sharing failures remain errors.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
