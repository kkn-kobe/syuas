# 表の再編集（第1〜3段階）

第1段階ではCoreに表のParser、診断情報、検証付きのモデル構築用ファクトリを追加しています。第2段階では、文書内の対象表を探すLocatorと、文書状態を確認して置換するServiceを追加しています。第3段階で編集メニュー・右クリックメニューから既存の表デザイナーを開き、元の表を更新できるようになりました。

## 操作方法

1. 再編集したい表の中にカーソルを置くか、1つの表の中を選択します。
2. 「編集 → 表を再編集…」、またはエディタの右クリックメニューの「表を再編集…」を選びます。対象は現在のカーソル・選択範囲です。
3. 「表を再編集」画面でセル、行列、結合、タイトル、ヘッダー、列幅を修正します。読み戻した結合セルも編集できます。
4. 右側のAsciiDocプレビューを確認し、「適用」で元の表へ反映します。文書への保存はCtrl+Sで行います。

「キャンセル」、Escape、ウィンドウの閉じる操作は、元の文書を変更しません。デザイナーで修正しても、適用までは編集内容を文書や復元用コピーへ反映しません。変更せずに適用した場合も、本文・未保存状態・Undo／Redo履歴を維持します。反映した表はCtrl+Z 1回で元に戻せます。

現在対応していない表、複数の表にまたがる選択、表以外の場所で実行した場合は、理由と文書内の行・列を案内します。適用する内容を読み戻せない場合は、ダイアログの下部へ理由を表示し、入力内容を保持したまま修正できます。開いた後に元の文書が変わっていた場合も適用を中止します。この場合は必要な内容をプレビューからコピーしたうえでキャンセルし、表を開き直してください。

セル内の文字参照（例: `&#124;`）はソースの表記を保持します。「挿入 → 表…」は従来どおり新しい表を作成する操作です。

## API

```csharp
var result = AsciiDocTableParser.Parse(tableSource);
if (result.Succeeded)
{
    TableDefinition table = result.Definition;
    string regenerated = AsciiDocTableGenerator.Generate(table, result.NewLine)
        + (result.HasTrailingNewLine ? result.NewLine : "");
    // 成功時は、regenerated と tableSource が文字列として完全に一致する。
    // table は独立した編集可能モデル。既存の行列操作・結合・解除を利用できる。
}
else
{
    foreach (var diagnostic in result.Diagnostics)
    {
        // Code: 判定用のコード、Message: 日本語の説明
        // Line / Column: 渡した表ソース内での1始まりの位置
    }
}
```

Parserには1つの表全体を渡します。任意のタイトル行、属性行、開始区切り、本文、終了区切りを含めます。終了区切り直後の改行は0個または1個を許容し、`HasTrailingNewLine` に記録します。周囲の文章・Anchor・複数の表を含めた文書全体を渡すAPIではありません。

失敗時の `Definition` と `NewLine` はnullです。現在は最初に見つかった問題を診断として返します。通常の未対応・不正なソースでは例外を投げません。引数そのものがnullの場合はAPIの使用誤りとして `ArgumentNullException` を投げます。ファイルアクセス、include展開、エディタの操作は行いません。

## 対応形式

```asciidoc
.項目一覧
[cols="2,1",options="header"]
|===
|項目
|説明

|名前
|本文
|===
```

- 現在の `AsciiDocTableGenerator` の形式を対象にします。属性の順序や区切りの空白も現行の生成形式に合わせます。
- 列指定は `cols="N*"` または正の整数の列幅リスト。列数は1〜50列です。
- `options="header"` または `options="noheader"` が必須です。暗黙のヘッダー推論は行いません。
- セル開始は `|`、`N+|`、`.N+|`、`N.N+|`。セルの開始記号はそれぞれ行頭へ記載します。
- 行数は占有範囲から求め、1〜100行です。縦結合でセル開始記号が存在しない行も復元します。
- 行末コードはLFまたはCRLFです。混在や単独CRは診断を返します。
- セル内の文字列はAsciiDocソースのまま扱います。文字参照のHTMLデコードやインライン記法の変換はしません。

例えば、新規デザイナーで入力した `A|B` は生成時に `A&#124;B` になります。読み戻したモデルには `A&#124;B` を格納します。入力時の文字列への逆変換ではなく、現在のソースの保持を保証します。再生成しても文字参照が二重にエスケープされないことをテストしています。

## 空白と結合の復元

セル順序・列数・結合数から各マスの占有状態を作ります。次のセルは次の未占有マスへ配置し、横方向の範囲超過、結合の重複、未充填マス、ヘッダーをまたぐ縦結合を検出します。

空行を行数の根拠にはしません。セルの開始行が変わる箇所では、Generatorが挿入した行区切りの空行を1つだけ取り除き、それ以外の空白・改行は本文として保持します。セル本文の `Trim()` は行いません。

解析後は同じGeneratorで再生成し、入力文字列と完全一致することを確認します。不一致の場合は `NonCanonicalLayout` を返し、モデルを公開しません。構文の明示的な検証も先に行うため、往復一致だけを対応可否の根拠にはしていません。

