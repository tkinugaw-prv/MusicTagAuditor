# LLM Gudeline
- 本ドキュメントは汎用的なガイドラインである
- プロジェクトによっては利用しない技術もある

# コーディング規約

- 定数は `FULL_CAPITAL`　を使用する
- 名前空間名、クラス名、メソッド名などは `UpperCamel` を使用する
- ローカル変数は `lowerCamel` を使用する
- クラス変数は `_lowerCalem` のように先頭に `_` を付加する
- インターフェイスは `I` を先頭に付加する
- DB テーブル名、カラム名は `snake_case` を使用する
- DB スキーマ名は `UpperCamel` を使用する
- 環境変数名はハードコード禁止、 `const.cs` に定義内容をまとめること
- クラス、メソッドなどにはヘッダコメントは必須である

- 条件分岐等の書法は以下に従う
  - DO
  ~~~CSharp
  // Do this way
  if (someValue == 42)
  {
    DoSomething();
  }
  ~~~

  - DON'T
  ~~~CSharp
  //Don't do this.
  if (someValue == 42) DoSomething();
  //Don't do this.
  if(someValue == 42){
    DoSomething();
  }
~~~

----
# DB に関するコーディング規約
- EF Core では可能な限り `RawSql` を使用すること
- トランザクションレベルの変更は禁止である

# README.md について 
- 変更実施時には README.md の更新が必要を確認すること
- 使用する環境変数がある場合には `README.md` にテーブルを作成し、そこに記述すること
