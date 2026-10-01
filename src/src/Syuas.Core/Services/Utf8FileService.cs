using System.Text;

namespace Syuas.Core.Services;

public sealed class Utf8FileService : IFileService
{
    private static readonly UTF8Encoding Encoding = new(false, true);

    public string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return Encoding.GetString(bytes, offset, bytes.Length - offset);
    }

    public void Write(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, text, Encoding);
            // Replace only after the complete UTF-8 document has been written.
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, null);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