## 未対応の入力

次の入力は自動修復・正規化せずに診断を返します。

- 追加の表属性、属性の省略、CSV／DSV、異なる区切り文字。
- 配置・セルスタイル・繰り返し指定、1行に複数セルを書く形式。
- セル内の生のパイプやバックスラッシュでエスケープされたパイプ。現行Generatorは文字参照を出力します。
- include・条件分岐ディレクティブ。現行デザイナーで文字として入力した場合も、読み戻しでは展開しません。
- 入れ子の表に必要なAsciiDocセルスタイル。
- 欠けた区切り、範囲外の結合、不足セル、サイズ上限を超える表。
- 余分な属性空白やゼロ埋め数値など、再生成で表記が変わる形式。

同じ形式を維持して直接ソースを編集した表も読み戻せます。SYUASで作成したことを示す専用コメントや、外部管理ファイルは不要です。任意のAsciiDoc表を完全解析する機能ではありません。

## モデル構築用ファクトリ

`TableDefinition.FromCells(rows, columns, cells, title, hasHeader, columnWidths)` は、座標・span・本文を持つ `TableCellDefinition` の列挙から独立したモデルを作ります。

入力セルと列幅をコピーし、本文の連結や空白の変更をせずに、全マスの被覆、重複、サイズ、タイトル、列幅、ヘッダー境界を検証します。失敗時は `ArgumentException` を投げ、部分モデルは返しません。解析のためにGUI操作用の `Merge()` を呼ばない構成です。

## 検証

```powershell
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter "FullyQualifiedName~TableParserTests|FullyQualifiedName~TableImportTests"
dotnet test SYUAS.sln --no-restore
```

追加テストは65件です。通常表、ヘッダー・タイトル・列幅、全体結合、複数の結合が並ぶ表、100行×50列、80通りの固定乱数による結合配置、本文・改行・空白・文字参照の保持、不正入力の診断、モデルの独立性と編集操作を確認します。

第1段階ではソースの読み戻しと往復一致を検証します。Asciidoctor.jsによる表示結果との照合と互換性検証の拡充は第4段階の検証対象です。

## 第2段階：対象範囲と置換

### 表の範囲を調べる

```csharp
var located = AsciiDocTableLocator.Locate(documentSource, selectionStart, selectionLength);
if (located.Succeeded)
{
    TableSourceRange range = located.Range;
    // StartOffset / Length: エディタと同じUTF-16単位の位置と長さ
    // StartLine: 文書内の1始まりの行番号
    // OriginalSource: タイトル・属性・開始区切りから終了区切りまで
    // NewLine: 表が使用する改行（混在などは続くParserで検証）
}
```

`selectionLength` が0の場合はカーソル位置として扱います。空の文書や不正な範囲ではモデルを返さず診断を返します。直前・直後の表を推測して選ぶ処理はありません。

- タイトル、属性、開始／終了区切り、セル本文の中から同じ表を選べます。
- 1つの表の内部の選択、表全体の選択に対応します。行単位の選択では終了区切り直後の改行1つを含めることもできます。
- 置換範囲には終了区切り直後の改行を含めません。文書末尾の改行の有無や周囲の空行を維持します。
- 表の前に独立した行で付いている `[[id]]`、`[[id,参照文字列]]`、`[#id]` とコメントを置換範囲の外に残します。
- 複数の表にまたがる選択や、表の前後の文章を含む選択は拒否します。
- 終了区切りが欠けている場合、次の表の属性行に続く開始区切りを、現在の表の終了区切りとして取り込みません。

位置特定は安全側に限定しています。ソース・リテラル・コメント・パススルー・フェンスの各ブロック、例示・引用・サイドバー・オープンブロック、字下げされたリテラル段落、条件分岐内の表は対象外です。include先は読み込みません。

表の前の段落と空行で分離されていない場合、区切りのないsource／literal段落などとの区別を推測せず、空行を置く案内を返します。リスト継続の `+` に接続された表は空行があっても対象外です。表タイトルと属性の間にAnchorがある場合や、複数の属性行が付く場合も対応関係を推測しません。終了区切りの後に属性なしでセルと思われる行が続く場合や、最後のセルが別表の属性行に見える行で終わる場合は、曖昧な区切りとして扱います。

### 編集の開始と適用

```csharp
var editing = new TableEditingService(editorAdapter, () => mainViewModel.Session);
var started = editing.BeginEdit();
if (started.Succeeded)
{
    var context = started.Context;
    // context.Definition は元の文書から独立した作業用モデル。
    // キャンセルはcontextを破棄するだけ。エディタに変更はない。
    context.Definition.CellAt(0, 0).Text = "変更後の内容";
    var applied = editing.Apply(context);
    // Applied: 置換成功 / Unchanged: 元と同じため書き換えなし
    // Rejected: 理由はDiagnosticに格納。文書は変更しない。
}
```

