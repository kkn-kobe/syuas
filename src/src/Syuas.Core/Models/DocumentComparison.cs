namespace Syuas.Core.Models;

// Both panes are immutable snapshots. Opening this view never changes the save baseline.
public sealed record DocumentComparison(string FilePath, string EditorText, FileSnapshot DiskSnapshot, DateTimeOffset CapturedAt)
{
    public string Summary => EditorText == DiskSnapshot.Text
        ? "表示する本文は同じです。BOMなど、ファイル上のバイト列だけが異なる場合があります。"
        : "左は編集中の内容、右はディスクから取得した内容です。両方とも読み取り専用です。";
}
