# 表デザイナーのUndo／Redo基盤

## 実装状況

第1段階として、Core層に状態保存・復元と履歴管理を実装しています。表デザイナーのViewModelや画面操作にはまだ接続していません。現在のアプリで結合・行列削除などをデザイナー内でUndoできるようになったわけではありません。

次の段階で、セル入力・属性変更・構造変更をこの履歴へ接続し、その後にボタン・ショートカット・入力のまとめ方・フォーカス復元を実装します。文書へ挿入・適用した表を1回のUndoで戻す既存の動作は維持します。

## 状態の保存と復元

- `TableSnapshot.Capture(table)` は行列数、セルの座標・結合・本文、タイトル、ヘッダー、列幅を保存します。セルは座標順に並べ、読み取り専用コレクションと不変のセル値を保持します。元のモデルや復元先を変更しても保存状態は変わりません。
- `TableDefinition.Restore(snapshot)` は同じモデルのインスタンスへ復元します。既存表の再編集では `TableEditContext.Definition` が参照するモデルを維持できます。セルのインスタンスは作り直すため、ViewModelのセル参照は復元後に再構築する必要があります。
- `TableDesignerSnapshot.Capture(...)` は表の状態に加え、選択範囲、選択の起点、行数・列数の入力文字列を保存します。入力文字列を省略すると、実際の表サイズを使用します。空欄や不正な数値文字列も保存できます。
- 選択範囲は表内で結合セル全体を含み、起点は選択範囲内にある必要があります。選択だけの変化は内容の変更とは判定しません。

AsciiDocへの変換や再解析を経由しません。本文の空白・改行・文字参照と、列幅などの入力途中の値をそのまま保持します。履歴への保存では表構造を検証しますが、タイトルや列幅の記法としての妥当性は要求しません。nullの本文・タイトル・列幅は受け付けません。取り込み用の `TableDefinition.FromCells()` の厳密な検証は変更していません。

行数・列数の入力値と実際の表サイズは別の状態です。基盤は両方をそのまま復元します。「行列数の入力からサイズ適用までを1操作にまとめる」などの操作単位は、次の段階で呼び出し側が決定します。

## 履歴API

`TableEditHistory` は1つの `TableDefinition` に結び付け、モデルを所有するスレッドから使用します。WPF・AvalonEdit・ファイル保存に依存しません。

| API | 動作 |
| --- | --- |
| `Execute(description, before, change)` | 操作前の状態と実モデルを照合し、変更コールバックが返した操作後の状態を記録。例外時はモデルを操作前へ復元し、例外を再送出する |
| `Record(description, before, after)` | 呼び出し側が既に行った変更を記録。文字入力をまとめて記録するときなどに使う。拒否時は履歴を変えず、モデルの自動復元は行わない |
| `Undo()` / `Redo()` | モデルを復元し、選択・入力値の復元に使う `TableDesignerSnapshot` を返す。履歴がなければnull |
| `CanUndo` / `CanRedo` | 実行可能な履歴の有無。`Execute` の実行中はfalse |
| `UndoDescription` / `RedoDescription` | 次に取り消す／やり直す操作の表示名。該当する履歴がなければnull |
| `UndoCount` / `RedoCount` / `EstimatedBytes` | 履歴件数と推定保持容量 |
| `Clear()` | 履歴だけを破棄し、モデルの内容は維持する |

使用例（表全体を選択する最小例）：

```csharp
var table = new TableDefinition(2, 2);
var history = new TableEditHistory(table);

TableDesignerSnapshot Capture() => TableDesignerSnapshot.Capture(
    table, new TableSelection(0, 0, table.RowCount, table.ColumnCount), 0, 0);

history.Execute("セル結合", Capture(), () =>
{
    table.Merge(new TableSelection(0, 0, 2, 2));
    return Capture();
});

var restored = history.Undo(); // 同じtableへ結合前の状態を復元
// 呼び出し側はrestoredから選択・行列数入力を戻し、セル参照とプレビューを更新する。
history.Redo();
```

`Execute` の例外時にもセル参照の再構築が必要です。選択・入力文字列など呼び出し側の状態は、渡した `before` から戻してください。履歴操作には変更通知イベントを設けていないため、コマンドの有効状態やプレビューの更新も呼び出し側が行います。

## 履歴の整合性

- 変更なしの操作や選択だけの移動は記録せず、既存のRedo履歴を保持します。
- Undo後に新しい内容変更を記録すると、その先のRedo履歴を破棄します。
- 操作前の内容は履歴の現在位置と、操作後の表データは実モデルと一致する必要があります。Undo／Redoも復元前に実モデルを照合します。
- 記録されていない変更を検出すると例外を返します。入力中の変更は先に `Record` 等で確定してください。モデル外の行列数入力についても、Undo／Redo前に呼び出し側が確定する必要があります。
- 失敗した `Execute` は途中のモデル変更を取り消し、既存のUndo／Redo履歴を維持します。復元処理自体は履歴を増やしません。
- `Execute` 中の別の履歴操作は拒否します。履歴はダイアログの編集セッション内で使用し、永続化・自動復元は行いません。

## 履歴上限

既定値は100操作・推定32MiBです。コンストラクターで変更できます。UndoとRedoの合計を対象に、上限を超えた場合は古い操作から破棄します。

容量は各操作の前後の状態・操作名について、UTF-16文字数とオブジェクト分の概算から求めます。共有される文字列も重複して数える保守的な値であり、実測したヒープ使用量ではありません。

直近の1操作は必ず保持します。単独の操作が容量上限を超える場合は、他の履歴を破棄してその1操作を残すため、容量上限は厳密なメモリ制限ではありません。大きな表を削除した直後でも、1回のUndoで元に戻せます。

## 検証

```powershell
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter "FullyQualifiedName~TableSnapshotTests|FullyQualifiedName~TableEditHistoryTests"
dotnet test SYUAS.sln --no-restore
```

状態の独立性、本文と不正な入力値の保持、選択範囲、結合・解除・行列操作・属性変更のUndo／Redo、変更なし、Redo分岐、失敗時の復元、未記録変更の拒否、件数・容量上限、100行×50列と長文セルを検証します。再編集サービスが保持するモデルへの適用と、文書側の1回のUndoも統合テストで確認します。

画面への接続、日本語IME、ショートカット、フォーカス復元、文字入力をまとめる操作単位は後続段階の検証対象です。
