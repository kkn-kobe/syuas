# AsciiDoc Editor 「SYUAS」 要件定義

## 1. アプリケーション概要

AsciiDoc形式の文書を編集するWindowsデスクトップアプリケーションを開発する。

本アプリケーションはWYSIWYGエディタではなく、AsciiDocソースを直接編集するテキストエディタを基本とする。

ただし、AsciiDoc特有の記法をユーザーが手入力する負担を軽減するため、表、見出し、画像、外部ファイル参照、リンク、コードブロックなど、一般的に利用するAsciiDoc記法についてGUIによる入力補助機能を提供する。

特に表については、行数・列数・セル結合数などをユーザー自身が計算してAsciiDoc記法を組み立てる必要がないよう、GUI上で表構造を作成し、その結果からAsciiDocを自動生成する。

---

# 2. 技術要件

## 2.1 基本技術

- 言語：C#
- ランタイム：.NET 8
- UI：WPF
- アーキテクチャ：MVVM
- 対象OS：Windows 11
- 文字コード：UTF-8を基本とする

## 2.2 エディタ

通常のWPF TextBoxではなく、コード／テキスト編集に適したエディタコンポーネントを利用する。

候補としてAvalonEditを想定する。

エディタには最低限以下を備える。

- 行番号表示
- Undo / Redo
- コピー・切り取り・貼り付け
- 全選択
- 検索
- 置換
- カーソル行・列表示
- Tab入力
- 複数行選択
- AsciiDocの簡易シンタックスハイライト

エディタコンポーネントへの依存はView層またはAdapter層に閉じ込め、AsciiDoc記法生成ロジックがエディタコンポーネントへ直接依存しない構造とする。

---

# 3. ファイル操作

以下を実装する。

- 新規作成
- 開く
- 保存
- 名前を付けて保存
- 最近使用したファイル
- ドラッグ＆ドロップによるAsciiDocファイルのオープン
- 未保存状態の検出
- 未保存の状態で終了／別ファイルを開く場合の確認

標準的な拡張子として以下を扱う。

- `.adoc`
- `.asciidoc`
- `.ad`
- `.asc`
- `.txt`

保存時は原則としてUTF-8を使用する。

---

# 4. 基本画面

基本画面は以下の構成とする。

```text
┌─────────────────────────────────────────────────────────┐
│ File Edit Insert Format View Help                       │
├─────────────────────────────────────────────────────────┤
│ New Open Save | Heading Table Image Include Link ...    │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  1  = Document Title                                    │
│  2                                                      │
│  3  == Overview                                         │
│  4                                                      │
│  5  本文...                                             │
│  6                                                      │
│                                                         │
│                                                         │
├─────────────────────────────────────────────────────────┤
│ Ln 5, Col 1                         UTF-8 | AsciiDoc     │
└─────────────────────────────────────────────────────────┘
```

中央領域はAsciiDocソースエディタとする。

---

# 5. 入力補助の基本思想

入力補助機能は、GUI内部に独自の文書形式を保持するのではなく、

「ユーザー操作 → AsciiDoc文字列生成 → 現在の文書へ挿入」

を基本とする。

例えば表挿入の場合は、

```text
TableDefinition
       ↓
AsciiDocTableGenerator
       ↓
AsciiDoc文字列
       ↓
現在のカーソル位置へ挿入
```

という構造とする。

画像、include、見出し、コードブロックなどについても同様に、それぞれのモデルからAsciiDoc文字列を生成する。

---

# 6. Insertメニュー

以下を実装する。

```text
Insert
 ├─ Heading...
 ├─ Table...
 ├─ Image...
 ├─ Include File...
 ├─ Link...
 ├─ Cross Reference...
 ├─ Anchor...
 ├─ Source Block...
 ├─ Admonition...
 ├─ List
 │   ├─ Unordered List
 │   ├─ Ordered List
 │   └─ Checklist
 └─ Block
     ├─ Listing Block
     ├─ Literal Block
     ├─ Quote Block
     └─ Example Block
```

頻繁に使用する機能についてはツールバーにも配置する。

---

# 7. 見出し挿入機能

## 7.1 機能

「Heading」を選択すると見出し入力ダイアログを表示する。

入力項目：

- 見出しレベル
- 見出し文字列

Level 1〜Level 5を選択可能とする。

例：

```text
Level: [2 ▼]

Title:
[システム構成________________]

                     [Cancel] [Insert]
```

生成例：

```asciidoc
=== システム構成
```

## 7.2 現在行への適用

現在行に既に文字列が存在する場合、

```text
システム構成
```

を選択またはカーソル配置してHeading Level 2を指定すると、

