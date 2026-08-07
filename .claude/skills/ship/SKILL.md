---
name: ship
description: リリース前の定型ワークフロー。全テスト実行(カバレッジ確認) → リリースビルド → コミット → プッシュを順に行う。「リリースして」「出荷」「テストしてコミット・プッシュ」などで使う。
---

リリース前チェックとコミット・プッシュを一気通貫で行う手順書。
**順序厳守**: テストかビルドが失敗したら、その時点で中断して失敗内容を報告し、コミット・プッシュには進まない。
パスはすべてリポジトリルート基準。

## 1. テスト実行 + カバレッジ確認

```powershell
dotnet test --logger "trx" --collect:"XPlat Code Coverage" --results-directory "reports/raw"
```

- **テストが 1 件でも失敗したら即中断**。失敗したテスト名と出力を報告して終了。
- `[RealLibraryFact]` の付いた結合テストは `MUSICTAGAUDITOR_LIBRARY_ROOT` を設定していない環境ではスキップされる。**スキップは失敗ではない**が、件数は報告に含める。
- 全パスしたらカバレッジサマリーを生成して確認する（`reports/` は git 管理外。コミットしない）:

```powershell
# reportgenerator 未インストール時のみ（初回）
if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) { dotnet tool install --global dotnet-reportgenerator-globaltool }

reportgenerator "-reports:reports/raw/*/coverage.cobertura.xml" "-targetdir:reports/coverage/html" "-reporttypes:TextSummary"
```

- `reports/coverage/html/Summary.txt` を読んでカバレッジサマリーを把握しておく（コミットメッセージや報告に使う）。
- 公開向けのテスト結果・カバレッジレポートは GitHub Actions（CI ワークフロー）がプッシュ後に
  Artifacts（`test-results` / `coverage-report`）として自動生成し、カバレッジ表を Run の概要にも出す。ローカル成果物のコミットは不要。

## 2. リリースビルド

```powershell
dotnet build -c Release
```

- 失敗したら中断（コミットしない）。エラー内容を報告して終了。
- 出力: `src\MusicTagAuditor.App\bin\Release\net10.0-windows\MusicTagAuditor.App.exe`

## 3. コミット

1. `git status` と `git diff` で変更内容を確認する。
2. 全変更をステージし、既存の規約に従ってコミットする:
   - 件名: 日本語で変更の要約（`git log --oneline` の既存スタイルに合わせる）
   - 本文: 箇条書きで変更点
   - 末尾: `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

## 4. プッシュ

```powershell
git push origin <現在のブランチ>
```

- リモートは origin（github.com/tkinugaw-prv/MusicTagAuditor）。
- **`main` と `develop` への直 push は禁止**（`docs/branch_strategy.md`）。`feature/` または `fix/` ブランチに push し、`develop` への PR を作る。
  現在のブランチが `main` / `develop` なら、push せずにブランチを切るよう促して終了する。
- 完了したらテスト件数・カバレッジ・コミットハッシュ・プッシュ先を報告する。
- プッシュ後に GitHub Actions の CI ワークフローが成功したことを確認する（`gh run watch` など）。

## Gotchas

- **`reports/` は git 管理外**（.gitignore 済み）— TRX にはローカルのユーザー名・マシン名、Cobertura にはソースの絶対パスが入るため絶対にコミットしない。
- **`dotnet test` は Debug 構成で走る** — Release ビルド（手順 2）とは別物。両方必要。
- **`-reports` のグロブは `*/` の 1 階層にする** — TRX ロガーが `coverage.cobertura.xml` を添付ディレクトリにも複製するため、`**/` にすると同じレポートを二重に読み込む。
- **`TreatWarningsAsErrors=true` + `GenerateDocumentationFile=true`**（`Directory.Build.props`）のため、public メンバーに XML ドキュメントコメントが無いと **CS1591 でビルドが落ちる**。警告 0 件でないとそもそもビルドが通らない。
