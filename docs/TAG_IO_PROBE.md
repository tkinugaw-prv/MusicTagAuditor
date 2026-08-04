# TagIoProbe

`tools/TagIoProbe` は、musicTagger 本体が使うタグ入出力ライブラリを選ぶための**使い捨ての検証スパイクツール**である。[docs/SPEC.md](SPEC.md) 4章「タグ入出力ライブラリの選定」で定義された V1〜V8 の検証項目を、実ライブラリから複製した検体に対して実測する。

検証結果と最終決定は [docs/adr/0001-tag-io-library.md](adr/0001-tag-io-library.md) にまとめられており、**TagLibSharp が採用済み**。このツール自体は選定が完了した後も再検証・再現用に残しているもので、`README.md` の「プロジェクト構成」表にあるとおり「選定後は破棄可能なスパイク」という位置づけ。

---

## 目的

TagLibSharp と z440.atl.core (ATL.NET) のどちらを採用すべきか、ライブラリ自身の読み戻しに頼らず、M4A については MP4 ボックスを直接バイナリ走査した結果で実測比較する。判定はライブラリの主張ではなく、実際にファイルへ書き込まれたバイト列を根拠にする。

## 実行方法

```bash
dotnet run --project tools/TagIoProbe/TagIoProbe.csproj -- "D:\Music Library for AIMP\Classic"
```

- 第1引数(`args[0]`)にライブラリのルートパスを渡す。省略した場合は既定値 `D:\Music Library for AIMP\Classic` を使う([Program.cs:7](../tools/TagIoProbe/Program.cs))
- 指定したパスが存在しない場合はエラーメッセージを出して終了コード `1` で終了する([Program.cs:9-13](../tools/TagIoProbe/Program.cs))
- **実ライブラリのファイルは読み取り専用で扱う。** 検体はフォーマットごとに `tools/TagIoProbe/work/{taglibsharp,atl}/` へ複製され、書き込みテストは複製に対してのみ行う([Program.cs:5](../tools/TagIoProbe/Program.cs))
- `work/` は毎回削除してから再生成される([Program.cs:18-21](../tools/TagIoProbe/Program.cs))ため、実行結果は都度上書きされる

## 検証項目(V1〜V8)

| # | 確認内容 |
|---|---|
| V1 | M4A の指揮者(Conductor)を `©con` atom に書けるか |
| V2 | 未知の4文字 atom(`©con` など)を直接読み書きできるか |
| V3 | フリーフォーム atom(`----:com.apple.iTunes:*`)を保存時に壊さず保持するか |
| V4 | カバーアート(`covr`)を保存時に保持するか |
| V5 | FLAC `CONDUCTOR` / ID3v2 `TPE3` を扱えるか |
| V6 | AIFF の ID3 チャンクを扱えるか |
| V7 | `;` を区切り文字として複数値に分割せず、1値のまま書き込めるか |
| V8 | .NET 10 (`net10.0`) で動作するか、パッケージの更新状況はどうか |

判定は `OK` / `NG` / `N/A`(対象外)/ `ERROR`(検証中に例外)の4種([CheckResult.cs](../tools/TagIoProbe/CheckResult.cs))。実測結果の詳細と根拠は [docs/adr/0001-tag-io-library.md](adr/0001-tag-io-library.md) の「実測結果」表を参照。

## ファイル構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | エントリポイント。検体の準備・両ライブラリでの検証実行・レポート生成の一連の流れを制御する |
| `Const.cs` | 定数(作業フォルダ名 `work`、レポート名 `report.md`、`©con` atom のバイト列、検証用の指揮者名・区切り文字値、対象拡張子 `.m4a/.flac/.mp3/.aif/.aiff` など) |
| `CheckResult.cs` | 検証結果1件を表す `record CheckResult(string Id, string Library, string Format, string Verdict, string Detail)` と判定値定数(`Verdict.OK/NG/NOT_APPLICABLE/ERROR`) |
| `SpecimenPreparer.cs` | 実ライブラリから検体ファイルを `work/` へ複製する。`backup_*` フォルダは複製対象から除外する |
| `TagLibSharpProbe.cs` | TagLibSharp による V1〜V7 の検証ロジック |
| `AtlProbe.cs` | ATL.NET による V1〜V7 の検証ロジック |
| `NeutralReader.cs` | TagLibSharp の XiphComment/ID3v2 経由で FLAC/MP3/AIFF の生フィールド値を読む、ライブラリ非依存の読み取りロジック |
| `Mp4AtomDumper.cs` | MP4 のボックス構造(`moov/udta/meta/ilst` 配下)を自前でバイナリ走査し atom を列挙する。判定の根拠として使う |
| `work/`(`.gitignore` 済み) | 実行時に生成される複製検体と `report.md`。リポジトリには含まれない |

## 入出力

- **入力**: `libraryRoot` 配下の `.m4a` / `.flac` / `.mp3` / `.aif` / `.aiff`(`Const.TARGET_EXTENSIONS`)。実ファイルは読み取り専用
- **出力**: `work/report.md`(検体一覧、書き込み前後の M4A atom ダンプ、V1〜V8 の検証結果マトリクス、AIMP での目視確認手順を含む Markdown)。同内容を標準出力にも出す([Program.cs:133-138](../tools/TagIoProbe/Program.cs))

## 本体アプリとの関係

`MusicTagAuditor.slnx` の `tools/` フォルダにプロジェクトとして含まれているが、`src/MusicTagAuditor.TagIo` や他の `src/` プロジェクトへの参照は持たない**完全に独立した exe**([TagIoProbe.csproj](../tools/TagIoProbe/TagIoProbe.csproj) には `TagLibSharp` と `z440.atl.core` のみを参照)。

ただし検証結果は本体の設計に直接反映されている。ADR-0001 の決定により「TagLibSharp を採用し、`Mp4AtomDumper.cs` の読み取りロジックを `MusicTagAuditor.TagIo` に移す」こととなり、実際に [src/MusicTagAuditor.TagIo/Mp4/Mp4AtomReader.cs](../src/MusicTagAuditor.TagIo/Mp4/Mp4AtomReader.cs)(モデルは [Mp4Atom.cs](../src/MusicTagAuditor.TagIo/Mp4/Mp4Atom.cs))として本体側に移植されている(コードを直接共有しているわけではなく、設計を踏襲したもの)。

## 関連ドキュメント

| ドキュメント | 内容 |
|---|---|
| [docs/adr/0001-tag-io-library.md](adr/0001-tag-io-library.md) | 実測結果の詳細、決定内容、再現手順 |
| [docs/SPEC.md](SPEC.md) | 4章「タグ入出力ライブラリの選定」の要件定義 |
| [docs/ARCHITECTURE.md](ARCHITECTURE.md) | `MusicTagAuditor.TagIo` を含む本体モジュールの構成 |
