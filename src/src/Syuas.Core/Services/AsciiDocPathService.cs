namespace Syuas.Core.Services;

public static class AsciiDocPathService
{
    public static string Resolve(string filePath, string? documentPath, bool relative)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("ファイルを指定してください。");
        if (filePath.IndexOfAny(['\r', '\n', '[', ']']) >= 0)
            throw new ArgumentException("ファイルパスに改行や [ ] は使用できません。");
        if (documentPath is null && !Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("未保存の文書ではファイルを参照ボタンから選ぶか、絶対パスを入力してください。");
        var directory = documentPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(documentPath))!;
        var fullPath = directory is null ? Path.GetFullPath(filePath) : Path.GetFullPath(filePath, directory);
        return (relative && directory is not null ? Path.GetRelativePath(directory, fullPath) : fullPath).Replace('\\', '/');
    }
}
