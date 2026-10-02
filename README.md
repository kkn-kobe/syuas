# SYUAS

**AsciiDocを書くための、Windows向けソースエディター。**

SYUASは、AsciiDocのソースを直接編集しながら、表・見出し・画像・外部ファイル参照などの記法をGUIで入力できるデスクトップアプリケーションです。技術文書やマニュアルの作成で手間のかかる、セル結合の計算や記号の入力、相対パスの組み立てを支援します。

無料で利用できる、MITライセンスのオープンソースソフトウェアです。

## 主な機能

- **ソース編集**：行番号、AsciiDocの簡易シンタックスハイライト、検索・置換、Undo／Redo、折り返し表示。
- **複数文書のタブ編集**：タブごとの編集状態の保持、ドラッグによる並べ替え、すべて保存、最近使用したファイル。
- **表デザイナー**：行列の追加・削除、セル入力、縦横のセル結合・解除、ヘッダー・列幅の設定、対応形式の既存表の再編集。
- **記法の入力補助**：見出し、画像、include、リンク、相互参照、アンカー、ソースブロック、注意書き、リスト、インライン書式。
- **文書構造とHTMLプレビュー**：見出し一覧からの移動、ソースと表示結果の分割表示。
- **編集内容の保護**：未保存確認、復元用コピーの自動作成、外部変更の検出・比較、保存時の競合確認。

GUIで入力した内容はAsciiDocソースとして文書に反映されます。生成した記法はそのまま確認・編集でき、入力補助による変更は1回のUndoで取り消せます。

## 動作環境

| 項目 | 必要な環境 |
| --- | --- |
| OS | Windows 11 |
| 実行環境 | .NET 8 Desktop Runtime（ランタイムを同梱しない配布版の場合） |
| HTMLプレビュー | Microsoft Edge WebView2 Runtime |
| 文書の文字コード | UTF-8 |

WebView2 Runtimeがない場合もソース編集は利用できます。プレビューを使う場合は、Runtimeを導入してSYUASを再起動してください。HTML変換用のAsciidoctor.jsは同梱されているため、Node.jsやRubyのインストールは不要です。

## 入手と起動

配布版が公開されている場合は、このリポジトリの **Releases** から入手してください。ZIP形式の配布版はフォルダーごと展開し、`SYUAS.exe` を実行します。実行ファイルだけを取り出さず、同梱のファイル・フォルダーを一緒に配置してください。

配布版がない場合や、自分でビルドする場合は、下記の「開発・ビルド」を参照してください。

## 基本的な使い方

1. 「ファイル → 新規作成」で文書を作成するか、「ファイル → 開く」で既存の文書を開きます。
2. 中央のエディターでAsciiDocソースを編集します。「挿入」「書式」メニューやツールバーから入力補助を利用できます。
3. 必要に応じて「表示 → 文書構造」「表示 → HTMLプレビュー」を有効にします。
4. `Ctrl+S` で保存します。新規文書では保存先を指定します。

対応する拡張子は `.adoc`、`.asciidoc`、`.ad`、`.asc`、`.txt` です。UTF-8（BOMあり・なし）の読み込みに対応し、保存はBOMなしUTF-8で行います。UTF-8以外のファイルは読み込みエラーになります。

ファイルをエディターや `SYUAS.exe` へドラッグ＆ドロップして開くこともできます。複数ファイルはそれぞれ別のタブで開きます。コマンドラインからの起動例は次のとおりです。

```powershell
.\SYUAS.exe "C:\Documents\操作 手順.adoc" "C:\Documents\補足.adoc"
```

### 記法の入力補助

入力補助は、新しい記法の挿入と、選択した文章への記法の適用に利用できます。

| 機能 | 入力・操作できる内容 |
| --- | --- |
| 見出し | Level 1〜5（`==`〜`======`）。現在行の見出し化、既存の見出しレベルの変更 |
| 画像 | ファイル参照、Block／Inline、代替テキスト、タイトル、幅・高さ、ID |
| 外部ファイル参照（include） | ファイル参照、相対／絶対パス、lines・tag・tags・leveloffset・indent・encoding・optional |
| リンク・相互参照・アンカー | URL、参照先、表示文字列、ID。文書内の見出し・アンカーからの参照先選択 |
| ソースブロック | 言語の選択・自由入力、選択文字列の取り込み |
| 注意書き（Admonition） | NOTE／TIP／IMPORTANT／CAUTION／WARNING |
| リスト | 箇条書き、番号付きリスト、チェックリスト |
| インライン書式 | 太字、斜体、等幅 |
| その他のブロック | Listing／Literal／Quote／Example |

画像・includeの相対パスは、編集中の文書を保存したフォルダーが基準です。相対パスを使う場合は、先に文書を保存してください。未保存の文書や別ドライブのファイルでは絶対パスを生成します。パス区切りには `/` を使用します。

画像の挿入ダイアログは `imagesdir` などの文書属性を解析しないため、属性を使う文書では生成後のパスを調整してください。

### 表の作成・再編集

