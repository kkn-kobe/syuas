# SYUAS

AsciiDocソースを直接編集するWindowsデスクトップエディタです。AGENTS.mdのPhase 1・Phase 2を実装しています。

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

## Phase 2の入力補助

「挿入」「書式」メニューとツールバーから使用します。ダイアログの生成結果は入力に合わせて更新され、不正な値がある場合は挿入できません。

| 機能 | 動作 |
| --- | --- |
| 見出し | Level 1〜5を選択（`==`〜`======`）。現在行の文字列を取り込み、既存の見出しレベルも変更可能 |
| 画像 | ファイル参照、Block / Inline、Alt Text、Title、Width、Height、ID、相対 / 絶対パス |
| Include | ファイル参照、相対 / 絶対パス。詳細設定でlines・tag・leveloffsetを指定 |
| Link / xref / Anchor | URLや参照先、表示文字列、IDを入力。参照先の候補表示はPhase 4の対象 |
| Source Block | 選択文字列を取り込み、言語を選択または自由入力。C#はcsharp、C++はcppへ変換 |
| Admonition | NOTE / TIP / IMPORTANT / CAUTION / WARNING。複数行はブロック形式、単一行でもブロック形式を指定可能 |
| List | 箇条書き・番号付き・チェックリスト。現在行または選択範囲に含まれる行全体へ適用 |
| Inline Format | Bold / Italic / Monospace。未選択なら空の記号対を挿入し、中央へカーソルを配置 |
| その他のBlock | Listing / Literal / Quote / Example。選択文字列を囲み、未選択なら空ブロックを挿入 |

ダイアログのキャンセルは文書を変更しません。各入力補助は1回のUndoで取り消せます。ブロック挿入時には前後の本文との間に必要な空行を補い、文書のLF / CRLFを引き継ぎます（改行のない新規文書はWindowsのCRLF）。既存テキスト内に区切り行がある場合は、外側の区切りを長くして衝突を避けます。

画像・includeの相対パスは保存済み文書のフォルダーが基準です。文書が未保存、または別ドライブのファイルの場合は絶対パスを生成します。未保存文書で相対パスを使う場合は、先に文書を保存してください。画像の `imagesdir` 属性やincludeで変更された属性などの文書解析は行わないため、そのような文書では生成後のパスを必要に応じて調整してください。

## 構成

- `src/Syuas.Core`: エディタ非依存のモデル・Generator、ViewModel、ファイル操作、履歴、検索、挿入範囲・改行・カーソル位置の制御。
- `src/Syuas.App`: WPF View、Windowsダイアログ、入力フォーム、AvalonEdit Adapter、埋め込みXSHD。
- `tests/Syuas.Tests`: ファイル・検索・文書ライフサイクル、記法生成、入力検証、実際のAvalonEditでの挿入・Undo、WPF画面・ダイアログの読み込みとバインディングのテスト。Windows上で実行します。

AvalonEditへの直接参照はAppに閉じ込めています。各Generatorはモデルから文字列を生成し、`EditorInsertionService` が `IEditorAdapter.BeginUpdate()` と `Replace()` で1回のUndoにまとまる編集を行います。テストはApp経由で実際のAvalonEditも検証します。

Phase 3の表デザイナー、Phase 4のOutline / HTML Previewは未実装です。ハイライトは簡易ルールで、AsciiDocの完全な解析や埋め込み言語の構文解析は行いません。

## 手動確認

1. 新規文書に日本語と見出しを入力し、`.adoc` として保存・再オープンする。
2. 変更後の新規作成・オープン・終了で、保存 / 破棄 / キャンセルをそれぞれ確認する。
3. 検索と置換、すべて置換のUndo、保存位置までのUndo / Redoを確認する。
4. `.adoc` をエディタへドロップし、アプリを再起動して最近使用したファイルから開く。
5. `samples/phase1.adoc` でハイライト、Tab入力、コピー・貼り付け、行・列表示を確認する。
6. `samples/phase2.adoc` の文章を選択して見出し・リスト・書式・ソースブロックを適用し、Ctrl+Zで戻す。
7. 保存済み文書から画像・includeの参照ボタンでファイルを選び、相対パスと生成結果を確認する。未保存文書では絶対パスになることも確認する。
8. 空のリンク表示文字列・空のソースブロックを挿入し、カーソル位置を確認する。入力ダイアログのキャンセルで文書が変わらないことを確認する。

## 依存ライブラリ

エディタは [AvalonEdit 6.3.1.120](https://www.nuget.org/packages/AvalonEdit/6.3.1.120)（MIT License）を使用しています。
