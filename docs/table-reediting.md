# 表の再編集：読み戻しと対象範囲・置換（第1〜2段階）

第1段階ではCoreに表のParser、診断情報、検証付きのモデル構築用ファクトリを追加しています。第2段階では、文書内の対象表を探すLocatorと、文書状態を確認して置換するServiceを追加しています。再編集メニューとダイアログへの接続は第3段階で実装します。

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

第1段階ではソースの読み戻しと往復一致を検証します。再編集画面、Asciidoctor.jsによる表示結果との照合は後続段階の検証対象です。

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
