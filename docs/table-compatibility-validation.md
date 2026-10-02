# 表の再編集：互換性検証

表の再編集について、保存済みの表、実際のAvalonEdit、ファイル保存・復元・外部変更サービス、同梱のAsciidoctor.jsを使う検証手順と実施記録です。通常テストと、環境変数で有効にする実WebView2のHTML照合は分けて実行します。

## 保存済みサンプルと通常テスト

`samples/table-reediting/` の対応サンプル6個と対応外サンプル5個をテスト出力へコピーします。期待値はParserやGeneratorから生成せず、セルの座標・結合・本文・表示文字列を手で定義しています。

`TableCompatibilityTests` の36件は、次を確認します。

| 件数 | 確認内容 |
| --- | --- |
| 24 | 対応6サンプル×LF／CRLF×UTF-8 BOM有無。実ファイルを開く、変更なし適用、セル修正、1回のUndo／Redo、保存、新しいエディタで再オープン、再編集。前後の本文・Anchor・コメント・改行を保持し、保存時は既存仕様どおりBOMなしUTF-8とする |
| 5 | 対応外サンプルの拒否。デザイナーを開かず理由と位置を表示し、本文・リビジョン・履歴・未保存状態・元ファイルのバイト列を保持する |
| 3 | 表を適用して自動退避後、確認済み終了を経ずにセッションを破棄して復元。元ファイルが変更なし／外部変更あり／削除済みの場合で、復元後の再編集・Undo／Redoと保存基準の維持を確認する |
| 1 | デザイナーを開いている間の自動退避に、未適用のセル編集が混入しない |
| 3 | デザイナー表示中に外部ファイルを書き換える。適用後の通知、通知を閉じた後の保存前チェック、キャンセル／別名保存／外部版のバックアップ付き上書きを確認する |

復元のテストは、実ストアへの書き込み後にViewModelとエディタを作り直して起動時復元へ接続します。このテストでOSやプロセスを強制終了しているわけではありません。子プロセスの強制終了・障害検証は [障害検証](failure-validation.md) を参照してください。外部変更は実ファイルに書き込み、通知の検査を明示実行するため、ファイル監視イベントの配信タイミングには依存しません。

```powershell
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter FullyQualifiedName~TableCompatibilityTests
dotnet test SYUAS.sln --no-restore
dotnet build SYUAS.sln --no-restore -c Release
```

## 実WebView2とAsciidoctor.jsの照合

`WindowTests` に環境変数で有効化する追加検査を用意しています。製品の `HtmlPreviewControl` と同梱Asciidoctor.js 4.1.0を使用し、透明な非アクティブウィンドウで確認します。通常のテスト実行だけではこのHTML検査は実行しません。WindowsとWebView2 Runtimeが必要です。

対応6サンプルについて「保存済みの元ソース」「読み戻し後の再生成」「先頭セルの修正後」の3通り、計18ケースを変換します。

- 表が1個であること、ヘッダー行数、th／td、タイトル、列幅の比率。
- 出力された行の順番、各セルのrowspan／colspan、ソース上の論理的な占有範囲。
- 日本語・絵文字、空セル、文字参照、特殊文字の表示文字列。HTMLの空白は正規化して比較する。
- 太字・等幅要素と、複数段落セルの段落数。
- 元ソースと再生成後のDOM要約が一致すること、および修正した文字列が表示に反映されること。

実行例（テスト専用のWebView2プロファイルを用意し、通常利用のプロファイルと分離します）：

```powershell
$env:SYUAS_TABLE_COMPATIBILITY_SMOKE = Join-Path $PWD '.local/table-compatibility'
$env:SYUAS_TABLE_HELP_SCREENSHOT = Join-Path $PWD '.local/table-help.png'
$previousWebViewData = $env:WEBVIEW2_USER_DATA_FOLDER
$env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $PWD ('.local/table-webview-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $PWD '.local') | Out-Null
try {
    dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter FullyQualifiedName~WindowTests
    if ($LASTEXITCODE -ne 0) { throw '表の互換性検証に失敗しました。' }
} finally {
    Remove-Item Env:SYUAS_TABLE_COMPATIBILITY_SMOKE
    Remove-Item Env:SYUAS_TABLE_HELP_SCREENSHOT
    $env:WEBVIEW2_USER_DATA_FOLDER = $previousWebViewData
}
```