1. 「挿入 → 表…」を開き、行数・列数を指定して「サイズを適用」を押します。初期値は3行×3列、最大100行×50列です。
2. セルに文章を入力し、必要に応じてタイトル・ヘッダー・列幅を設定します。
3. クリックと `Shift+クリック` でセル範囲を選び、「セル結合」で結合します。
4. 右側の生成ソースを確認し、「挿入」で文書に反映します。

列数や結合数は自動計算されます。横結合・縦結合・両方向の結合に対応し、行列の追加・削除や結合解除もGUIで操作できます。表デザイナー内でもUndo／Redoを利用できます。

既存の表は、表の中にカーソルを置いて「編集 → 表を再編集…」または右クリックメニューから開けます。**再編集の対象は、現在のSYUASが生成する形式の表に限られます。** CSV形式、1行に複数セルを書く形式、追加属性・セルスタイル・includeを含む表などは対象外です。

詳しい操作と対応範囲は、[表の作成・再編集の使い方](docs/table-reediting-guide.txt)と[表のサンプル](samples/table-reediting/README.md)を参照してください。使い方はアプリ内の「ヘルプ」からも閲覧できます。

### HTMLプレビュー

「表示 → HTMLプレビュー」で、エディターの右側に表示します。保存前の編集内容も反映されます。保存済み文書では、その文書フォルダーと配下にある相対パスのinclude・画像に対応します。

include先などの外部ファイルを変更した場合は、「表示 → プレビューを更新」を実行してください。

プレビューには次の制限があります。

- 親フォルダー、別ドライブ、絶対ファイルパス、シンボリックリンク経由の参照、リモートURLは読み込みません。
- 未保存文書のincludeは展開しません。
- 文書内のスクリプトやフォーム送信、外部リンクへの移動は無効です。
- HTML／PDFファイルの書き出しには対応していません。

入力補助で生成できる参照パスと、プレビューで読み込める参照範囲は異なります。

### 自動復元・外部変更への対応

自動復元は初期設定で有効です。未保存の編集内容を復元用コピーとして保存し、異常終了後の起動時に復元できます。元の文書ファイルへ自動保存する機能ではないため、通常の保存は `Ctrl+S` で行ってください。復元できるのは最後にコピーが作成された時点までで、表デザイナー内の未適用の変更は対象外です。

別のアプリで文書が変更された場合は通知し、編集中の内容との比較、再読み込み、別名保存などを選択できます。保存時にも外部変更を確認し、外部版を退避して上書きする操作を用意しています。

「ツール → 設定…」で、自動復元の有効・無効と、プレビューデータの保存方針を変更できます。初期設定では、設定・履歴・復元用コピー・プレビューデータを `%LOCALAPPDATA%\SYUAS` 配下に保存します。

詳細は[自動復元と外部変更の使い方](docs/recovery-guide.txt)と[データ保存設定](docs/data-storage-settings.md)を参照してください。

### 主なショートカット

| 操作 | キー |
| --- | --- |
| 新規作成／開く／保存 | `Ctrl+N`／`Ctrl+O`／`Ctrl+S` |
| 名前を付けて保存 | `Ctrl+Shift+S` |
| タブを閉じる | `Ctrl+W` |
| 次のタブ／前のタブ | `Ctrl+Tab`／`Ctrl+Shift+Tab` |
| 元に戻す／やり直し | `Ctrl+Z`／`Ctrl+Y` |
| 検索／置換 | `Ctrl+F`／`Ctrl+H` |
| 次を検索 | `F3` |

検索・置換は選択中の文書が対象です。文字列検索に対応し、正規表現検索は未対応です。

## 外部送信・プライバシー

SYUAS本体には、編集中の文書や復元用コピー、利用状況を開発者のサーバーや外部サービスへ送信する機能はありません。HTMLへの変換も、同梱のAsciidoctor.jsを使って端末内で行います。

- **文書の処理**：編集・記法生成・HTML変換にクラウドサービスを使用しません。変換用スクリプトをCDNから取得することもありません。
- **プレビュー内の通信**：リモートURLの画像・includeなどの読み込み、外部リンクへの移動、文書内のスクリプト実行・フォーム送信を無効にしています。
- **利用状況・障害情報**：SYUAS独自のアクセス解析やテレメトリ、クラッシュ情報の送信処理はありません。
- **WebView2の設定**：SYUASのプレビューではSmartScreenと、クラッシュ情報のMicrosoftへの自動送信を無効にしています。