```asciidoc
=== システム構成
```

へ変更できるようにする。

既に見出し記号が存在する場合は、見出しレベルを変更する。

---

# 8. 表挿入機能

本アプリケーションで最も重要な入力補助機能の一つとする。

## 8.1 新規表作成

「Insert → Table」を選択すると表作成ダイアログを表示する。

入力項目：

- 行数
- 列数
- ヘッダー行の有無
- 表タイトル（任意）
- 各列の幅（任意）

初期値：

```text
行数：3
列数：3
ヘッダー：なし
```

## 8.2 表デザイナー

行列数を入力するとGrid形式で表を表示する。

```text
┌────────┬────────┬────────┐
│        │        │        │
├────────┼────────┼────────┤
│        │        │        │
├────────┼────────┼────────┤
│        │        │        │
└────────┴────────┴────────┘
```

Grid上では以下を可能とする。

- セル選択
- 複数セル選択
- セル結合
- 結合解除
- 行追加
- 行削除
- 列追加
- 列削除
- セルへの文字入力

## 8.3 セル結合

ユーザーがセル範囲を選択して「セル結合」を実行すると、AsciiDocのspan指定を自動計算する。

ユーザーはspan数を入力しない。

内部モデル例：

```csharp
public class TableCell
{
    public string Text { get; set; } = "";
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
}
```

例えば2列結合されたセルについては、AsciiDoc出力時に必要なcolumn span記法を自動生成する。

2列×3行の結合であれば、ColumnSpan=2、RowSpan=3から必要なAsciiDoc記法を生成する。

## 8.4 出力

例えば3列×2行の場合、

```asciidoc
[cols="3*"]
|===
|
|
|

|
|
|
|===
```

のようなAsciiDocを生成する。

ヘッダーを指定した場合は、明示的にheader optionを付与する。

例：

```asciidoc
[cols="3*",options="header"]
|===
|項目
|内容
|備考

|
|
|
|===
```

表の列数やセル結合に関する計算をユーザーへ要求しないこと。

---

# 9. 外部ファイル参照（include）挿入

「Insert → Include File」を選択するとダイアログを表示する。

## 9.1 基本項目

- ファイル
- パス指定方式
- オプション

「参照」ボタンからファイル選択ダイアログを開けるようにする。

基本的には現在編集中のAsciiDocファイルからの相対パスを生成する。

例：

現在のファイル：

```text
C:\Documents\manual\main.adoc
```

参照ファイル：

```text
C:\Documents\manual\chapter\overview.adoc
```

の場合、

```asciidoc
include::chapter/overview.adoc[]
```

を生成する。

Windows上でファイルを選択しても、AsciiDocへ出力するパス区切りは `/` を基本とする。

## 9.2 詳細オプション

詳細設定を展開すると以下を指定できるようにする。

- lines
- tag
- tags
- leveloffset
- indent
- encoding
- optional

第1版ではすべてを必須実装とせず、

- lines
- tag
- leveloffset

を優先する。

例：

```asciidoc
include::chapter/overview.adoc[leveloffset=+1]
```

---

# 10. 画像挿入

「Insert → Image」を選択すると画像挿入ダイアログを表示する。

入力項目：

- 画像ファイル
- Block / Inline
- Alt Text
- Title
- Width
- Height
- ID

ファイルは参照ボタンから選択できるようにする。

基本的には現在のAsciiDocファイルから画像ファイルへの相対パスを生成する。

Block Imageの生成例：

```asciidoc
image::images/system.png[System Architecture]
```

Inline Imageの場合：

```asciidoc
image:images/icon.png[Icon]
```

Titleが入力された場合には、必要なAsciiDoc記法を生成する。

---

# 11. リンク挿入

「Insert → Link」を選択するとダイアログを表示する。

入力項目：

- URL
- 表示文字列

例：

```text
URL:
https://example.com

Text:
Example
```

から、

```asciidoc
https://example.com[Example]
```

または適切なlink macroを生成する。

---

# 12. Cross Reference挿入

文書内または他のAsciiDoc文書へのxrefを挿入できるようにする。

入力項目：

- Target
- 表示文字列

例：

```asciidoc
xref:installation.adoc[インストール方法]
```

または文書内IDに対するxrefを生成する。

将来的には現在の文書を簡易解析し、既存の見出し／Anchor一覧からTargetを選択できるようにする。

---

# 13. Anchor / ID挿入

任意位置へAnchorを挿入できるようにする。

入力：

```text
ID:
system-architecture
```

から、適切なAsciiDoc記法を生成する。

