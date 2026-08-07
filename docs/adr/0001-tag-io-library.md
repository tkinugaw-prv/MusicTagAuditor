# ADR-0001: タグ入出力ライブラリの選定

- 状態: **承認**
- 日付: 2026-08-02
- 対象: `docs/SPEC.md` 4章 / 13章 D1

---

## 背景

Music Tag Auditor のタグ読み書きに使うライブラリを決める。候補は TagLibSharp (TagLib#) と z440.atl.core (ATL.NET)。

`docs/SPEC.md` 4.1 の V1〜V8 を、実ライブラリから複製した検体に対して実測した。判定はライブラリ自身の読み戻しに頼らず、M4A は MP4 ボックスを直接走査したバイナリ結果で行っている（`tools/TagIoProbe/Mp4AtomDumper.cs`）。

### 検体

| ラベル | 複製元 | 特徴 |
|---|---|---|
| M4A | `backup_20260802145251\TAGTEST 皇帝 1st Mov.m4a` | AIMP 検証（`TAGGING_POLICY.md` 4.3）で使ったファイル。`©con` と 6 種のフリーフォーム atom、`;` で分割された `aART` 3 件を含む |
| M4A-covr | ライブラリ内でカバーアートを持つ最初の M4A | V4 用 |
| FLAC | `グラズノフ 5 - ムラヴィンスキー\01 ...flac` | |
| MP3 | `シベリウス\エン・サガ\エン・サガ.mp3` | |
| AIFF | `グリーグ\01 Grieg_ ...aif` | |

再現手順:

```bash
dotnet run --project tools/TagIoProbe/TagIoProbe.csproj -- "D:\Music Library for AIMP\Classic"
```

---

## 実測結果

| # | 確認内容 | TagLib# 2.3.0 | ATL.NET 7.15.3 |
|---|---|---|---|
| V1 | M4A の Conductor を `©con` に書くか | **NG** — `cond` (636F6E64) に書く | **OK** — `©con` (A9636F6E) に書く |
| V2 | 未知の4文字 atom を直接読み書きできるか | **OK** — `AppleTag.SetText`/`GetText` で `©con` を読み書きできた | **NG** — `AdditionalFields["©con"]` への書き込みは反映されない |
| V3 | フリーフォーム atom を保存時に壊さないか | **OK** — 8 件すべて保持 | **一部 NG** — 大文字小文字違いの同名キーを統合し 2 件消失（下記参照） |
| V4 | `covr` を保存時に保持するか | **OK** | **OK** |
| V5 | FLAC `CONDUCTOR` / ID3v2 `TPE3` | **OK** | **OK** |
| V6 | AIFF の ID3 チャンク | **OK** | **OK** |
| V7 | `;` を複数値に分割しないか（書き込み） | **OK** — 全フォーマットで 1 値のまま格納 | **NG** — FLAC / MP3 / AIFF で 2 値に分割して格納 |
| V8 | .NET 10 で動作するか | **OK** — `net10.0` で動作。ただし 2.3.0 は 2021 年公開で更新が停滞 | **OK** — 更新は活発 |

### V3 の詳細

ATL は TAGTEST 検体で `----:com.apple.iTunes:CONDUCTOR` と `----:com.apple.iTunes:Performer` を失った。検体には `PERFORMER` / `Performer`、`CONDUCTOR` / `Conductor` という大文字小文字違いの同名キーが同居しており、ATL はこれを 1 つに統合している。

**これは AIMP 検証用に人為的に作った状態であり、実ライブラリの通常ファイルには無い。** 実ファイル由来の M4A-covr 検体では ATL も `iTunNORM` / `iTunSMPB` を保持した。とはいえ、保存のたびに情報が減る可能性がある点は変わらない。

### V7 の詳細（判断の決め手）

`Peter Pears(T); Hermann Prey(BR)` を `albumartist` に書き込んだときの、**ファイルに実際に格納された値**:

| フォーマット | TagLib# | ATL.NET |
|---|---|---|
| M4A | 1 値 `Peter Pears(T); Hermann Prey(BR)` | 1 値 `Peter Pears(T); Hermann Prey(BR)` |
| FLAC | 1 値（そのまま） | **2 値** `Peter Pears(T)` / ` Hermann Prey(BR)` |
| MP3 | 1 値（そのまま） | **2 値** 同上 |
| AIFF | 1 値（そのまま） | **2 値** 同上 |

ATL は AIMP と同じ「`;` で複数値に分割する」挙動を持つ。`TAGGING_POLICY.md` 2.3 の配役情報（保護対象の 5 種）や 3.4 の区切り文字規則に真っ向から反する。FLAC 510・MP3 4・AIFF 11 の計 525 ファイルが影響範囲になる。

### TagLib# の M4A 読み取りにおける制約

書き込みは全フォーマットで正しいが、**M4A の読み取り側は `; ` で分割する**。`Tag.AlbumArtists` も `AppleTag.GetText` も、1 つの data ボックスに入った `Peter Pears(T); Hermann Prey(BR)` を 2 要素として返した。

これは値を失うわけではない（`"; "` で連結し直せば復元できる）が、**「1 値に `;` が含まれる状態」と「AIMP に分割された複数値の状態」を区別できない**。R-205（値に `;` が含まれる）と R-206（同一値の重複連結）の検出には、この区別が必要である。

---

## 決定

**TagLibSharp を採用する。`docs/SPEC.md` 4.2 の案 C（TagLib# を使いつつ `©con` を直接扱う）を取る。**

M4A については以下の構成にする。

- **書き込み**: TagLib# の `AppleTag.SetText(©con, value)` を使う。汎用の `Tag.Conductor` は**使わない**（`cond` に書かれ、AIMP から見えなくなる）。既存ファイルに `cond` が残っていれば削除する。
- **読み取り**: **自前の MP4 atom リーダーを使う**（`tools/TagIoProbe/Mp4AtomDumper.cs` を `MusicTagAuditor.TagIo` に移す）。data ボックス単位の値をそのまま取得できるため、`;` の分割状態を正しく検出できる。

FLAC / MP3 / AIFF は TagLib# の標準 API で読み書きする（V5・V6・V7 いずれも問題なし）。

### 理由

1. **V7 が決定的。** ATL は 525 ファイルで `;` を分割して保存する。原則（`TAGGING_POLICY.md` 3.4）に反する挙動を持つライブラリを土台にはできない。回避策は「`;` を含む値を一切書かない」しかないが、保護対象の配役情報がまさに `;` を含む。
2. **V1 の不利は回避可能。** TagLib# の `©con` 非対応は V2 が OK であることから完全に埋められる。逆に ATL の V7 は、ライブラリの書き込み経路そのものの挙動なので回避しにくい。
3. **V3 の堅牢性。** 未知タグを失わないことは `SPEC.md` 10章の `RawTags` 要件の前提になる。
4. M4A 読み取りの自前実装は、すでに Probe で動作するものがあり追加コストが小さい。しかも `;` 分割状態の検出という**要件そのもの**に必要である。

### 残るリスク

| リスク | 対処 |
|---|---|
| TagLib# 2.3.0 は 2021 年公開で更新が停滞 | `net10.0` での動作は実測済み。読み書きは `ITagReader` / `ITagWriter` の背後に隔離し、差し替え可能にしておく |
| 自前 MP4 リーダーの実装バグ | `MusicTagAuditor.TagIo.Tests` で往復テストを行う。書き込みは TagLib# に任せ、読み取りのみ自前とすることで実装範囲を絞る |
| `Tag.Conductor` を誤って使う | `MusicTagAuditor.TagIo` 内で MP4 用の書き込み経路を 1 か所に集約し、テストで `cond` が生成されないことを確認する |

---

## AIMP による裏付け（2026-08-02 実施）

`tools/TagIoProbe/work/taglibsharp/M4A.m4a` を AIMP のタグ編集画面で開いた。このファイルには `©con` = `Sergiu Celibidache` と `cond` = `Yevgeny Mravinsky` の**両方**が入っている。

| AIMP の欄 | 表示された値 | 意味 |
|---|---|---|
| 指揮者 | `Sergiu Celibidache` | **AIMP は `©con` を読み、`cond` を無視する。** V1 の判定が裏付けられた |
| アルバムアーティスト | `Peter Pears(T); Hermann Prey(BR)` | TagLib# が 1 値のまま格納した状態を、AIMP も 1 値として表示する（V7 の裏付け） |
| 作曲 | `Ludwig van Beethoven` | `©wrt` |
| ジャンル / 年 | `Classic` / `1983` | |

これにより、TagLib# の高レベル API（`Tag.Conductor`）をそのまま使うと **AIMP から指揮者が消える**ことが実機で確定した。本 ADR の決定（`©con` への直接書き込み）は必須の対処である。

なお `TAGGING_POLICY.md` 4.3 のとおり、AIMP は**保存時**に `;` を複数値へ分割する。上記のアルバムアーティストは AIMP で保存し直すと壊れるため、Music Tag Auditor 側から書いた値を AIMP で編集・保存しないよう運用で注意する。