ただし、**WebView2 RuntimeやWindowsによる診断データの収集・送信まで、すべて停止するものではありません。** WebView2の任意の診断データはWindowsの診断設定に従い、必須の診断データはその設定にかかわらず収集されます。詳細はMicrosoftの[WebView2のデータとプライバシー](https://learn.microsoft.com/ja-jp/microsoft-edge/webview2/concepts/data-privacy)を参照してください。そのため、アプリと実行環境を含めて「外部通信が一切発生しない」と保証するものではありません。

WebView2を使用しない場合は、「ツール → 設定…」でHTMLプレビューを「作成しない」に設定できます。この設定ではプレビューを無効にし、WebView2を初期化しません。ローカルに保存されるデータや削除の範囲は、[データ保存設定](docs/data-storage-settings.md)を参照してください。

なお、利用者が共有フォルダーやクラウド同期対象のフォルダーを開く・保存する場合、そのアクセスや同期に伴う通信は各環境の仕組みに従います。

## 開発・ビルド

C#／.NET 8／WPFを使用し、MVVM構成で実装しています。ビルド・テストはWindows上で行ってください。.NET 8をターゲットにビルドできる.NET SDKが必要です。初回のパッケージ復元にはNuGetへの接続が必要です。

リポジトリを取得し、ルートフォルダーで次のコマンドを実行します。

```powershell
dotnet restore SYUAS.sln
dotnet build SYUAS.sln --no-restore
dotnet test SYUAS.sln --no-restore
dotnet run --project src/Syuas.App/Syuas.App.csproj --no-restore
```

Visual Studioを使用する場合は、.NETデスクトップ開発用の環境を用意して `SYUAS.sln` を開き、`Syuas.App` をスタートアッププロジェクトに指定します。

### 配布用ファイルの作成

Windows x64向けに、.NETランタイムを同梱しない構成で出力する例です。この構成での実行には、x64版の.NET 8 Desktop Runtimeが必要です。

```powershell
dotnet publish src/Syuas.App/Syuas.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish/win-x64
```

`artifacts/publish/win-x64` の内容をフォルダーごと配布します。`LICENSE`、`Licenses`、`PreviewAssets` はビルド設定により出力先へコピーされます。配布時もこれらを含めてください。

### プロジェクト構成

| パス | 内容 |
| --- | --- |
| `src/Syuas.Core` | モデル、AsciiDoc生成・表解析、文書管理、復元、検索、ViewModel |
| `src/Syuas.App` | WPF画面、AvalonEditアダプター、WebView2プレビュー、ダイアログ |
| `tests/Syuas.Tests` | 記法生成、表編集、ファイル操作、復元、WPF画面などのテスト |
| `tests/Syuas.RecoveryProbe` | 子プロセスを使った異常終了・復元の検証ツール |
| `docs` | 利用説明、設計、検証手順 |
| `samples` | 編集・入力補助・プレビュー・表の再編集用のサンプル |

AsciiDocの生成ロジックはUIから分離しています。AvalonEditへの直接依存はApp側に閉じ込め、Coreのモデル・生成処理を単体テストできる構成です。

実際のWebView2を使った追加の統合確認は、WebView2 Runtimeを利用できるWindows環境で実行します。結果のHTMLとPNGは指定フォルダーに出力されます。

```powershell
$env:SYUAS_WEBVIEW_SMOKE = Join-Path $PWD 'artifacts/webview-smoke'
try {
    dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --filter FullyQualifiedName~WindowTests
} finally {
    Remove-Item Env:SYUAS_WEBVIEW_SMOKE
}
```

## ドキュメント・サンプル

- [表の作成・再編集の使い方](docs/table-reediting-guide.txt)
- [自動復元と外部変更の使い方](docs/recovery-guide.txt)
- [データ保存設定](docs/data-storage-settings.md)
- [タブエディターの仕様](docs/tab-editor.md)
- [表の再編集の設計](docs/table-reediting.md)・[表デザイナーのUndo／Redo](docs/table-designer-history.md)
- [表の互換性検証](docs/table-compatibility-validation.md)・[障害検証](docs/failure-validation.md)
- サンプル：[基本編集](samples/phase1.adoc)・[入力補助](samples/phase2.adoc)・[セル結合](samples/phase3.adoc)・[文書構造とプレビュー](samples/phase4.adoc)・[表の再編集](samples/table-reediting/README.md)

[AGENTS.md](docs/AGENTS.md)には当初の要件と開発優先順位を記載しています。現在の機能・対応範囲は、このREADMEと各ドキュメントを参照してください。


## ライセンス・使用ライブラリ

SYUASは[MITライセンス](LICENSE)で提供するフリーウェアです。

Copyright (c) 2026 KUBOYAMA Kyota

ライセンスの条件に従って、商用・非商用を問わず利用、改変、再配布できます。著作権表示とライセンス表示を保持してください。本ソフトウェアは無保証で提供されます。条件の全文は `LICENSE` を参照してください。

外部ライブラリにはそれぞれのライセンスが適用されます。

| ライブラリ | 用途 | ライセンス |
| --- | --- | --- |
| AvalonEdit | ソースエディター | MIT |
| Asciidoctor.js | HTML変換 | MIT |
| Microsoft.Web.WebView2 SDK | HTMLプレビューの表示 | BSD 3-Clause |

バージョン・出典・ライセンス全文は、[外部ライブラリのライセンスと第三者通知](src/Syuas.App/Licenses/THIRD-PARTY.md)および[Asciidoctor.jsの同梱情報](src/Syuas.App/PreviewAssets/THIRD-PARTY.md)を参照してください。別途導入するWebView2 Runtimeには、SDKとは別の利用条件が適用されます。

アプリ内の「ヘルプ → SYUASについて → ライセンス…」からも、オフラインでライセンスと第三者通知を確認できます。
