# 開発者向け

## ビルドとテスト

.NET 8 SDK が必要。Visual Studio 2022 なら `ZasDictWin.sln` を開いて実行できる。

```
dotnet build ZasDictWin.sln -c Release
dotnet run --project ZasDictWin.csproj
dotnet test ZasDictWin.sln
```

単体テスト（`Tests/`、xUnit）は画面の裁定（`Mediator/`）・入力の経路（`Root/`）・`Services/`・`Models/`・
`ViewModels/`・`Presenters/` を、窓を作らずに検査する。WPF の要素が要るテストは STA スレッドで走らせる（`Tests/Sta.cs`）。

- 実際の通信はしない。GitHub と ZpDIC Online の HttpClient は偽の応答を返すもの（`Tests/FakeHttp.cs`）に差し替える
- ファイルダイアログは使わない
- 利用者の `%APPDATA%\ZasDictWin`（`settings.json`・`choices.json`・トークン・`error.log`）には触れない。
  テストは起動時に置き場（`Services/AppDataDir.cs`）を一時フォルダへ向け直す。
  **`%APPDATA%` 下に新しいファイルを置くときは、必ず `AppDataDir` を通してパスを作ること**（直に組むとテストが実データを読み書きする）

### 配布用の実行ファイル

```
dotnet publish ZasDictWin.csproj -c Release
dotnet publish ZasDictWin.csproj -c Release -p:SelfContained=true   # ランタイムの無い環境向け
```

- 出力は `bin\Release\net8.0-windows\win-x64\publish\ZasDictWin.exe` の 1 個（`win-x64` 固定）。
  通常は約 2.1MB で .NET 8 デスクトップランタイムが必要。自己完結は約 67MB で、初回起動時の展開があるぶん遅い
