# Branch 戦略

## Branch構成
- main
- develop
  - develop Branch を Default branch とする

- 初期構築時を除き、 main, develop への直 push は禁止
  - 必ず PR を作成し、レビューの後マージすること

- 機能追加時には `feature/` Branchを develop から作成すること
- バグ修正時には `fix/` Branch を develop から作成すること

- CI（ビルド + テスト）は **base ブランチを問わず全ての PR で走る**
  - `feature/` の上に PR を積んでもよい。先の PR がマージされれば base は develop へ自動で付け替わる
  - base を絞っていた頃は、積んだ PR の CI が動かないまま必須チェック未実行で止まった（2026-08-15 に解消）

## リリース

- リリースタグ（`v` 始まり）は **main から切ること**
  - develop → main の PR をマージした後、main のコミットに対してタグを打つ
  - release ワークフローは main に含まれないコミットのタグを拒否する

