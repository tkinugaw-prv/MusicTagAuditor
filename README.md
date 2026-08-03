# musicTagger

クラシック音楽ライブラリ向け タグ検査・編集デスクトップアプリケーション（Windows / WPF）。

`docs/TAGGING_POLICY.md` に定めた原則に沿って、音楽ライブラリのタグを検査・修正する。

**最重要方針**: 一括処理ツールでありながら、**適用前に必ず差分を人間が確認できる**こと。原則で確信が持てない項目を自動で埋めないこと。

---

## ドキュメント

| ファイル | 内容 |
|---|---|
| [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) | タグ付けの原則。実装の唯一の基準 |
| [docs/SPEC.md](docs/SPEC.md) | アプリケーション仕様 |
| [docs/adr/0001-tag-io-library.md](docs/adr/0001-tag-io-library.md) | タグ入出力ライブラリの選定記録 |
| [docs/library-baseline-2026-08-03.md](docs/library-baseline-2026-08-03.md) | 実ライブラリの実態。検査ルール実装時の答え合わせ用 |
| [docs/branch_strategy.md](docs/branch_strategy.md) | ブランチ戦略 |
| [docs/llm_guideline.md](docs/llm_guideline.md) | コーディング規約 |

---

## 開発環境

- .NET 10 SDK（LTS。GA 2025-11-11 / サポート期限 2028-11-14）
- Windows 11

.NET 8 / .NET 9 は 2026-11-10 にサポート終了のため採用しない。

### ビルドとテスト

```bash
dotnet build
```

```bash
dotnet test
```

---

## プロジェクト構成

| プロジェクト | TFM | 役割 |
|---|---|---|
| `src/MusicTagger.Core` | `net10.0` | ドメイン。正規化・辞書・検査ルール。UI とタグライブラリに依存しない |
| `src/MusicTagger.TagIo` | `net10.0` | タグ読み書きの抽象（`ITagReader` / `ITagWriter`）と実装 |
| `src/MusicTagger.App` | `net10.0-windows` | WPF アプリケーション（MVVM） |
| `tests/MusicTagger.Core.Tests` | `net10.0` | ドメインのテスト |
| `tests/MusicTagger.TagIo.Tests` | `net10.0` | タグ読み書きの往復テスト |
| `tools/TagIoProbe` | `net10.0` | タグライブラリ選定の検証スパイク（選定後は破棄可） |

---

## タグ入出力の実装上の注意

[ADR-0001](docs/adr/0001-tag-io-library.md) の実測にもとづく制約。**これを守らないと AIMP からタグが見えなくなる、あるいは値が壊れる。**

- **M4A の指揮者は `©con`（`A9 63 6F 6E`）に書く。** TagLib# の `Tag.Conductor` は `cond` に書くため**使ってはならない**。`AppleTag.SetText` で atom を明示する。
- **M4A の読み取りは自前の MP4 atom リーダーを使う。** TagLib# の MP4 読み取りは `; ` で値を分割するため、「1 値に `;` が含まれる状態」と「複数値に分割済みの状態」を区別できない。この区別は検査ルール R-205 / R-206 に必要。
- **M4A はファイル全体を読まない。** タグは `moov` の中にあり、ファイルの大半を占める `mdat`（音声本体）には無い。全体を読むと 1,041 ファイルのスキャンに 34 秒かかり、非機能要件（1,000 ファイル / 10 秒）を満たせない。`moov` だけをシークして読むこと。
- **`;` を値の区切りに使わない**（`TAGGING_POLICY.md` 3.4）。
- **既知の制約**: M4A では複数値を書き分けられない。TagLib# の `AppleTag.SetText` は `string[]` を `"; "` で連結して 1 つの data ボックスに書くため、AIMP が分割した状態を復元できない。書き込み後の読み戻し照合で不一致として検出される。FLAC / MP3 / AIFF に制約はない。

---

## 検査ルール

`docs/SPEC.md` 6.1 のルールを実装している。段階 3 時点で有効なのは R-1xx / R-2xx。

実装で外せない前提が 3 つある。いずれも `docs/library-baseline-2026-08-03.md` の実測から導かれたもので、守らないと誤検出だらけになる。