指定フォルダーへ各ケースのHTMLとDOM要約JSON、ヘッダー・複合結合・文字参照の修正後PNG、実行結果 `results.json` を出力します。結果にはエンジンのSHA-256とWebView2バージョンを記録します。開始時に結果を未完了へ更新し、全ケース成功時だけ `completed: true` とするため、途中で失敗しても以前の成功記録を誤認しません。過去の個別HTML／PNGが残る場合は、今回の `results.json` に記録されたケースを基準にしてください。

テスト実行環境でWebView2のプロセス起動が制限されていると、初期化が失敗またはタイムアウトします。この場合は検証済みとはせず、WebView2を実行できるWindows環境で再実行してください。

## 変換エンジンの表示特性と対象外

3行×2列の全体結合サンプルでは、モデルは3行を保持しますが、Asciidoctor.jsは1つの `tr` と `colspan="2" rowspan="3"` のセルを出力します。セル開始のない行の空の `tr` は出力されません。検証では論理行数とHTMLの行数を同一視せず、セル開始行とspan指定を照合します。デザイナー上の行の高さ・マス目と、ブラウザー上の高さ・配置の完全一致を保証する検査ではありません。

この検査は現在の限定された生成形式と同梱エンジンを対象にしています。任意のAsciiDoc表、他バージョンのAsciidoctor、PDF出力、異なるブラウザーのレイアウトの互換性を保証するものではありません。追加属性・CSV・セルスタイル・include等を自動変換する機能は追加していません。エンジンや表生成処理を更新した際は、固定サンプルと実HTML検査を再実行してください。

## 利用説明と画面確認

「ヘルプ → 表の再編集の使い方」、または表デザイナーの「使い方」から利用説明を開けます。説明は `docs/table-reediting-guide.txt` を埋め込んで配布するため、ネット接続やリポジトリがなくても開けます。作成・再編集、結合解除とUndoの違い、入力のまとまり、Redoの分岐と履歴上限、適用と保存の違い、エラー後の対処、未適用の変更と自動復元、外部変更との競合を説明します。

WPFテストで説明リソースの読み込みとレイアウトを確認し、`SYUAS_TABLE_HELP_SCREENSHOT` 指定時はPNGへ出力します。

Undo／Redoの第4段階では、このファイルで紹介する保存・復元・外部変更のテストにもデザイナー内のUndo／Redoを組み込みました。追加した回帰確認と実施記録は [表デザイナーのUndo／Redo](table-designer-history.md) を参照してください。以下の実施記録は当初の互換性検証時のものです。

## 実施記録（表の再編集の互換性検証追加時）

2026-09-30、Windows／.NET 8ターゲット（SDK 10.0.401）での確認記録です。件数・バージョン・生成物のパスはこの実施時点のもので、ドキュメント更新時に再実行した結果ではありません。

- 通常テスト全442件成功（互換性検証で追加した36件を含む）。
- Releaseビルド成功、警告0・エラー0。
- WebView2 154.0.4258.37で18ケースのHTML照合成功。
- 同梱Asciidoctor.jsのSHA-256: `208694685D3087A8F617E896947D1813BEFE68AD9616F229300AB09CC97C3588`。
- ヘッダー・複合結合・文字参照のHTML画像、および利用説明画面を目視確認。

実行時の詳細記録は `.local/table-compatibility/results.json`、HTML／DOM要約／PNGは同フォルダー、利用説明画面は `.local/table-help.png` に出力しています。`.local` は生成物であり配布物やソース管理の対象ではありません。