Cross Reference機能と組み合わせて利用することを想定する。

---

# 14. Source Block挿入

「Insert → Source Block」を選択するとダイアログを表示する。

入力項目：

- Programming Language
- Source Text

Programming Languageは自由入力に加え、代表的なものを候補表示する。

例：

- C#
- C
- C++
- Java
- JavaScript
- TypeScript
- Python
- XML
- JSON
- YAML
- SQL
- Bash
- PowerShell

生成例：

```asciidoc
[source,csharp]
----
Console.WriteLine("Hello");
----
```

エディタ上で文字列が選択されている場合、「Source Block」を実行すると、その文字列をSource Blockで囲む。

---

# 15. Admonition挿入

以下に対応する。

- NOTE
- TIP
- IMPORTANT
- CAUTION
- WARNING

短い1段落の場合：

```asciidoc
NOTE: メッセージ
```

複数行・複数ブロックの場合はAdmonition Blockを生成できるようにする。

GUI上では、

```text
Type:
[NOTE ▼]

Text:
[________________________________]
[________________________________]

                     [Cancel] [Insert]
```

とする。

---

# 16. リスト入力補助

以下をサポートする。

- 箇条書き
- 番号付きリスト
- Checklist

選択された複数行についてリスト化できるようにする。

例えば、

```text
Apple
Banana
Orange
```

を選択して「Unordered List」を実行すると、

```asciidoc
* Apple
* Banana
* Orange
```

へ変換する。

インデント／ネストについては、第1版では基本的な単一レベルを対象とする。

---

# 17. インライン書式

Formatメニューおよびツールバーから以下を適用できるようにする。

- Bold
- Italic
- Monospace

文字列が選択されている場合はAsciiDoc記法で囲む。

文字列が未選択の場合は開始・終了記号を挿入し、その中央へカーソルを移動する。

例：

```text
重要
```

を選択してBold：

```asciidoc
*重要*
```

---

# 18. その他のBlock挿入

一般的に使用される以下のブロックについて入力補助を行う。

- Listing Block
- Literal Block
- Quote Block
- Example Block

基本動作は、

1. 現在選択されている文字列を取得
2. 対応するAsciiDocブロック記法で囲む
3. 未選択なら空ブロックを生成
4. ブロック内部へカーソルを移動

とする。

---

# 19. 選択文字列に対する共通動作

入力補助機能では、可能な限り現在の選択範囲を利用する。

例えば、

```text
Console.WriteLine("Hello");
```

を選択して「Source Block → C#」を実行した場合、その場でSource Blockへ変換する。

同様に、

- Bold
- Italic
- Monospace
- List
- Source Block
- Admonition
- 見出し

について、既存文字列へ後から記法を適用できるようにする。

単純にテンプレートを挿入するだけではなく、「既存文章への記法適用」を重視する。

---

# 20. カーソル位置制御

空の記法を挿入した場合、入力を続けやすい位置へカーソルを自動配置する。

例えばSource Block挿入後、

```asciidoc
[source,csharp]
----
<ここにカーソル>
----
```

とする。

リンクであれば表示文字列入力位置、見出しであれば見出し文字列位置など、次にユーザーが入力すると考えられる位置へ移動する。

---

# 21. Undo / Redo

GUIによる記法挿入は、原則として1回のUndo操作で元に戻せるようにする。

例えばTable挿入によって20行のテキストが追加された場合でも、Ctrl+Z 1回で表全体を削除できることが望ましい。

---

# 22. シンタックスハイライト

AsciiDocの主要記法について簡易的なシンタックスハイライトを行う。

対象：

- Document Title
- Section Title
- Block delimiter
- Attribute
- include
- image
- link
- xref
- Admonition
- コメント
- Source Block
- Table delimiter

完全なAsciiDoc Parserを実装する必要はない。

編集補助を目的とした簡易ハイライトでよい。

---

# 23. 文書構造表示

第2段階の機能として、現在のAsciiDoc文書から見出しを抽出し、左側にDocument Outlineを表示できるようにする。

例：

```text
Document
├─ Overview
├─ Installation
│  ├─ Requirements
│  └─ Setup
├─ Configuration
└─ Troubleshooting
```

項目をクリックすると該当行へ移動する。

文書構造解析については、AsciiDoc全体を完全に解析する必要はなく、まず見出し行を認識する簡易実装とする。

---

# 24. HTMLプレビュー

HTMLプレビューは初期必須要件とはしない。

将来的な拡張機能として、

```text
┌──────────────────┬──────────────────┐
│ AsciiDoc Source  │ Preview          │
│                  │                  │
│ == Title         │ Title            │
│                  │                  │
│ text             │ text             │
└──────────────────┴──────────────────┘
```

