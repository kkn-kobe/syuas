# 表の再編集：第1段階（読み戻し基盤）

第1段階ではCoreに表のParser、診断情報、検証付きのモデル構築用ファクトリを追加しています。文書内の対象表を探す処理、文書への置換、再編集メニューは後続段階で実装します。

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

この段階ではソースの読み戻しと往復一致を検証します。文書内の対象範囲、Undo、再編集画面、Asciidoctor.jsによる表示結果との照合は後続段階の検証対象です。
