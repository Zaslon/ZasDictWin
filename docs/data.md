# データと設定ファイル

## 辞書ファイル

OTM-JSON を読み書きする（`Services/OtmJsonIo.cs`。保存は一時ファイル経由で差し替える）。

- 単語・例文ごとに元の JSON を保持し、編集した項目だけ書き戻す。`zpdicOnline`、`snoj`、`legend` など
  アプリが扱わないフィールドは素通しする
- 辞書依存の設定（`punctuations`・`ignoredPattern`）は辞書の `zpdicOnline` に書き戻す
- 並び順の既定は `eaoiuhkstcnrmpfgzdbv- `。設定で変えられる

### 例文

辞書ルート直下の `examples` 配列。ZasDict（Python 版）と同じ形で、相互に読み書きできる。

```json
{ "id": 4, "sentence": "Bei renkeu reeke n …", "translation": "りんごが好きな子供は…",
  "supplement": "", "tags": [], "words": [{ "id": 579 }],
  "offer": { "catalog": "zpdicDaily", "number": 4 } }
```

- 例文が 1 つも無い辞書には `examples` キー自体を書かない（他ツールとの差分を増やさないため）
- `words` には id だけ書き、見出し語は表示のたびに辞書から引く。指している単語を消すと `id:12` と表示される。
  例文側の参照は自動では外さない
- `offer.catalog` には API 名だけを保存する。表示名は `choices.json` の `ExampleCatalogs`
- 「自作」の例文は `offer.number` に例文自身の id を入れる（ZasDict と同じ）

## 設定ファイルと秘密情報

どれも `%APPDATA%\ZasDictWin\` に置く。

| ファイル | 中身 |
| --- | --- |
| `settings.json` | 辞書のパス、表示設定、画面の割り付け、GitHub モードの接続先（owner/repo/ブランチ/パス）。秘密情報は入れない |
| `choices.json` | 編集画面のプルダウンの選択肢（下記） |
| `zpdic_api_key` | ZpDIC Online の API キー |
| `github_token` | GitHub の Personal Access Token（Contents の読み書き権限） |
| `error.log` | 障害ログ（下記） |
| `WebView2\` | ブラウザのユーザーデータ |

Heksa フォント（`Fazik-regular.ttf`）はリポジトリに含めない。設定 → 表示・動作 から取り込む。

### choices.json

初回起動時に既定値で書き出される。書き換えると次回起動から反映される（ビルド不要）。
既定値は `Services/Choices.cs` にあり、ZasDict（Android 版）の `Const.kt` と同じ並び。

| キー | 中身 |
| --- | --- |
| `Pos` | 品詞。訳語ごとに 1 つ選ぶ（保存時の必須項目） |
| `ContentTypes` | 内容欄の種類。書いた順に追加ボタンが並び、各項目は 1 つまで |
| `Relations` | 関係名と対照になる関係名。設定 → 表示・動作 からも編集できる |
| `ExampleCatalogs` | 例文の出典。`Api` が ZpDIC Online に渡す名前、`Label` が表示名 |

- 空の配列・オブジェクトを書いた項目は既定値に戻す（選択肢が無いと選べなくなるため）。出典から「自作」を消しても戻す
- `発音記号`・`語源`・`自作` は処理に結び付いた値（`Services/Const.cs`）。`発音記号` は「特殊発音」タグの自動付与、
  `語源` はイジェール文字での描画に使う。選択肢から外すとその自動処理が働かなくなる
- 関係の対照表の既定値は ZasDictAndroid の `Const.RECIPROCAL_MAP` と同じ。保存済みの対照表が優先されるので、
  古い設定を引き継いでいる場合は 設定 → 表示・動作 の［既定に戻す］で入れ直す

### 障害ログ

キャッチできた例外は `error.log` にスタックトレースを追記し、アプリは落とさず画面内で案内する。
画面に出す先の無い失敗（設定の読み書き、Heksa フォントの読み込み、`ignoredPattern` の解釈）も、
既定値に戻った理由を追えるよう記録する。不具合の報告時にはこのファイルを添付する。

Markdown 描画が原因の PtsHost FailFast はキャッチできないため、ログに残らずプロセスが終了する。

## 語源欄の描き分け

語源欄は辞書の凡例が定める `{造語者/言語略称:単語|意味}` 記法で書かれている（実データに波括弧は無い）。
`Etymology.Split` がこれを解析し、イジェール語の語幹だけを Heksa で描く。

| 書き方 | 描き分け |
| --- | --- |
| `dos+icen` | 言語略称が無いので両方イジェール文字 |
| `cal/mo+aker` | `cal/`（造語者＝かりぐら）はラテン文字、`mo`・`aker` はイジェール文字 |
| `*nes+for\|足の重ねる所` | `*`（廃用語）と `\|` 以降の和訳はそのまま、語幹だけイジェール文字 |
| `ru:Кобальт` | 外来語源。`ru`・`a`・`r`・`en`・`de`・`zh`・`u` などはイジェール文字にしない |
| `i.a:kel` | `i` で始まる略称（i / i.a / i.s / i.k / i.t、実データにある i.r・i.o も）はイジェール語 |
| `a:>i.t:` | `+`（合成）と `>`（変化）が区間の区切り |

さらにイジェール文字にするのは `A-Z a-z ' -` が連続する範囲だけに絞っている。語源欄に和訳や引用文が
紛れ込んでいる語があり、仮名やキリル文字を巻き込まないため。

## 他の版との違い

- `dialects.py` の `titauini()` は 3 母音化の結果が次の行で破棄される。既定では同じ挙動を保ち、
  ツール画面のチェックボックス（`Dialects.FaithfulTitauini`）で 3 母音化を有効にできる
- `ignoredPattern` は前方・後方・完全一致でのみ見出し語から除去する。部分一致に適用すると、
  入力した記号が永久にヒットしなくなるため