`BeginEdit()` と `Apply()` はエディタを所有するUIスレッドから呼びます。ダイアログの表示やファイル保存はServiceの責務に含めません。

編集開始時に、文書ID、本文リビジョン、置換範囲と元のソースを保持します。適用時には次の条件を確認します。

1. Contextが同じServiceで取得されたこと。
2. 文書IDと本文リビジョンが取得時と一致すること。
3. 元の範囲が現在も有効で、ソースが文字列として一致すること。
4. 編集したモデルから生成したソースがParserで読み戻せ、Locatorでも全体を1つの表として特定できること。

表外の編集もリビジョン変更として検出します。編集後にUndoして同じ本文へ戻っても、古いContextの適用は拒否します。新規作成・別文書への切り替えでも拒否します。カーソル移動や、本文を変更しない保存・別名保存だけであれば、Contextは有効です。

変更なしの場合は `Replace` やカーソル移動を行いません。未保存フラグ、リビジョン、選択、既存のUndo／Redoを維持します。変更時には `BeginUpdate()` 内の1回の `Replace()` で対象範囲だけを置換し、1回のUndo／Redoで表の更新を取り消し・再実行できます。適用後のカーソルは先頭セルの本文開始位置へ移します。

文書IDやファイルの保存基準をServiceから更新しません。通常の本文変更イベントを通じて既存の文書管理へ通知され、保存と外部変更チェックは従来の経路を使用します。適用だけでディスク上のファイルを上書きすることはありません。

### 診断とテスト

Locatorと編集Serviceは `TableEditDiagnostic` を返します。`Code` は対象外・範囲の曖昧さ・文書変更などの分類、`Message` は日本語の理由、`Line`／`Column` は文書内の1始まりの位置です。表の解析失敗では `ParseCode` にParserの診断コードを保持し、行番号を文書内の位置へ変換します。文書全体に関する診断の位置は既定で1行1列です。

```powershell
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter "FullyQualifiedName~TableLocatorTests|FullyQualifiedName~TableEditingTests"
```

第2段階の追加テストは67件です。範囲・Anchor・改行の保持、複数表、コード例・複合ブロック・条件分岐の除外、未完了の表と後続表の分離、不正選択、実際のAvalonEditでのUndo／Redo、キャンセル・変更なし、古いContextの拒否、保存基準の維持、編集結果の再特定を確認します。

## 第3段階：画面への統合

`MainViewModel.EditTableCommand` が `TableEditingService.BeginEdit()` を呼び、取得したモデルを `ITableEditingDialogs` 経由で表示します。WPF側の `TableEditingDialogs` は所有者付きのモーダルダイアログとして既存の `TableDesignerDialog` を開きます。

`TableDesignerViewModel` は新規作成／再編集の2つのモードを持ちます。再編集ではタイトルを「表を再編集」、確定ボタンを「適用」に切り替え、対象の開始行と文字参照の扱いを案内します。`ConfirmCommand` で適用コールバックを呼び、成功・変更なしの場合だけ `CloseRequested` を通知します。拒否された場合は画面内に診断を表示し、ダイアログを閉じません。従来の `InsertCommand` は同じコマンドの別名として維持しています。

再編集の実行中や復元処理中は再編集コマンドを無効にします。表デザイナーが開いている間の新規作成・文書を開く・保存・再読み込み・復元・終了による文書切り替えも抑止します。監視と自動退避は元の文書について継続し、デザイナーを閉じるとエディタへフォーカスを戻します。

右クリックメニューは独立したWPFの表示ツリーになるため、`PlacementTarget.DataContext` を明示して同じコマンドに接続しています。開始時の失敗は通常の案内ダイアログ、適用時の失敗は表デザイナー内に表示します。

```powershell
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter "FullyQualifiedName~TableEditingUiTests|FullyQualifiedName~WindowTests"
```

第3段階では14件のコマンド・ViewModel統合テストを追加し、既存のWPF画面テストも拡張しています。メニューと右クリックのバインディング、新規作成モードの互換性、既存モデルの表示、適用・キャンセル・変更なし、エラー後の修正、古い文書への適用拒否、再入防止、復元処理中の無効化、Undo／Redoを確認します。実際のモーダルダイアログは透明なウィンドウで開き、適用失敗では閉じず、再試行成功で閉じることを検証します。

画面を画像化して確認する場合は、以下の環境変数に出力先の絶対パスを指定します（出力先のフォルダーは先に作成してください）。

```powershell
$env:SYUAS_TABLE_EDIT_SCREENSHOT = Join-Path $PWD '.local/table-edit.png'
$env:SYUAS_TABLE_EDIT_ERROR_SCREENSHOT = Join-Path $PWD '.local/table-edit-error.png'
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter FullyQualifiedName~WindowTests
Remove-Item Env:SYUAS_TABLE_EDIT_SCREENSHOT
Remove-Item Env:SYUAS_TABLE_EDIT_ERROR_SCREENSHOT
```
