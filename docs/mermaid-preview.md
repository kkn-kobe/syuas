# Mermaid プレビュー

## 表示と隔離

`[mermaid]` の listing ブロックを Asciidoctor.js 4.1.0 の拡張 API で処理します。
図のソースは HTML として評価せず、変換ごとのランダムなプレースホルダーと対応付けます。
通常の include のアクセス制限・文書世代管理は既存実装を利用します。

同梱の Mermaid 12.3.0 は、`sandbox="allow-scripts"` の専用 iframe で動作します。
`allow-same-origin` は付けず、ホストや文書の DOM にアクセスできない opaque origin にします。
描画用 CSP は外部接続、フレーム、Web Worker、外部フォントを禁止します。
要求と応答は送信元の Window と要求ごとのトークンで照合します。

ホスト側は返された SVG を data URL の画像として文書へ渡します。
文書用 iframe の `sandbox="allow-same-origin"` とスクリプト禁止 CSP は維持します。
Mermaid の strict モードに加え、出力 SVG の script、foreignObject、画像、外部参照、
イベント属性、アニメーションを除去します。図内設定は受け付けません。
描画用フレームには文書フォルダーへのアクセス権を与えません。

日本語フォントは端末の Yu Gothic UI / Meiryo を使います。
レイアウトは dagre、テーマは default、look は classic に固定します。
配布バンドルに ELK が含まれるため、使用するレイアウトにかかわらずそのライセンスにも対応します。

## 対応範囲と上限

- `[mermaid]` と `----` で囲まれた図を対象とします。
- `[source,mermaid]` は通常のコード表示です。
- 1 文書 32 図、1 図 20,000 文字、Mermaid の maxEdges は 200。
- 1 回の描画応答は最大 4 MiB、待ち時間は 10 秒。タイムアウト後は描画フレームを破棄します。
- タイムアウトは非同期処理の復旧策です。ブラウザーのメインスレッドを占有する
  病的な入力を必ず期限内に中断できる保証ではありません。
- エラーは各図の位置に表示します。古い文書の描画結果は反映しません。
- 外部画像、追加アイコン、HTML ラベル、クリック操作、図内設定は対象外です。
- アプリは SVG キャッシュファイルを書き出しません。WebView2 のデータ保存設定は従来どおり適用されます。

## ライセンス確認と配布

Mermaid の公式 npm アーカイブとソースマップを照合し、79 個の npm パッケージを特定しました。
パーサーの入れ子のソースマップ、D3 Delaunay / RoughJS の内部バンドル、
ELK の Java 由来コードも調べています。全通知は `Licenses/Mermaid-NOTICES.txt`、
バージョン・配布元・整合性値は `Licenses/Mermaid-INVENTORY.json` に記録しています。
khroma は npm の license フィールドが欠落していますが、同梱 license 原文で MIT と確認しています。
fastdom は README のライセンス全文を保持しています。

採用条件は MIT、ISC、BSD-3-Clause、Apache-2.0、Unlicense、EPL-1.0、EPL-2.0 です。
DOMPurify は Apache-2.0、elkjs は EPL-2.0 を選択しています。
独立した SYUAS のソースは MIT のままです。これらの条件は営利利用を禁止していません。
第三者コードを SYUAS の MIT へ再ライセンスするものではありません。

EPL 対象の対応ソースを `Licenses/MermaidSources` に同梱しています。
ELK / elkjs、EMF GWT、Xtext Xbase のソースと入手方法を利用者へ提供します。
GWT・Guava の実行時部分には Apache-2.0 が適用されます。
配布時は `Licenses` を全体として保持してください。
ライセンス画面から原文・第三者通知・ソースの入手方法をオフラインで読めます。

更新時は、新版の配布物とソースマップから依存関係を再確認し、メタデータだけでなく
ライセンス原文・バンドル内の追加通知まで照合します。ソースアーカイブとハッシュも更新します。
MIT の維持または営利利用に影響する条件が見つかった場合は、採用前に利用者へ確認してください。

## 検証

```powershell
$env:SYUAS_MERMAID_SMOKE = Join-Path (Get-Location) '.local/mermaid-verification'
dotnet test src/tests/Syuas.Tests/Syuas.Tests.csproj -c Debug --filter FullyQualifiedName~MermaidPreviewTests
```

WebView2 Runtime が必要です。環境変数を指定した場合に実ブラウザーの検証を実行します。
Mermaid の初回読み込み前に DevTools Protocol でネットワークをオフラインにし、
日本語のフローチャート・シーケンス図・クラス図・状態遷移図、画像のロード完了、
エラーの局所化、設定拒否、上限、include、最新文書への切り替えを確認します。
画面画像を指定フォルダーへ保存します。通常のテストでは配布ファイルのハッシュと通知を確認します。

2026-11-17 の確認では、Mermaid と既存 WebView2 の検証を有効にした Debug の全 560 テストが成功しました。
Release のアプリ publish も成功しています。Release のテスト DLL はこの環境のセキュリティ検査で
読み込みを拒否されたため（0x800700E1）、Release テストの成功は確認していません。
セキュリティ設定の変更や除外登録は行っていません。
