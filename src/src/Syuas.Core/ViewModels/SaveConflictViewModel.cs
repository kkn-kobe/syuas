using Syuas.Core.Models;

namespace Syuas.Core.ViewModels;

public sealed class SaveConflictViewModel(string path, FileObservation observation, bool isCurrentFile)
{
    public string FilePath { get; } = path;
    public string Title => "保存先の確認 — SYUAS";
    public string Message => observation.Status switch
    {
        FileObservationStatus.Present => isCurrentFile
            ? "このファイルは、最後に開いた／保存した後に外部で変更されています。"
            : "保存先には既にファイルがあります。",
        FileObservationStatus.Missing => "元のファイルが見つかりません。削除または移動された可能性があります。",
        _ => "保存先の状態を確認できません。ロックやアクセス権を確認してから再試行してください。"
    };
    public string Detail => observation.Status switch
    {
        FileObservationStatus.Present => "上書きするときは、置き換え前のファイルを同じフォルダーへ退避します。退避できない場合は保存を中止します。",
        FileObservationStatus.Missing => "元の場所に再作成するか、別の場所に保存できます。移動先のファイルは変更しません。",
        _ => observation.Error ?? "保存先を読み取れませんでした。"
    };
    public string PrimaryLabel => observation.Status switch
    {
        FileObservationStatus.Present => "外部版を退避して上書き",
        FileObservationStatus.Missing => "元の場所に再作成",
        _ => "再試行"
    };
    public SaveConflictDecision PrimaryDecision => observation.Status switch
    {
        FileObservationStatus.Present => SaveConflictDecision.OverwriteWithBackup,
        FileObservationStatus.Missing => SaveConflictDecision.Recreate,
        _ => SaveConflictDecision.Retry
    };
}
