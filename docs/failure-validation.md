# 自動復元・外部変更検出の障害検証

## 実行方法

Windows上でリポジトリのルートから実行します。

```powershell
dotnet restore SYUAS.sln
dotnet test SYUAS.sln --no-restore
dotnet run --project tests/Syuas.RecoveryProbe/Syuas.RecoveryProbe.csproj --no-restore
```

通常のテストはロジック、実ファイル、AvalonEdit、WPF画面を検証します。`Syuas.RecoveryProbe` は別途、実際の子プロセスを強制終了して検証します。どちらも一意なテスト用フォルダーを使用し、利用者の `%LOCALAPPDATA%\SYUAS\Recovery` には触れません。

プロセス検証は自分が起動した子プロセスだけを終了します。各待機には上限を設け、失敗時にも子プロセスを終了します。成功時はテストデータを削除し、失敗時は調査用フォルダーの場所を表示して保持します。ウィンドウは表示しません。

## 自動検証の対応表

| 障害・競合 | 検証方法／主なテスト | 確認する結果 |
| --- | --- | --- |
| 退避の容量不足・アクセス拒否 | `RecoveryServiceTests.FailedWriteKeepsPreviousDataAndRetriesWithoutFurtherTyping`。IOException／UnauthorizedAccessExceptionを注入 | 前回のコピーを維持し、エラーを表示。追加編集なしでも再試行して回復 |
| 最新コピーの置換失敗 | `RecoveryStoreTests.FailedAtomicReplacementLeavesBothGenerationsAndRemovesTemporaryFile`。実際に最新ファイルをロック | 最新・直前の世代を保持。一時ファイルを整理 |
| 最新の破損／両世代の破損 | `RecoveryStoreTests.LatestCorruptionFallsBackToPreviousGeneration`、`BothCorruptGenerationsDoNotBlockOtherCandidates`。JSONを改変 | チェックサム検証、直前世代への切り替え。他の文書は復元可能 |
| 復元先の確定失敗 | `RecoveryStoreTests.FailedClaimWriteDoesNotRetireSource`。復元先のファイルをロック | 元の復元候補を整理しない |
| 復元先の確定後、元候補の整理失敗 | `RecoveryStoreTests.FailedSourceCleanupAfterClaimKeepsBothDurableCopies`。元候補をロック | 両方に有効なコピーを残す。処理失敗を返すため重複候補が残る場合がある |
| 整理後の古い世代を削除できない | `RecoveryStoreTests.RetiredMarkerSuppressesOldGenerationEvenWhenDeletionFails`。直前の世代をロック | 確定済みの終了記録によって古い候補の復活を防ぐ |
| 終了記録そのものを確定できない | `RecoveryIntegrationTests.FailedCloseCleanupShowsNoticeAndCopyRemainsRecoverable`。最新コピーをロック | 終了時に案内し、ロックを解放。次回もコピーを読める |
| 以前の文書の整理失敗 | `RecoveryServiceTests.CloseRetriesFailedCleanupOfPreviouslySwitchedDocument`、`FailureOnOldDocumentIsReportedEvenIfCurrentCleanupSucceeds` | 確認済みの終了時に再試行。現在の文書の整理だけ成功しても未完了を隠さない |
| 退避中に保存・文書切り替え | `RecoveryServiceTests.SaveRetirementRunsAfterAnInFlightWrite`、`DocumentSwitchCannotBeOverwrittenByAnOldWriteCompletion`。書き込みを停止・再開 | 保存済み／以前の文書のコピーを遅れて復活させない |
| プロセス強制終了、復元直後の再終了 | `Syuas.RecoveryProbe`。子プロセスを実際に終了 | 本文、日本語・絵文字・改行、保存基準、選択・カーソルを復元。未完了一時ファイルを候補にしない |
| 連続退避中の強制終了 | `Syuas.RecoveryProbe`。書き込みループを実際に終了 | 有効な世代を読み取り、復元後に整理できる |
| 複数プロセスの使用中コピー | `Syuas.RecoveryProbe`、`RecoveryStoreTests.CandidateCannotBeClaimedOrDiscardedWhileSourceLeaseIsHeld` | 使用中のコピーを一覧から除外。復元後の所有権移管も確認 |
| 元ファイルの外部変更・削除・確認中の再変更 | `SaveConflictTests`、`RecoveryIntegrationTests` | 保存基準を勝手に更新しない。再照合して再確認。キャンセルで本文とUndoを維持 |
| ファイルロック・読み取り専用・保存先消失 | `FileAndSearchTests`、`SaveConflictTests`。実ファイルを使用 | 保存失敗を未保存のまま返し、破壊的な上書きへ切り替えない |
| 文字コード不正・BOM・同一サイズ／時刻の変更 | `FileAndSearchTests`、`SaveConflictTests.AcceptedOverwritePreservesExternalBytesIncludingInvalidUtf8` | バイト単位の変更を検出。外部版を元のバイト列で退避。不正UTF-8を再読み込みして本文を置き換えない |
| 監視イベントの連発・欠落・監視エラー | `ExternalChangeServiceTests`。通知と時刻を制御 | イベントをまとめ、定期照合で補完し、監視再開を試みる |
| 照合中のI/O例外・アクセス拒否 | `ExternalChangeServiceTests.StorageExceptionIsReportedAndLaterCheckRecovers`。例外を注入 | 確認不能として通知。次の照合で回復できる |
| ファイルの作成・削除・置換・名前変更 | `FileSystemChangeMonitorTests`。実FileSystemWatcherと実ファイル | 対象パスのイベントを受信し、無関係なファイルを無視 |
| 自分の保存とバックグラウンド照合 | `FileAndSearchTests.BackgroundComparisonDoesNotPreventAtomicSaves` | 同じファイルサービスでI/Oを調整し、共有違反を起こさない |
| 古い照合結果の遅延到着・終了 | `ExternalChangeServiceTests`、`ExternalChangeIntegrationTests` | 切り替え後／保存後／終了後に古い結果を適用しない |
| 通知を閉じる・比較・再読み込み | `ExternalChangeIntegrationTests`、`WindowTests` | 保存前照合を継続。比較は読み取り専用。再読み込みの失敗・キャンセルで文書を保持 |
| 利用説明の同梱 | `WindowTests` | 埋め込みリソースを読み出して画面に表示。コピー可能で編集不可 |