1. **`TAGGING_POLICY.md` 2.3 の保護対象（配役情報）は全ルールの検査前に除外する**
2. **団体名に含まれる作曲家の姓を作曲家名として拾わない**（`Smetana Quartet` / `Münchener Bach-Chor`）
3. **頭字語を全大文字判定から除外し、「姓, 名」順の判定は人名フィールドに限る**（`USSR State Symphony Orchestra` / `Kirov Orchestra, Mariinsky Theatre`）

団体名の時代分割（5.3.1）は**実体 ID** で判断する。名前が似ていても別実体、名前が違っても同一実体がある。`date` が空欄で名称を決められない場合は書き換えず `HOLD_ERA_UNKNOWN` として保留する。

### 適用

「検査」→ チェックを確認 → 「チェックした項目を適用」の順に進む。適用時は必ず次の順序を通る（`docs/SPEC.md` 9章）。

1. **適用直前にタグのスナップショットを自動取得する。** 利用者が明示的にバックアップを取っていなくても
2. チェックされた差分だけを書き込む。1 ファイルにつき 1 回
3. **書き込んだ全項目を読み戻して照合する。** この工程を省略しない
4. 成功件数・失敗件数・不一致件数を表示する

**1 件の失敗で全体を止めない。** 失敗・不一致・競合は一覧として残し、ログにも出す。

同じフィールドに異なる修正案が選ばれている場合は**書き込まずに競合として報告する**。どちらが正しいか機械的に決められないため。

### 正規化辞書

`%APPDATA%\musicTagger\dictionary.json`。初回起動時に同梱の既定辞書がコピーされる。

照合は正規化キー（NFKC・小文字化・ひらがな→カタカナ・ダイアクリティカルマーク除去・記号と空白の除去）で行うため、中黒やピリオドの有無、大文字小文字の違いは登録不要。

**推測で名前を足さないこと。** 誤った `canonical` は誤った修正案を生む。エントリは原則の表に載っているものと、実ライブラリに実在する値に限る。

---

## バックアップと復元

**音声ファイル本体は複製しない**（対象ライブラリは 30GB）。タグだけを JSON にスナップショットする。

保存先はライブラリ直下の `backup_{yyyyMMddHHmmss}\`。

| ファイル | 内容 |
|---|---|
| `tags_snapshot.json` | 全ファイルの全タグ。復元はこの `fields` を使う |
| `manifest.json` | 取得日時・理由・件数 |
| `restore-tags.ps1` | **アプリ無しで復元する PowerShell** |
| `TagLibSharp.dll` | 上記スクリプトが使う |

### アプリを使わずに復元する

```bash
pwsh -File "D:\Music Library for AIMP\Classic\backup_20260803031500\restore-tags.ps1" -DryRun
```

`-DryRun` を外すと実際に書き戻す。PowerShell 7 以降が必要。`-PathFilter` で対象を絞り込める。

### 検証スパイクの実行

```bash
dotnet run --project tools/TagIoProbe/TagIoProbe.csproj -- "D:\Music Library for AIMP\Classic"
```

実ライブラリのファイルは読み取りのみ。検体は `tools/TagIoProbe/work/` に複製され、書き込みはその複製に対してのみ行う。結果は `tools/TagIoProbe/work/report.md` に出力される。

---

## 実行

```bash
dotnet run --project src/MusicTagger.App/MusicTagger.App.csproj
```

第 1 引数にライブラリのパスを渡すと、起動直後にそのフォルダを開いてスキャンする。

```bash
dotnet run --project src/MusicTagger.App/MusicTagger.App.csproj -- "D:\Music Library for AIMP\Classic"
```

ログは `%LOCALAPPDATA%\musicTagger\logs\` に日次で出力される。設定・辞書は `%APPDATA%\musicTagger\`。

---

## 環境変数

| 変数名 | 用途 | 既定値 |
|---|---|---|
| `MUSICTAGGER_LIBRARY_ROOT` | 実ライブラリを使う結合テストの対象パス。**テスト専用**で、アプリ本体は参照しない。指定したフォルダが存在しない場合、該当テストはスキップされる | `D:\Music Library for AIMP\Classic` |

---

## ブランチ

- `develop` が既定ブランチ。`main` と `develop` への直 push は禁止（初期構築時を除く）
- 機能追加は `feature/`、バグ修正は `fix/` を `develop` から作成し、PR を経てマージする

詳細は [docs/branch_strategy.md](docs/branch_strategy.md)。
