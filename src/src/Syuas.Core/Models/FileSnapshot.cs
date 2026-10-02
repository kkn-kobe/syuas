using System.Security.Cryptography;

namespace Syuas.Core.Models;

// Hash the original bytes, including a UTF-8 BOM and the original newline sequences.
public sealed record FileFingerprint(long ByteLength, string Sha256)
{
    public static FileFingerprint FromBytes(ReadOnlySpan<byte> bytes)
        => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
}

public sealed record FileBaseline(string FullPath, FileFingerprint Fingerprint);

// Text and baseline must describe the same read, not two successive disk reads.
public sealed record FileSnapshot(string Text, FileBaseline Baseline);

public enum FileComparisonStatus { Unchanged, Modified, Missing, Unavailable }

public sealed record FileComparison(
    FileComparisonStatus Status, FileBaseline? Current = null, string? Error = null);