## 第5段階で修正した動作

- 復元用コピーの整理に失敗した文書を覚えておき、確認済みの終了時に再試行します。その後も整理できない場合は、次回起動で候補が再表示される可能性を案内して終了します。復元用コピーと元ファイルの保存結果は別々に扱います。
- バックグラウンド照合からI/O例外やアクセス拒否が返った場合も、確認不能として通知・再試行します。
- 利用説明は [recovery-guide.txt](recovery-guide.txt) をアプリへ埋め込み、「ヘルプ → 自動復元と外部変更の使い方」からネットワーク接続なしで参照できます。

## 実施結果

2026-09-30、Windows上の.NET 8対象ビルドで確認しました。

- 単体・統合・WPFテスト: 260件成功。
- プロセス検証: 使用中データの除外、強制終了後の復元、所有権の移管、復元直後の再終了からの復元、連続退避中の終了からの復元が成功。
- 利用説明画面を画像化して確認。日本語表示、折り返し、スクロール領域、閉じるボタンを確認。

容量不足やアクセス拒否の一部は例外注入で再現しています。物理ディスクを満杯にする試験やACLを変更する試験は行っていません。プロセス終了のタイミングはOSに依存し、置換命令の全中断点を網羅するものではありません。実際の電源断・ストレージ故障・SMB切断・クラウド同期サービスとの組み合わせは未検証です。

## 利用環境で行う確認

実運用文書のコピーを使い、次を確認してください。電源断やシステム全体への障害注入はこの手順に含めません。

1. 無題の文書を編集し、復元用コピーの時刻を確認してから、そのテスト用SYUASだけを終了させます。再起動で本文が復元されることを確認します。
2. 同じテスト文書を別のエディタで編集します。通知が出てもSYUASの本文を維持し、比較・キャンセル・別名保存で両方の版を残せることを確認します。
3. 外部版の退避を選んで保存し、案内された `.bak` が外部版の内容と一致することを確認します。
4. 復元直後に保存せずテスト用SYUASを強制終了し、再度復元できることを確認します。通常終了時の「破棄」とは区別します。
5. 共有フォルダーを利用する場合は専用のテスト用共有で接続喪失と復帰を確認します。確認不能の間も本文が残り、接続復帰後の再確認で更新されることを確認します。
6. エラーが続く場合は編集中の本文を別名で保存し、エラー文、発生時刻、操作、保存先の種類（ローカル／共有など）を記録します。必要に応じて復元フォルダーを保全します。詳細は利用説明の「エラーが出たとき」「復元データの場所と保全」を参照してください。
