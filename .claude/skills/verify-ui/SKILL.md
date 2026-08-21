---
name: verify-ui
description: WPF アプリを実際に起動して動作確認する。テスト用ライブラリを作り、設定を退避してから起動し、画面を PNG に撮って、終了後に設定を戻す。「動作確認して」「起動して確かめて」「UI を見て」「画面を撮って」などで使う。UI に触る変更をしたら毎回実施する。
---

`dotnet build` と `dotnet test` が通っても、XAML のリソース参照や DI の登録漏れは実行するまで
表に出ない。View / XAML / テーマは単体テストの対象外なので、UI に触ったらここを通す。

**やってはいけないことが 2 つある。** どちらも [verify-ui.ps1](verify-ui.ps1) が面倒を見るので、
手で `MusicTagAuditor.App.exe` を叩かずにこのスクリプトを使う。

1. **実ライブラリで動かさない。** 押し間違い 1 回で所蔵のタグが書き換わる
2. **`settings.json` を戻し忘れない。** テスト用ライブラリを開いた時点で `lastLibraryRoot` が
   書き換わる。戻さないと、次に利用者が起動したとき消えたパスを開きにいく

背景と実測値は [docs/manual_verification.md](../../../docs/manual_verification.md)。

## 使い方

起動できることだけを確かめる。UI に触ったら最低限これは通す。

```powershell
pwsh -NoProfile -File .claude/skills/verify-ui/verify-ui.ps1 -Build
```

ボタンを押し、タブを切り替えて、それぞれの画面を撮る。ボタン → タブの順に実行する。

```powershell
pwsh -NoProfile -File .claude/skills/verify-ui/verify-ui.ps1 -Click 検査 -Tab ファイル一覧
```

**明細の行を選ぶ・ダイアログを押す導線は `-Steps` を使う。** `-Click` はボタンしか押せず、
「行を選んでからボタン」という形の機能（「このアルバムの扱いを決める」「作品を辞書に追加」）を
素通りしてしまう。

```powershell
pwsh -NoProfile -File .claude/skills/verify-ui/verify-ui.ps1 -Steps click:検査,change:1,click:このアルバムの扱いを決める,dialog:キャンセル
```

| 手順 | すること |
|---|---|
| `click:検査` | メインウィンドウのボタンを押す。**ダイアログが開いたら自動で撮り、文言も書き出す** |
| `rule:1` | ルール一覧の n 行目を選ぶ（1 始まり） |
| `change:1` | 明細の n 行目を選ぶ（1 始まり） |
| `dialog:キャンセル` | 開いているダイアログのボタンを押す |
| `tab:ファイル一覧` | タブを選ぶ |
| `shot:任意の名前` | その時点を撮る |

**開いたダイアログは `dialog:` で必ず閉じる。** 閉じ忘れるとスクリプトが名指しで落ちる
（閉じないと `CloseMainWindow` が効かず、強制終了になって後始末が読めなくなる）。

| 引数 | 既定 | 用途 |
|---|---|---|
| `-Build` | 無し | 起動前に `dotnet build` を走らせる |
| `-Click` | 無し | 押すボタンの表示名。カンマ区切りで複数可 |
| `-Tab` | 無し | 選ぶタブの表示名。カンマ区切りで複数可 |
| `-Steps` | 無し | 手順を並べて実行する。上表の書式。`Steps` → `Click` → `Tab` の順 |
| `-Reuse` | 無し | テスト用ライブラリを作り直さない。2 回目以降は速いが、前回の適用結果が残る |
| `-SourceLibrary` | `settings.json` の `lastLibraryRoot` | コピー元 |
| `-TestLibrary` | `%TEMP%\MusicTagAuditor-testlib` | テスト用ライブラリの置き場 |
| `-OutDir` | `%TEMP%\MusicTagAuditor-verify` | PNG と設定バックアップの出力先 |
| `-StartupWaitSeconds` | 7 | 起動からスキャン完了までの待ち |
| `-ActionWaitSeconds` | 4 | 1 操作ごとの待ち |

## 結果の読み方

- **`起動 OK` が出ない**なら、起動時例外で落ちている。**ログには残らないことがある**ので、
  出力の `exitcode` を見る。ログは `%LOCALAPPDATA%\MusicTagAuditor\logs\` の当日分
- **ボタン名が違う**と、実在する候補を並べて落ちる。その一覧から正しい名前を選び直す
- **`設定を復元:` に元のライブラリのパスが出ていること**を必ず確認する。ここが出ていなければ
  利用者の設定が書き換わったままなので、`$OutDir\settings.json.bak` から手で戻す
- 撮った PNG は Read ツールで開いて目視する。**DataGrid の行の中身は UI Automation から
  読めない**（`AutomationProperties.Name` 未設定のため型名が返る）ので、セルの値は画像で確かめる
- **ダイアログの文言は出力にそのまま出る。** `[Text]` / `[RadioButton]` / `[Button]` の行が
  それ。TextBlock は Name として読めるので、注意書きを変えたときの確認は画像より確実
- **`ダイアログが開いた:` が出たら、そこから先はメインウィンドウを撮っていない。** 続けて
  操作するには `dialog:` を使う

## 自分でやる範囲

起動確認・画面の目視・クリック・タブ切替・行の選択・ダイアログの操作までは自分でやる。
**起動確認ごと丸投げしない。**
委譲してよいのはドラッグ操作・実ライブラリでしか出ない事象・主観的な見え方の判断の 3 つだけ
（[docs/manual_verification.md](../../../docs/manual_verification.md)）。依頼するときは、
**自分で確認できたこと**と**確認できていないこと**を分けて書く。

## Gotchas

- **`-Click` に「チェックした項目を適用」を渡すとタグが書き換わる。** テスト用ライブラリの
  コピーが対象なので所蔵は無事だが、**バックアップ先は利用者の設定を共有している**
  （既定で `D:\music backup`）。検証のバックアップが実運用の履歴に混ざる
- **辞書を書き換える検証に入る前に、ログで実体かどうかを確かめる。**
  `%LOCALAPPDATA%\MusicTagAuditor\logs` の `辞書を読み込んだ … 個別例外=N` を利用者の実際の
  件数と突き合わせる。**食い違っていれば写しなので書き換えてよく、一致していたら実体なので
  やらない**（理由は [docs/manual_verification.md](../../../docs/manual_verification.md)）。
  写しの側は汚れるので、足した項目は検証のあと**足した 1 件だけ**を消す
- **`pwsh -File` では配列引数が束縛されない。** `-Click A,B` も `-Click 'A','B'` も 1 個の
  文字列として渡ってくる。スクリプト側でカンマで割っているので気にしなくてよいが、
  **手順名・ボタン名にカンマは使えない**
- **テスト用ライブラリは m4a と flac を 1 フォルダずつ拾うだけ。** 特定の検出を再現したい
  ときは、自分でフォルダを組んで `-TestLibrary` と `-Reuse` で開く。実ライブラリからの
  コピーは読み取りだけなので所蔵は動かない
- **`%APPDATA%` / `%LOCALAPPDATA%` は差し替えられない。** `Environment.GetFolderPath` は
  シェル API を見るため、環境変数を上書きしても実際のパスが使われる。設定・辞書・ログを
  分離する道は無い
- **`-TestLibrary` をリポジトリ内にしない。** 音源がコミット候補に入る。コピー元と同じか
  その配下を指した場合はスクリプトが起動前に止める
