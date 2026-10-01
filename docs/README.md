# SYUAS

AsciiDocソースを直接編集するWindowsデスクトップエディタです。AGENTS.mdのPhase 1を実装しています。

## 開発・起動

Windows 11、.NET 8 SDK以降（.NET 8をターゲットにビルドできるもの）、.NET 8 Desktop Runtimeが必要です。初回の復元にはNuGetへの接続が必要です。

```powershell
dotnet restore SYUAS.sln
dotnet build SYUAS.sln --no-restore
dotnet test SYUAS.sln --no-restore
dotnet run --project src/Syuas.App/Syuas.App.csproj --no-restore
```

Visual Studioからは `SYUAS.sln` を開き、`Syuas.App`をスタートアッププロジェクトに指定します。

## Phase 1の機能

- AvalonEditによるソース編集、行番号、Tab入力、複数行選択、コピー・切り取り・貼り付け、全選択。
- 新規作成、開く、保存、名前を付けて保存。対応拡張子は `.adoc` / `.asciidoc` / `.ad` / `.asc` / `.txt`。
- 最近使用したファイル（最大10件）と1ファイルのドラッグ＆ドロップによるオープン。
- 未保存表示と、新規作成・別ファイルを開く・終了時の保存確認。保存失敗やキャンセル時は編集内容を保持。
- Undo / Redo。保存した履歴位置までUndo / Redoすると未保存表示も解除。
- 文字列検索、先頭への折り返し、大文字小文字の区別、選択箇所の置換、すべて置換。すべて置換は1回のUndoで取り消し。
- カーソル行・列、UTF-8表示、行番号の表示切り替え、折り返し表示。
- 見出し、ブロック区切り、属性、include / image / link / xref、URL、Admonition、コメント、ソースブロックの簡易ハイライト。

| 操作 | ショートカット |
| --- | --- |
| 新規 / 開く / 保存 | Ctrl+N / Ctrl+O / Ctrl+S |
| 名前を付けて保存 | Ctrl+Shift+S |
| Undo / Redo | Ctrl+Z / Ctrl+Y |
| 検索 / 置換 | Ctrl+F / Ctrl+H |
| 次を検索 | F3（検索欄ではEnterも可） |
| 検索欄を閉じる | Escape |

置換は選択中の文字列が検索文字列と一致するときに実行し、次の一致箇所を選択します。一致しない場合は検索のみ行います。検索は正規表現ではなく文字列検索です。

UTF-8（BOM有無の両方）を読み込み、BOMなしUTF-8で保存します。既存の改行は維持します。UTF-8でないファイルは文字化けした状態で保存しないよう読み込みエラーにします。保存は同じフォルダーの一時ファイルに書き込んでから置き換えます。

最近使用したファイルは `%LOCALAPPDATA%/SYUAS/recent-files.json` に保存します。履歴ファイルが壊れている場合は空の履歴から起動します。履歴の保存失敗は文書の保存結果に影響しません。

## 構成

- `src/Syuas.Core`: エディタ非依存のインターフェース、MainViewModel、ファイル操作、履歴、検索。
- `src/Syuas.App`: WPF View、Windowsダイアログ、AvalonEdit Adapter、埋め込みXSHD。
- `tests/Syuas.Tests`: ファイル・検索・文書ライフサイクルのテスト、実際のAvalonEditでのUndo検証、ハイライトとWPF画面の読み込みテスト。WPFを使用するためWindows上で実行します。

AvalonEditへの参照はAppのみに置いています。Phase 2以降の記法生成モデルとGeneratorはCoreへ追加でき、`IEditorAdapter.BeginUpdate()` と `Replace()` で1回のUndoにまとまる編集ができます。

Phase 2の入力補助、Phase 3の表デザイナー、Phase 4のOutline / HTML Previewは未実装です。ハイライトは簡易ルールで、AsciiDocの完全な解析や埋め込み言語の構文解析は行いません。

## 手動確認

1. 新規文書に日本語と見出しを入力し、`.adoc` として保存・再オープンする。
2. 変更後の新規作成・オープン・終了で、保存 / 破棄 / キャンセルをそれぞれ確認する。
3. 検索と置換、すべて置換のUndo、保存位置までのUndo / Redoを確認する。
4. `.adoc` をエディタへドロップし、アプリを再起動して最近使用したファイルから開く。
5. `samples/phase1.adoc` でハイライト、Tab入力、コピー・貼り付け、行・列表示を確認する。

## 依存ライブラリ

エディタは [AvalonEdit 6.3.1.120](https://www.nuget.org/packages/AvalonEdit/6.3.1.120)（MIT License）を使用しています。
