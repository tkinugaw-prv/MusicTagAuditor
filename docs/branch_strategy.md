# Branch 戦略

## Branch構成
- main
- develop
  - develop Branch を Default branch とする

- 初期構築時を除き、 main, develop への直 push は禁止
  - 必ず PR を作成し、レビューの後マージすること

- 機能追加時には `feature/` Branchを develop から作成すること
- バグ修正時には `fix/` Branch を develop から作成すること

