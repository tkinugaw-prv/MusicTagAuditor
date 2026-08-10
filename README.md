日本語 | [English](README.en.md)

# Music Tag Auditor

クラシック音楽ライブラリ向け タグ検査・編集デスクトップアプリケーション（Windows / WPF）。

[docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) に定めた原則に沿って、音楽ライブラリのタグを検査・修正する。

**最重要方針**: 一括処理ツールでありながら、**適用前に必ず差分を人間が確認できる**こと。原則で確信が持てない項目を自動で埋めないこと。

主な機能:

- フォルダを再帰スキャンして M4A / FLAC / MP3 / AIFF のタグを読む
- 24 の検査ルール（[docs/SPEC.md](docs/SPEC.md) 6.1）で問題を検出し、修正案と根拠を出す
- **適用直前にタグのスナップショットを自動取得**し、書き込み後は全項目を読み戻して照合する
- ファイル一覧での手編集、フォルダ単位の一括入力、辞書からの入力候補
- **検査結果をフォルダ単位に絞り込み**、表示・一括選択・適用・CSV 出力をその範囲に揃える
- 正規化辞書（作曲家 / 人物 / 団体 / 誤記 / 保護対象）の編集と検証
- 検査結果の CSV 出力、アプリ無しで戻せる PowerShell 復元スクリプト付きバックアップ

実装済みの範囲は [docs/SPEC.md](docs/SPEC.md) 12章の段階 0〜7（スキャン / バックアップと復元 / 検査ルール / 適用と読み戻し照合 / 辞書タブと辞書編集 / 手編集 / 全ルール）。

---

## 技術スタック

| 項目 | 内容 |
|---|---|
| ランタイム | .NET 10（LTS。GA 2025-11-11 / サポート期限 2028-11-14） |
| UI | WPF（`net10.0-windows`）+ MVVM |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection 10.0.10 |
| タグ入出力 | TagLibSharp 2.3.0 + 自前の MP4 atom リーダー（[ADR-0001](docs/adr/0001-tag-io-library.md)） |
| ログ | Serilog 4.4.0 + Serilog.Sinks.File 7.0.0 |
| テスト | xUnit 2.9.3 + coverlet.collector 6.0.4 |

.NET 8 / .NET 9 は 2026-11-10 にサポート終了のため採用しない。

