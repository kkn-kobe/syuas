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
        var observation = Observe(baseline.FullPath);
        return observation.Status switch
        {
            FileObservationStatus.Present => new(observation.Baseline!.Fingerprint == baseline.Fingerprint
                ? FileComparisonStatus.Unchanged : FileComparisonStatus.Modified, observation.Baseline),
            FileObservationStatus.Missing => new(FileComparisonStatus.Missing),
            _ => new(FileComparisonStatus.Unavailable, Error: observation.Error)
        };
    }

    public FileObservation Observe(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            // Do not decode: an external non-UTF-8 edit is still a content change.
            var current = new FileBaseline(fullPath, FileFingerprint.FromBytes(ReadBytes(fullPath)));
            return new(FileObservationStatus.Present, current);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(FileObservationStatus.Missing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(FileObservationStatus.Unavailable, Error: e.Message);
        }
    }

    public FileSaveResult WriteChecked(string path, string text, FileBaseline? expected, bool preserveBackup = false)
    {
        var fullPath = Path.GetFullPath(path);
        if (expected is not null && !string.Equals(fullPath, expected.FullPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("保存先と照合基準のパスが一致しません。", nameof(expected));

        var observed = Observe(fullPath);
        if (!Matches(observed, expected)) return new(Conflict: observed);

        var bytes = Encoding.GetBytes(text);
        var baseline = new FileBaseline(fullPath, FileFingerprint.FromBytes(bytes));
        var folder = Path.GetDirectoryName(fullPath)!;
        var temporary = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.syuas-backup.{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.{Guid.NewGuid():N}.bak");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            // Check again after preparing the complete output and immediately before committing it.
            observed = Observe(fullPath);
            if (!Matches(observed, expected)) return new(Conflict: observed);
            try
            {
                if (expected is null)
                    File.Move(temporary, fullPath); // No overwrite if a file appeared after the check.
                else
                    File.Replace(temporary, fullPath, backup);
            }
            catch (IOException)
            {
                observed = Observe(fullPath);
                if (!Matches(observed, expected)) return new(Conflict: observed);
                throw;
            }

            if (expected is null) return new(Baseline: baseline);

            // File.Replace captures the actual replaced version, including a change in the final
            // check/replace gap. Keep that version even during an otherwise ordinary save.
            if (!preserveBackup && Matches(Observe(backup), expected))
            {
                try { File.Delete(backup); return new(Baseline: baseline); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return new(Baseline: baseline, BackupPath: backup);
        }
        finally
        {
            // Cleanup must not turn a completed save into a reported failure.
            try { File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool Matches(FileObservation observation, FileBaseline? expected) => expected is null
        ? observation.Status == FileObservationStatus.Missing
        : observation.Status == FileObservationStatus.Present && observation.Baseline!.Fingerprint == expected.Fingerprint;

    private static byte[] ReadBytes(string path)
    {
        // A writer/deleter cannot change the file during this read. Sharing failures remain errors.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
