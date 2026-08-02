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