配色とコントロールの見た目は姉妹プロジェクト [MusicFolderTimeFitter](https://github.com/tkinugaw-prv/MusicFolderTimeFitter)（音楽フォルダー時間フィッター）に合わせている。詳細は [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

---

## ビルドと実行

必要なもの: .NET 10 SDK / Windows 11（WPF を含むため Windows でしかビルドできない）

```bash
dotnet build
```

```bash
dotnet run --project src/MusicTagAuditor.App/MusicTagAuditor.App.csproj
```

最後に開いたライブラリは設定に残り、次回の起動で自動的に開いてスキャンまで行う。見つからない場合はステータスバーに出すだけで、設定からは消さない（外付けドライブを外しているだけかもしれない）。

第 1 引数にライブラリのパスを渡すと、記憶しているライブラリより優先して、起動直後にそのフォルダを開いてスキャンする。

```bash
dotnet run --project src/MusicTagAuditor.App/MusicTagAuditor.App.csproj -- "D:\Music\Classic"
```

ログは `%LOCALAPPDATA%\MusicTagAuditor\logs\` に日次で出力される。辞書は `%APPDATA%\MusicTagAuditor\dictionary.json`、設定は同フォルダの `settings.json`（現在の項目はバックアップ先と前回のライブラリ）。設定が壊れていても既定値で起動し、理由をログに残す。

操作方法は [docs/USER_MANUAL.md](docs/USER_MANUAL.md) を参照。

---

## 配布物

[Releases](https://github.com/tkinugaw-prv/MusicTagAuditor/releases) から Windows x64 向けの単一 exe を配布している。

| ファイル | 形態 | 実行要件 |
|---|---|---|
| `MusicTagAuditor-<tag>-win-x64.exe` | 自己完結型（ランタイム同梱） | なし（Windows x64） |
| `MusicTagAuditor-<tag>-win-x64-fdd.exe` | フレームワーク依存型 | .NET 10 デスクトップランタイム |

### ローカルでの publish

```powershell
dotnet publish src/MusicTagAuditor.App -p:PublishProfile=win-x64-self-contained
```

出力は `src/MusicTagAuditor.App/bin/publish/win-x64-self-contained/`。プロファイルは `src/MusicTagAuditor.App/Properties/PublishProfiles/` にある。

どちらのプロファイルも `IncludeAllContentForSelfExtract` を有効にしている。これを外すと単一 exe に埋め込まれたアセンブリの `Assembly.Location` が空文字になり、**バックアップに復元スクリプト用の `TagLibSharp.dll` を同梱できなくなる**（アプリ無しでの復元が使えなくなる）。

### リリース手順

`v` で始まるタグを push すると、[release ワークフロー](.github/workflows/release.yml)がテスト → 両構成の publish → GitHub Release 作成（exe 添付）を自動実行する。バージョンはタグ名から設定される（例: `v1.2.3` → `1.2.3`）。

```powershell
git tag v1.0.0
git push origin v1.0.0
```

---

## テストとカバレッジレポート

テスト結果とカバレッジレポートは GitHub Actions（[CI ワークフロー](.github/workflows/ci.yml)）が実行ごとに生成する。

| 公開場所 | 内容 |
|---|---|
| 各 Run の **概要ページ** | カバレッジのサマリー表（ログイン不要・失効しない） |
| 各 Run の **Artifacts** `test-results` | テスト結果の生データ（TRX） |
| 各 Run の **Artifacts** `coverage-report` | カバレッジ生データ（Cobertura XML）から生成した HTML レポート |

第三者はここからテストの合否と各クラスのカバレッジを検証できる。

### 依存関係の更新

NuGet パッケージと GitHub Actions のバージョン更新は Dependabot が毎週 PR にする（[設定](.github/dependabot.yml)）。宛先は既定ブランチの `develop`。minor / patch はまとめ、major は 1 件ずつ出す。

ワークフローの Action はコミット SHA で固定しているが、Dependabot は SHA と末尾のバージョンコメントの両方を書き換えるため、固定方針は崩れない。

### ローカルでの再現手順

```powershell
# 1. テスト実行 + TRX ログ + カバレッジ収集（要 .NET 10 SDK）
dotnet test --logger "trx" --collect:"XPlat Code Coverage" --results-directory "reports/raw"
```

```powershell
# 2. ReportGenerator のインストール（初回のみ）
dotnet tool install --global dotnet-reportgenerator-globaltool
```

```powershell
# 3. HTML レポート生成（reports/ は git 管理外）
reportgenerator "-reports:reports/raw/*/coverage.cobertura.xml" "-targetdir:reports/coverage/html" "-reporttypes:Html;TextSummary"
```

手順 3 のグロブを `**/` にしないこと。TRX ロガーが `coverage.cobertura.xml` を添付ディレクトリにも複製するため、同じレポートを二重に読み込む。

`reports/` は git 管理外にしている。TRX にはローカルのユーザー名・マシン名が、Cobertura XML にはソースの絶対パスが埋め込まれるため。

### テストの範囲

| プロジェクト | 対象 |
|---|---|
| `MusicTagAuditor.Core.Tests` | ドメイン全域。正規化・辞書・検査ルール・バックアップ・適用・CSV 出力・設定 |
| `MusicTagAuditor.TagIo.Tests` | タグ読み書きの往復。MP4 atom リーダー |
| `MusicTagAuditor.App.Tests` | ViewModel。WPF の `ListCollectionView` を実際に動かして検証する |

単体テストの対象外は **View / XAML / テーマ**（`MainWindow.xaml`、`Themes/DarkTheme.xaml` など）で、これらは手動の動作確認で検証する。全体のラインカバレッジにはこの対象外の層も分母として入るため、層ごとの数値は Run の概要ページで確認すること。

`[RealLibraryFact]` を付けた**結合テスト 11 件は実際の音楽ライブラリを必要とする**ため、`MUSICTAGAUDITOR_LIBRARY_ROOT` が未設定の環境ではスキップされる。CI では常にスキップされるので、Run で見えるテスト件数は手元で実ライブラリを指定した場合より 11 件少ない。

---

## 環境変数

| 変数名 | 用途 | 既定値 |
|---|---|---|
| `MUSICTAGAUDITOR_LIBRARY_ROOT` | 実ライブラリを使う結合テストの対象パス。**テスト専用**で、アプリ本体は参照しない。未設定の場合、および指定したフォルダが存在しない場合、該当テストはスキップされる | なし（既定値は持たない） |

---

## ドキュメント

| ファイル | 内容 |
|---|---|
| [docs/USER_MANUAL.md](docs/USER_MANUAL.md) | エンドユーザー向け操作マニュアル |
| [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) | タグ付けの原則。実装の唯一の基準 |
| [docs/SPEC.md](docs/SPEC.md) | アプリケーション仕様 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | モジュール構成の詳細解説 |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 開発ハンドブック。画面・検査ルール・手編集・辞書・バックアップの実装上の前提 |
| [docs/TAG_IO_PROBE.md](docs/TAG_IO_PROBE.md) | `tools/TagIoProbe`(タグ入出力ライブラリ選定の検証ツール)の説明 |
| [docs/adr/0001-tag-io-library.md](docs/adr/0001-tag-io-library.md) | タグ入出力ライブラリの選定記録 |
| [docs/library-baseline-2026-08-03.md](docs/library-baseline-2026-08-03.md) | 実ライブラリの実態。検査ルール実装時の答え合わせ用 |
| [docs/branch_strategy.md](docs/branch_strategy.md) | ブランチ戦略 |
| [docs/llm_guideline.md](docs/llm_guideline.md) | コーディング規約 |

---

## プロジェクト構成

| プロジェクト | TFM | 役割 |
|---|---|---|
| `src/MusicTagAuditor.Core` | `net10.0` | ドメイン。正規化・辞書・検査ルール。UI とタグライブラリに依存しない |
| `src/MusicTagAuditor.TagIo` | `net10.0` | タグ読み書きの抽象（`ITagReader` / `ITagWriter`）と実装 |
| `src/MusicTagAuditor.App` | `net10.0-windows` | WPF アプリケーション（MVVM） |
| `tests/MusicTagAuditor.Core.Tests` | `net10.0` | ドメインのテスト |
| `tests/MusicTagAuditor.TagIo.Tests` | `net10.0` | タグ読み書きの往復テスト |
| `tests/MusicTagAuditor.App.Tests` | `net10.0-windows` | App 層のテスト。WPF のコレクションビューを実際に動かすため `UseWPF` が要る |
| `tools/TagIoProbe` | `net10.0` | タグライブラリ選定の検証スパイク（選定後は破棄可） |

---

## コントリビューション

[CONTRIBUTING.md](CONTRIBUTING.md) を参照。

- `develop` が既定ブランチ。`main` と `develop` への直 push は禁止（初期構築時を除く）
- 機能追加は `feature/`、バグ修正は `fix/` を `develop` から作成し、PR を経てマージする

詳細は [docs/branch_strategy.md](docs/branch_strategy.md)。

---

## ライセンス

MIT License — [LICENSE](LICENSE) を参照。

配布物に含まれる第三者コンポーネントの著作権表示とライセンス全文は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にまとめている（Release にも同じファイルを添付している）。

依存ライブラリの [TagLibSharp](https://github.com/mono/taglib-sharp) は LGPL-2.1-only である。**LGPL は利用する側のライセンスを制約しないため、本プロジェクトのソースコードは MIT のまま**であり、TagLibSharp は改変せず NuGet の公式パッケージをそのまま使っている。

ただし配布している exe は `PublishSingleFile` により TagLibSharp.dll を実行ファイル内へバンドルしている。改変版の TagLibSharp と差し替えたい場合は、単一ファイル化しない構成で publish すれば `TagLibSharp.dll` が独立したファイルとして出力され、本体を再ビルドせずに置き換えられる。

```powershell
dotnet publish src/MusicTagAuditor.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
```

Serilog は Apache-2.0、CommunityToolkit.Mvvm と Microsoft.Extensions.DependencyInjection は MIT。詳細は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照。
