using Syuas.Core.Models;

namespace Syuas.Core.ViewModels;

public sealed class RecoveryListViewModel : ObservableObject
{
    private RecoveryItemViewModel? selected;
    public RecoveryListViewModel(IReadOnlyList<RecoveryCandidate> candidates)
    {
        Items = candidates.Select(c => new RecoveryItemViewModel(c)).ToArray();
        selected = Items.FirstOrDefault();
    }
    public IReadOnlyList<RecoveryItemViewModel> Items { get; }
    public RecoveryItemViewModel? Selected
    {
        get => selected;
        set { selected = value; Changed(); Changed(nameof(CanRestore)); Changed(nameof(CanDiscard)); }
    }
    public bool CanRestore => Selected?.Candidate.Snapshot is not null;
    public bool CanDiscard => Selected is not null;
}

public sealed class RecoveryItemViewModel(RecoveryCandidate candidate)
{
    public RecoveryCandidate Candidate { get; } = candidate;
    public string Name => Candidate.Snapshot is null ? "読み取れない復元データ"
        : Candidate.Snapshot.Baseline is { } baseline ? Path.GetFileName(baseline.FullPath) : "無題";
    public string FilePath => Candidate.Snapshot?.Baseline?.FullPath ?? "元ファイルなし";
    public string SavedAt => Candidate.Snapshot?.CapturedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") ?? "日時不明";
    public string State => Candidate.Snapshot is null ? "復元不可" : Candidate.OriginalFile?.Status switch
    {
        FileComparisonStatus.Unchanged => "元ファイルに変更なし",
        FileComparisonStatus.Modified => "元ファイルに外部変更あり",
        FileComparisonStatus.Missing => "元ファイルが見つかりません",
        FileComparisonStatus.Unavailable => "元ファイルの状態を確認できません",
        _ => "無題の文書"
    };
    public string Detail => Candidate.Error ?? "復元後は未保存状態になります。元ファイルへの保存時に外部変更を再確認します。";
}