- `WebView2Loader.dll` はネイティブ DLL なので、初回起動時に `%TEMP%\.net\ZasDictWin\` へ展開される。
  exe を消してもこの展開先は残る

### ビルド成果物の削除

```
dotnet msbuild ZasDictWin.csproj -t:Distclean
```

`bin\` `obj\` と展開先 `%TEMP%\.net\ZasDictWin\` をまとめて消す。`dotnet clean` は `publish\` を残すため、
古い exe が積み上がるのを避けるにはこちらを使う。

- **publish した exe も消える**。配る 1 個は先に別の場所へコピーするか、`-o` で `bin\` の外へ出しておく
- アプリを起動したままだと exe がロックされて消しきれない

### 依存パッケージ

`Microsoft.Web.WebView2`（ブラウザ用）だけを使う。実行には WebView2 ランタイムが必要で、Windows 10 以降なら
Microsoft Edge に同梱されていることがほとんど。無い場合は
[Evergreen ランタイム](https://developer.microsoft.com/microsoft-edge/webview2/) を導入する。
ランタイムが無くてもアプリは起動し、ブラウザのタブにエラーが出る。

## OBS 前提の実装制約

OBS の［ウィンドウキャプチャ］は対象ウィンドウの HWND だけを取り込むため、別ウィンドウとして生成される
UI 要素は配信に映らない。これを避けるため次の構成を取っている。

- `MessageBox` / `ContextMenu` / `ComboBox` のドロップダウンは使わない。確認・警告・単語ごとの操作メニューは
  メインウィンドウ内のオーバーレイ層に描く。確認ダイアログだけは枠に入らず、中央のモーダルとして重ねる
- `ToolTip` も配信に映らないので補助説明にだけ使う。それが唯一の手掛かりになる情報は画面上の文字や図形でも示す
  （未保存はヘッダのドット。タイトルの `*` はタスクバー・Alt+Tab 用で、枠を消したウィンドウの中には描かれない）
- 自前コントロール
  - `Views/SelectableText.cs`: 単語詳細の本文。読み取り専用の `TextBox` / `RichTextBox` を素の文字に見えるまで
    削ったもの（`TextBlock` は文字を選べないため）。標準の右クリックメニューは `Popup` なので出さない。
    ホイールは中の `ScrollViewer` が食うので親へ流し直す
  - `Views/DropDown.cs`: プルダウン。一覧を `Popup` ではなくウィンドウ最上段の `AdornerDecorator` に描く。
    `IsEditable` で一覧に無い値も打てる（単語エディタの関係名）
  - `Views/MenuButton.cs`: 階層メニュー。一覧は DropDown と同じく `AdornerDecorator` に描く。項目は
    `MenuActionSpec`（見出しと上げる Intent だけの入れ物）で、並びと有効・無効は `Mediator/AppMediator.cs` が組む
  - 検索モード・検索対象は `Themes/Theme.xaml` の `Chip` スタイルのトグル
- 標準の枠は消してある（`WindowStyle="None"` ＋ `WindowChrome`）。移動・最大化・最小化・閉じるは自前。
  ウィンドウの大きさは `settings.json` に覚える
- 単語ウィンドウ・単語数ウィンドウ・持ち出したタブの独立ウィンドウは意図的に別 HWND
- WebView2 は子 HWND なのでキャプチャに映るが、WPF より手前に描画される（airspace 問題）。窓全体を覆う
  確認ダイアログを開いている間は自動で隠す。他のタブとは枠を分け合うだけなので隠さない

ファイルダイアログ（開く／保存／フォント選択）だけは OS のウィンドウを使う例外で、配信には映らない。

## テーマの制約

配色は `Themes/Dark.xaml` と `Themes/Light.xaml`、コントロールの外見は `Themes/Theme.xaml` に置く。
キー名を変えるとオーバーレイ類まで壊れるため、調整は値の変更でおこなう。

- 起動時に `Views/ThemePalette.cs` が、設定の配色（OS に合わせる・ライト・ダーク）から配色の辞書を選び、
  外見の辞書より先に読み込む。App.xaml には書かない。並べて読み込むと、外見の辞書を読み込んだ時点で先に読んだ配色に
  結び付き、後から配色を差し替えてもスタイルに届かない。同じ理由で、`Theme.xaml` の中に色の値を書かない
  （同じ辞書の中の参照はビルド時に結び付く）。`Tests/ThemePaletteTests.cs` がこの 2 つを検査する
- `Dark.xaml` と `Light.xaml` は同じキーを持つ。片方にしか無いキーは、その配色のときだけ見つからずに落ちる
- 既定の TextBlock スタイルは文字色を本文色に固定する。ボタンの中身のように親の文字色を継がせたい TextBlock には
  空のスタイルを明示する（`Views/LocalizedText.cs` の `BuildTextBlock`）

- 影・ぼかし（`DropShadowEffect` などの `Effect`）は使わない。ビットマップ化されて文字がにじみ、キャプチャの
  画質が落ちる。奥行きは面の明暗と枠線で表現する
- hover / pressed は背景を塗り替えずヴェール（`HoverVeil` / `PressedVeil`。ダークは白・黒、ライトは黒）を重ね、
  Primary などの塗りを潰さない
- 角の丸みはカード `12` / ボタン・入力欄 `8` / chip・タグ・バッジ `6` の 3 段。長丸（`CornerRadius="999"`）は
  使わない（短い chip がほぼ真円になり、隣の部品と形が揃わない）
- ヘッダは幅 1280px で 1 行に収める。コードビハインドから文字列を代入するボタン（最大化・元に戻すなど）が
  あるので `Content` はプレーンテキストのままにする
- 入力欄の案内文字は `v:Placeholder.Text` 添付プロパティ。TextBox テンプレートが空のときだけ表示する
- `Views/StreamWindow.xaml`（単語ウィンドウ）と `Views/CountWindow.xaml`（単語数ウィンドウ）はテーマ適用外。
  背景色がクロマキー用の固定値になるため
- `TabItem` のカスタムテンプレートでは、ヘッダー用 `ContentPresenter` に必ず `ContentSource="Header"` を付ける。
  省略するとページ本体がヘッダー枠に描かれ、内側の ScrollViewer がスクロール不能になるうえ、FlowDocument が
  無限幅で Arrange されて PtsHost が FailFast（try/catch 不能）でプロセスを落とす
- オーバーレイ類の ScrollViewer は `HorizontalScrollBarVisibility="Disabled"` に統一する。横のはみ出しは
  折り返しと末尾省略で吸収する
- 暗黙の TextBlock スタイルは明示的な `Style` 指定で打ち消される。`Label` / `SectionHeading` などのキースタイル側で
  `TextWrapping="Wrap"` を宣言し直さないと、長い案内文の末尾が切れる
- 逆に `TextTrimming="CharacterEllipsis"` は `TextWrapping="NoWrap"` とセットで書く。既定が `Wrap` なので、
  書かないと省略記号が出ない。1 行に収めたい欄（見出し語の一覧、フッタのステータス、パス表示、案内文字）は両方を明示する
- ブラウザのステータス欄は長い URL の途中を `…` で省略する（`EllipsisMiddle`）

## GitHub モードの通信

- 読み取りは Contents API（Base64 で 1 ファイル取得。1MB を超える場合は、応答の sha で Git Data API の blob を生のまま取る。raw.githubusercontent.com の download_url は数 MB で 2 分を超えることがあるため、sha が無い応答に限って使う。通信のタイムアウトは 120 秒）
- 書き込みは Git Data API（ref → commit → tree → commit → ref 更新）。辞書 JSON と更新履歴 CSV を 1 本のツリーに
  積んで 1 回でコミットするので、片方だけ更新した中途半端なコミットにならない。ZasDictAndroid の
  `GitHubApiClient.commitFiles` と同じ組み立て方
- 基点は毎回ブランチ先端なので、ファイルの sha を覚えておく必要はない。他所が先に進めていた場合は
  ref の fast-forward 更新が失敗して安全に弾かれる
