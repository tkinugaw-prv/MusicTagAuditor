# コントリビューションガイド

個人プロジェクトだが、Issue と Pull Request は歓迎する。

## 開発環境

- .NET 10 SDK
- Windows 11（WPF プロジェクトを含むため Windows でしかビルドできない）

```bash
dotnet build
```

```bash
dotnet test
```

`[RealLibraryFact]` を付けた結合テスト 11 件は実際の音楽ライブラリを必要とする。環境変数 `MUSICTAGAUDITOR_LIBRARY_ROOT` を設定しない限り自動的にスキップされるので、手元にライブラリが無くても残りのテストは通る。

実ライブラリを指定した場合、OS のファイルキャッシュが冷えている初回実行は 2 回目以降より大幅に時間がかかる（実測で 16.8 秒 対 0.06 秒）。性能テストは自分でウォームアップ走査を行ってから計測するため、**遅いだけで失敗はしない**。

テスト結果とカバレッジレポートの生成手順は [README.md](README.md#テストとカバレッジレポート) を参照。

## 守ってほしいこと

| 項目 | 参照先 |
|---|---|
| ブランチ運用・PR の出し方 | [docs/branch_strategy.md](docs/branch_strategy.md) |
| コーディング規約（命名・ヘッダコメント・環境変数の扱い） | [docs/llm_guideline.md](docs/llm_guideline.md) |
| 実装上の前提（画面・検査ルール・辞書・バックアップ） | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| タグ付けの原則（実装の唯一の基準） | [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) |
| アプリを起動しての動作確認 | [docs/manual_verification.md](docs/manual_verification.md) |

要点だけ挙げると:

- `develop` が既定ブランチ。**`main` と `develop` への直 push は禁止**。`feature/` または `fix/` を `develop` から作成し、PR を経てマージする
- **ビルドは警告 0 件でなければ通らない。** `TreatWarningsAsErrors=true` と `GenerateDocumentationFile=true` を設定しているため、public メンバーに XML ドキュメントコメントが無いと CS1591 で失敗する
- **検査ルールの変更は [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) を根拠にする。** 原則に無い挙動を実装しない。原則自体を変えるなら、まずそちらの改訂を提案してほしい
- **`reports/` をコミットしない。** TRX にはローカルのユーザー名・マシン名、Cobertura にはソースの絶対パスが入る（`.gitignore` 済み）
- **UI の確認に実ライブラリを使わない。** 実ライブラリから数フォルダをコピーしたテスト用ライブラリを開くこと。押し間違い 1 回で所蔵のタグが書き換わる（[docs/manual_verification.md](docs/manual_verification.md)）
- ドキュメント・コメント・コミットメッセージは日本語で書く

## Issue

不具合報告には次を含めてほしい。

- 対象ファイルの形式（M4A / FLAC / MP3 / AIFF）
- 再現手順と、期待した結果・実際の結果
- `%LOCALAPPDATA%\MusicTagAuditor\logs\` の該当日のログ（**個人を特定しうるパスは伏せてよい**）

脆弱性の報告は Issue ではなく [SECURITY.md](SECURITY.md) の手順に従うこと。
