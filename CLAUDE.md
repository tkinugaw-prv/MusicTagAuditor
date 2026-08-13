# MusicTagAuditor

クラシック音楽ライブラリ向けのタグ検査・編集アプリ（WPF / .NET 10）。

## 作業を始める前に読むもの

**ここには要点しか書かない。** 判断に迷ったら必ず参照先の本文を読むこと。
同じ規則を複数の場所に書き写すと、片方だけが更新されて食い違う。

| 内容 | 参照先 |
|---|---|
| ブランチ運用・PR の出し方 | [docs/branch_strategy.md](docs/branch_strategy.md) |
| コーディング規約（命名・ヘッダコメント） | [docs/llm_guideline.md](docs/llm_guideline.md) |
| 仕様（画面・検査ルール・辞書） | [docs/SPEC.md](docs/SPEC.md) |
| 実装上の前提 | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| タグ付けの原則（実装の唯一の基準） | [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) |
| 開発環境・テストの動かし方 | [CONTRIBUTING.md](CONTRIBUTING.md) |

## 破ってはいけないもの

- **`main` と `develop` へ直接コミット・push しない。** 変更に着手する前に、
  `develop` から `feature/` または `fix/` を切る。マージは PR を経由する
  （[docs/branch_strategy.md](docs/branch_strategy.md)）
- **ビルドは警告 0 件でなければ通らない。** `TreatWarningsAsErrors=true` と
  `GenerateDocumentationFile=true` のため、public メンバーに XML ドキュメント
  コメントが無いと CS1591 で失敗する
- **検査ルールの変更は [docs/TAGGING_POLICY.md](docs/TAGGING_POLICY.md) を根拠にする。**
  原則に無い挙動を実装しない。原則自体を変えるなら、まずそちらの改訂を提案する
- **`reports/` をコミットしない。** TRX にローカルのユーザー名・マシン名、
  Cobertura にソースの絶対パスが入る（`.gitignore` 済み）
- ドキュメント・コメント・コミットメッセージは日本語で書く

## 変更に添えるもの

- 挙動を変えたら、対応する `docs/` を同じコミットで直す。仕様書が実装から
  遅れると、次に読む人はどちらが正しいのか判断できない
- コメントとコミットメッセージには**なぜそうしたのか**を書く。何をしたかは
  差分が語る。避けた失敗や捨てた代案が、後から読む人には最も要る情報になる

## ビルドとテスト

```bash
dotnet build MusicTagAuditor.slnx
```

```bash
dotnet test
```

`[RealLibraryFact]` を付けた結合テストは実ライブラリを要する。環境変数
`MUSICTAGAUDITOR_LIBRARY_ROOT` を設定しない限り自動でスキップされる。
