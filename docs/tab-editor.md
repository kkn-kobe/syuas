# 基本タブ機能の実装・検証

2026-10-01の実装記録です。

## 構成

- MainViewModel が Documents / ActiveDocument、新規・開く・閉じる・すべて保存、共有の検索条件・最近使用したファイル、自動復元を管理します。
- DocumentTabViewModel が1文書の DocumentSessionController、保存競合処理、入力補助、表再編集、文書構造、外部変更監視を保持します。
- DocumentEditorView はタブが閉じられるまで同じ AvalonEdit / AvalonEditAdapter を保持します。切り替えに Load は使わず、表示状態と入力先を変更します。スクロール位置は再表示後に復元します。
- RecoveryService.TrackDocument / TickDocumentAsync は文書ID別に退避時刻・リビジョンを管理します。全タブで同じストアと直列キューを共有し、RemoveDocumentAsync は対象文書だけを整理します。
- 復元候補と開いている文書のIDが衝突した場合は、新しいIDで復元コピーを確定してから元候補を整理します。
- プレビューは共有し、描画ごとに異なるローカルリソースのホストを使います。古い描画の画像・include要求が新しい文書フォルダーを参照することを防ぎます。

## 保存・終了

保存先は正規化した絶対パスで比較します。同じパスを開いた場合は既存タブを選択し、別タブの保存先への上書きは止めて保存先を選び直します。保存競合ダイアログからの別名保存にも同じ確認を適用します。

終了確認ではタブを削除せず、全文書の保存／破棄が確定してから復元用コピーを整理します。途中のキャンセルでは全タブが残り、成功済みの保存は維持します。個別に閉じたタブではイベント購読、監視、Adapterを解放します。

## 検証結果

- 通常テスト: 543件成功（WorkspaceTests の17件を含む）。
- Releaseビルド: 警告0、エラー0。
- WPF画面テスト: タブ追加・選択の連動、エディタインスタンスの保持、スクロール位置の復元を確認。
- 実WebView2: include・画像、連続更新、文書切り替え、旧リソースURLの拒否を確認。専用の一時プロファイルを使用。
- .local/tabs-validation/tabs.png を目視確認。

WebView2の実行はサンドボックス内では初期化待ちがタイムアウトしたため、実行制約外で確認しました。通常テストだけではWebView2の検証は実行しません。

~~~powershell
dotnet test SYUAS.sln --no-restore
dotnet build SYUAS.sln --no-restore -c Release

$env:SYUAS_WEBVIEW_SMOKE = Join-Path $PWD '.local/tabs-validation/preview'
$env:SYUAS_TABS_SCREENSHOT = Join-Path $PWD '.local/tabs-validation/tabs.png'
$env:WEBVIEW2_USER_DATA_FOLDER = Join-Path $PWD ('.local/tabs-validation/webview-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force .local/tabs-validation | Out-Null
dotnet test tests/Syuas.Tests/Syuas.Tests.csproj --no-restore --filter FullyQualifiedName~WindowTests
~~~

別ウィンドウへの分離、正常終了後のタブ構成復元は今回の対象外です。ファイルの重複判定はパス文字列に基づき、ハードリンクなど異なるパスが同じ実ファイルを指す場合の同一性までは判定しません。

## ドラッグによるタブ並べ替え（2026-10-01追記）

タブ見出しを左ボタンでドラッグすると挿入位置を青い線で表示します。見出しの前半で前、後半で後へ挿入します。左右端では横スクロールし、先頭・末尾にも移動できます。移動距離がOSのドラッグ開始距離を超えるまでは通常のクリックとして扱い、閉じるボタンからはドラッグを開始しません。

順序を変えるのはドロップが確定した時点です。Esc、領域外へのドロップでは順序を保持します。ファイルドロップは従来どおり新しい文書を開きます。別ウィンドウからのタブ移動は受け付けません。

MainViewModel.MoveDocument は元の一覧の挿入境界（0〜Count）を受け取り、ObservableCollection.Moveで並べ替えます。文書・エディタを作り直さず、選択中の文書、本文、Undo履歴、保存基準、復元用コピーを維持します。相対タブ移動、すべて保存、終了確認は新しい一覧順序に従います。

検証: 通常テスト545件成功。WPF画面上の配置を使い、前後への挿入、順序確定前の表示、範囲外のドロップ拒否、選択とスクロール位置の保持、閉じるボタンの除外、端での自動スクロールを確認しました。OSの実マウス入力によるドラッグやEsc操作の手動検証は含みません。

SYUAS_TAB_REORDER_SCREENSHOTをPNGの出力先に設定してWindowTestsを実行すると、挿入線の表示を出力できます。