のような分割表示を可能にする。

AsciiDoc→HTML変換については独自実装せず、Asciidoctor等の既存処理系との連携を前提とする。

---

# 25. 非対象

第1版では以下を目的としない。

- 完全なWYSIWYG編集
- Microsoft Wordのようなページレイアウト編集
- AsciiDoc仕様全体を網羅するParserの独自実装
- AsciiDoc→HTML/PDF変換エンジンの独自実装
- 任意の既存AsciiDoc表を完全解析してGUI表へ逆変換する機能
- AsciiDocの全記法をGUIから設定可能にすること

AsciiDocを理解しているユーザーが「手書きすると面倒な記法」を効率よく入力できることを優先する。

---

# 26. 内部設計方針

AsciiDoc文字列生成処理をUIから分離する。

例えば以下のような構造を想定する。

```text
Models
 ├─ TableDefinition
 ├─ TableCell
 ├─ IncludeDefinition
 ├─ ImageDefinition
 ├─ HeadingDefinition
 ├─ LinkDefinition
 └─ SourceBlockDefinition

Services
 ├─ AsciiDocTableGenerator
 ├─ AsciiDocIncludeGenerator
 ├─ AsciiDocImageGenerator
 ├─ AsciiDocHeadingGenerator
 ├─ AsciiDocLinkGenerator
 └─ AsciiDocBlockGenerator
```

各GeneratorはUIに依存しないこと。

例えば、

```csharp
string Generate(TableDefinition definition);
```

のように、モデルを受け取りAsciiDoc文字列を返す純粋なロジックとして実装する。

これにより単体テストを容易にする。

---

# 27. テスト

AsciiDoc生成ロジックについて単体テストを作成する。

特に以下を重点的にテストする。

- 2×2表
- 大きな表
- Header付き表
- ColumnSpan
- RowSpan
- ColumnSpan + RowSpan
- 相対パスのinclude
- 親ディレクトリを参照するinclude
- 画像相対パス
- 見出しLevel 1〜5
- Source Block
- Admonition
- 選択文字列のBold / Italic / Monospace化

UI自動テストよりも、まずAsciiDoc生成ロジックの単体テストを優先する。

---

# 28. 第1版の完成条件

第1版では最低限、以下が実用可能な状態であること。

1. `.adoc`ファイルを開いて編集・保存できる。
2. 基本的なシンタックスハイライトが動作する。
3. 見出しをGUIから挿入できる。
4. 表をGUI上で行列指定して作成できる。
5. 表のセルをGUI上で結合できる。
6. セル結合を正しいAsciiDoc記法へ変換できる。
7. 外部ファイルを選択してincludeを生成できる。
8. 画像ファイルを選択してimage記法を生成できる。
9. リンク・xrefを挿入できる。
10. Source Blockを挿入できる。
11. NOTE / TIP / IMPORTANT / CAUTION / WARNINGを挿入できる。
12. 箇条書き・番号付きリストを作成できる。
13. Bold / Italic / Monospaceを選択文字列へ適用できる。
14. GUIによる挿入操作をUndoできる。

---

# 29. 開発優先順位

## Phase 1：エディタ基盤

- WPF / MVVMプロジェクト作成
- エディタ実装
- ファイルOpen / Save
- Undo / Redo
- 検索
- 基本シンタックスハイライト

## Phase 2：基本入力補助

- Heading
- Image
- Include
- Link
- Source Block
- Admonition
- List
- Inline Format

## Phase 3：表デザイナー

- TableDefinition
- Grid UI
- 行列追加削除
- セル編集
- セル結合
- 結合解除
- AsciiDoc生成

## Phase 4：拡張

- Document Outline
- Cross Reference候補表示
- Anchor候補表示
- 高度なinclude指定
- HTML Preview

---

# 30. UX上の最重要要件

本アプリケーションの目的は、AsciiDocを別の形式へ隠蔽することではない。

ユーザーは常にAsciiDocソースそのものを確認・編集できる状態とする。

一方、

- 「この記法は何だったか」
- 「`=`はいくつ必要だったか」
- 「表は何列だったか」
- 「このセルは何セル分結合するのか」
- 「画像やincludeのパスをどう書くのか」
- 「ブロックの区切り記号は何だったか」

といった、文書の本質ではない記号入力・記法暗記・数え上げ作業をアプリケーション側で代行する。

したがって本アプリケーションは、

**「AsciiDocをGUIで編集するアプリ」ではなく、「AsciiDocを書くためのIDE的なエディタ」**

として設計する。