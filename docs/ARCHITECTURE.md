# モジュール構成

Music Tag Auditor のソースツリーがどう分割されているか、各プロジェクトが何を担い、互いにどう依存しているかをまとめた技術ドキュメント。

`README.md` の「プロジェクト構成」表(全体の一覧)を補い、各プロジェクト内部のフォルダ構成まで掘り下げる。実装の詳細な仕様(検査ルールの各論・タグ入出力の制約・UI 配色など)はここには書かず、[docs/SPEC.md](SPEC.md)・[docs/TAGGING_POLICY.md](TAGGING_POLICY.md)・[docs/DEVELOPMENT.md](DEVELOPMENT.md) を参照する形にしている。二重管理を避けるため、内容を変更したときはどちらか一方だけ直さないこと。

---

## 全体の依存関係

```
MusicTagAuditor.App  (net10.0-windows, WPF / MVVM)
        │
        ├─→ MusicTagAuditor.Core  (net10.0, ドメイン)
        │
        └─→ MusicTagAuditor.TagIo (net10.0, タグ入出力)
                    │
                    └─→ MusicTagAuditor.Core

tools/TagIoProbe (net10.0, コンソール) … どこにも依存しない独立した exe
```

- `Core` は他のどのプロジェクトにも依存しない純粋なドメイン層。UI にもタグライブラリ(TagLibSharp)にも依存しない
- `TagIo` は `Core` の `ITagReader` / `ITagWriter`(`Abstractions/`)を実装する層。TagLibSharp と自前の MP4 パーサに依存する
- `App` は `Core` と `TagIo` の両方を参照する WPF アプリケーション本体
- `tools/TagIoProbe` はソリューション(`MusicTagAuditor.slnx`)には含まれるが、`src/` の各プロジェクトへの参照は持たない完全に独立したスパイクツール。詳細は [docs/TAG_IO_PROBE.md](TAG_IO_PROBE.md)

この一方向依存(`App → TagIo → Core`)により、検査ルールや辞書などのドメインロジックは実際のタグライブラリを差し替えても影響を受けない。

---

## `MusicTagAuditor.Core`

タグの検査・正規化・辞書・バックアップなど、UI にもタグ入出力の実装にも依存しないドメインロジック一式。

| フォルダ | 役割 |
|---|---|
| `Abstractions/` | `ITagReader` / `ITagWriter`。タグ入出力の抽象インターフェース。`TagIo` プロジェクトが実装する |
| `Applying/` | 検査結果・手編集の差分をタグへ適用する処理(`ApplyService`)とその入出力モデル |
| `Backup/` | タグのスナップショット取得(`SnapshotService`)・復元(`RestoreService`)。音声ファイル本体は複製せず、タグのみを JSON化する。保存先は注入された取得関数で解決し(既定はライブラリ直下)、`Settings/` への依存は持たない |
| `Dictionary/` | 正規化辞書のロード・編集・検証・マージ・保存(`DictionaryLoader` / `DictionaryEditor` / `DictionaryValidator` / `DictionaryMerger` / `DictionaryWriter` など)。辞書に無い値の収集(`UnknownValueCollector`)、手編集の入力候補の組み立てと絞り込み(`DictionarySuggester`)も含む |
| `Editing/` | 手編集セットの保持・検証(`ManualEditSet` / `ManualEditValidator`) |
| `Export/` | 検査結果差分の CSV 出力(`ChangeCsvExporter`) |
| `Inspection/` | 検査エンジン本体(`InspectionEngine`)と付随ロジック(`ComposerFinder` / `ConductorFinder` / `MojibakeDetector` / `DiacriticCandidates` など)。ルール本体は `Inspection/Rules/` にまとめている |
| `Inspection/Rules/` | 検査ルール24件の実装。`AlbumRules` / `BasicFieldRules` / `MissingValueRules` / `NormalizationRules` / `PerformerContentRules` / `TitleRules` の6ファイルに分類されている |
| `Models/` | ドメインモデル(`TrackTags` / `TagField` / `TagChange` / `AudioFormat` / `VerificationMismatch`) |
| `Normalization/` | 正規化キーの生成(`NormalizationKey`)。NFKC・小文字化・ひらがな→カタカナ変換などを行う |
| `Scanning/` | ライブラリのフォルダ・ファイル走査(`LibraryScanner`) |
| `Settings/` | 利用者が選べる設定の保持と永続化(`AppSettings` / `AppSettingsStore`)。`%APPDATA%\MusicTagAuditor\settings.json` に読み書きする。現在の項目はバックアップ先と前回開いていたライブラリ |

検査ルールの各論(判定条件・除外規則など)は [docs/SPEC.md](SPEC.md) 6章、原則は [docs/TAGGING_POLICY.md](TAGGING_POLICY.md) を参照。

---

## `MusicTagAuditor.TagIo`

`Core` の `ITagReader` / `ITagWriter` を実装するタグ入出力層。

| ファイル | 役割 |
|---|---|
| `TagReader.cs` | タグの読み取り実装。TagLibSharp を利用 |
| `TagWriter.cs` | タグの書き込み実装。TagLibSharp を利用 |
| `Mp4/Mp4Atom.cs` | MP4 (M4A) のボックス構造を表す自前モデル |
| `Mp4/Mp4AtomReader.cs` | MP4 の `moov` 配下だけをシークして読む自前パーサ |
| `TagIoConst.cs` | 定数(atom バイト列など) |

自前の MP4 パーサを持つ理由は TagLibSharp の制約による(詳細は [docs/DEVELOPMENT.md](DEVELOPMENT.md)「タグ入出力の実装上の注意」および [docs/adr/0001-tag-io-library.md](adr/0001-tag-io-library.md)):

- TagLibSharp の MP4 読み取りは値を `; ` で分割してしまうため、「1値に `;` を含む状態」と「複数値に分割済みの状態」を区別できない
- ファイル全体を読むと 1,000 ファイル規模のスキャンで非機能要件(10秒以内)を満たせない。タグは `moov` 配下にしかないため、そこだけをシークして読む

このライブラリ選定と自前パーサ実装は、`tools/TagIoProbe` での実測検証を経て決定されたもの([docs/TAG_IO_PROBE.md](TAG_IO_PROBE.md) 参照)。

---

## `MusicTagAuditor.App`

WPF / MVVM によるデスクトップアプリケーション本体。

| フォルダ・ファイル | 役割 |
|---|---|
| `ViewModels/` | 画面ごとの ViewModel(`MainViewModel` / `DictionaryViewModel` / `AddToDictionaryViewModel` / `FolderNodeViewModel` / `TrackRowViewModel` / `RuleResultViewModel` / `TagChangeViewModel` / `BackupEntryViewModel` / `DictionaryRowViewModels`)。**グリッドに束ねる行は Core のモデルを直接使わずここでラップする**(理由は [docs/DEVELOPMENT.md](DEVELOPMENT.md)「グリッドに束ねるのは ViewModel にする」)。`RuleResultViewModel` は検査結果の絞り込み範囲(`SetScope` / `ScopedChanges`)も担い、件数と一括操作の対象を範囲内に揃える。`GridViewRefresher` はグリッドの編集中に絞り込みを掛け直すための補助クラスで、同フォルダに置いている(ファイル一覧と検査結果の差分明細で 1 つずつ使う) |
| `Converters/` | XAML バインディング用のコンバータ群(Enum⇔bool/Visibility、件数⇔Visibility、タグフィールド名のラベル変換など) |
| `Controls/Placeholder.cs` | `TextBox` にプレースホルダ文字列を持たせる添付プロパティ |
| `Controls/SuggestBox.cs` | 辞書の候補を絞り込みながら出す入力欄(`TextBox` 派生)。挙動だけを持ち、見た目と候補一覧のポップアップは `Themes/DarkTheme.xaml` のテンプレート(`PART_Popup` / `PART_Suggestions`)が担う。照合ロジックも持たず `Core` の `DictionarySuggester` に委ねる。**暗黙スタイルは派生型に当たらないため、テーマ側に `ctl:SuggestBox` のスタイルが必須**(詳細は [docs/DEVELOPMENT.md](DEVELOPMENT.md)「派生コントロールにはスタイルを明示する」) |
| `Interop/DwmDarkTitleBar.cs` | Windows のタイトルバーをダークテーマ化する DWM 呼び出し |
| `Themes/DarkTheme.xaml` | 配色・角丸・フォントなどのデザイントークン一式 |
| `Assets/` | アプリアイコン(`.ico`)とその元データ(`.svg`) |
| `MainWindow.xaml(.cs)` | メイン画面 |
| `AddToDictionaryWindow.xaml(.cs)` | 検査結果・手編集から辞書へ値を追加するダイアログ |
| `MergeDictionaryWindow.xaml(.cs)` | 既定辞書からの取り込みダイアログ |
| `App.xaml(.cs)` | アプリケーションエントリポイント。DI コンテナ・Serilog の初期化 |
| `AppConst.cs` | アプリ層の定数 |

画面配色・アイコン・プレースホルダの仕様は [docs/DEVELOPMENT.md](DEVELOPMENT.md)「画面」節を参照。

---

## テストプロジェクトとの対応

| プロジェクト | 対応する実装 | 備考 |
|---|---|---|
| `tests/MusicTagAuditor.Core.Tests` | `MusicTagAuditor.Core` | 検査ルール・辞書・バックアップ・適用処理などドメインロジックの単体テスト |
| `tests/MusicTagAuditor.TagIo.Tests` | `MusicTagAuditor.TagIo` | タグ読み書きの往復テストに加え、`Integration/` 配下に実ライブラリを対象にした結合テストを持つ。対象パスは環境変数 `MUSICTAGAUDITOR_LIBRARY_ROOT` で指定し、フォルダが存在しない場合は該当テストをスキップする |
| `tests/MusicTagAuditor.App.Tests` | `MusicTagAuditor.App` | `ViewModels/` の単体テスト(検査結果の選択状態、ルール別集計、検査結果のフォルダ絞り込み、下段の「チェック済みのみ」表示、手編集の差分、`GridViewRefresher` の絞り込み掛け直しなど)。WPF の `CollectionView` はスレッド親和性を持つため、フォルダ選択を伴うテストは `DispatcherTestRunner` で 1 本のディスパッチャスレッドに固定して走らせる |

---

## 関連ドキュメント

| ドキュメント | 内容 |
|---|---|
| [README.md](../README.md) | プロジェクト概要、技術スタック、ビルドと実行、テストとカバレッジ |
| [docs/DEVELOPMENT.md](DEVELOPMENT.md) | 開発ハンドブック。画面仕様、機能ごとの運用ルール、実装上の前提 |
| [docs/SPEC.md](SPEC.md) | アプリケーション仕様(検査ルールの各論、実装段階など) |
| [docs/TAGGING_POLICY.md](TAGGING_POLICY.md) | タグ付けの原則。実装の唯一の基準 |
| [docs/adr/0001-tag-io-library.md](adr/0001-tag-io-library.md) | TagLibSharp 採用の経緯と根拠 |
| [docs/TAG_IO_PROBE.md](TAG_IO_PROBE.md) | `tools/TagIoProbe`(タグ入出力ライブラリ選定の検証ツール)の説明 |
