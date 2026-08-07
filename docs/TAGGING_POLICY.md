# クラシック音楽ライブラリ タグ付け原則

対象ライブラリ: 策定者のローカルライブラリ（1,041ファイル / m4a 516・FLAC 510・AIFF 11・MP3 4）
再生環境: AIMP (Windows)
策定日: 2026-08-02
最終改訂: 2026-08-03（2.3 に配役情報の実値を追記、6.8 曲名中のセミコロンを追加）

---

## 1. この文書の位置づけ

タグ整理の実作業を通じて確定した規則をまとめたもの。以降のタグ付け、および `Music Tag Auditor` アプリの実装は本書を唯一の基準とする。

「検証済み」と明記した項目は、実ファイルと AIMP で実際に確認した結果である。それ以外は方針の宣言であり、事実の記述ではない。

---

## 2. フィールドの役割定義

### 2.1 中核4フィールド

| フィールド | 入れるもの | 例 |
|---|---|---|
| `composer` | 作曲家 | `Johannes Brahms` |
| `artist` | **その録音の主役** | `Yevgeny Mravinsky` / `Karl Böhm` |
| `conductor` | 指揮者 | `Yevgeny Mravinsky` |
| `albumartist` | 演奏団体 | `Leningrad Philharmonic Orchestra` |

### 2.2 `artist` の判定順序

`artist` は「誰の演奏として記憶しているか」を入れる。曲種によって主役が変わるため、次の順に判定する。

1. **協奏曲・独奏付き作品** → ソリスト
   例: ベートーヴェン ピアノ協奏曲第5番 → `artist = Vladimir Ashkenazy`、`conductor = Zubin Mehta`
2. **室内楽・独奏曲** → 演奏者または団体
   例: ベートーヴェン 弦楽四重奏曲第14番 → `artist = Smetana Quartet`（`conductor` は空）
3. **上記以外（交響曲・管弦楽曲・オペラ）** → 指揮者
   例: ブルックナー 交響曲第8番 → `artist = Eugen Jochum`

指揮者がいる録音では、`artist` が誰であっても `conductor` に指揮者を必ず入れる。これにより 1〜3 のどのケースでも指揮者で絞り込める。

### 2.3 `albumartist`

演奏団体名を入れる。指揮者名やソリスト名を連結しない（それらは `artist` / `conductor` にある）。

**例外 — 配役情報の保護**: 歌手・合唱団の配役が記録されている値は、楽団名に縮めると情報が失われるため書き換えない。該当は以下の5種類。

実値は 2026-08-03 に Music Tag Auditor のスキャナで全 1,041 ファイルから抽出したもの（検証済み）。**この表が `dictionary.json` の `protectedAlbumArtists` の生成元となる。**

| 作品 | 件数 | `albumartist` の実値 |
|---|---|---|
| パルジファル（カラヤン） | 29 | `カラヤン/ベルリン・フィル,ベルリン・ドイツ・オペラCho,ホセ・ヴァン・ダム,ヴィクター・フォン・ハーレム,クルト・モル,ペーター・ホフマン etc` |
| マタイ受難曲（ミュンヒンガー） | 1 | `Peter Pears(T); Hermann Prey(BR); Elly Ameling(S); Marga Höffgen(ALT); Fritz Wunderlich(T); Tom Krause(BS); Heinz Blankenburg(BS); August Messthaler(BS); Stuttgarter Hymnus-Chorknaben/Gerhard Wilhelm; Karl Münchinger; Stuttgarter Kammerorchester` |
| 魔弾の射手（C. クライバー） | 1 | `ドレスデン国立歌劇場管弦楽団, カルロス・クライバー, ライプツィヒ放送合唱団 & Horst Neumann` |
| バッハ 管弦楽組曲第2番（W. ベネット参加） | 1 | `William Bennett, Neville Marriner; Academy of St. Martin in the Fields` |
| カンタータ第140番（シュトゥットガルト室内合唱団） | 1 | `Kommerchor Stuttgart(Chorus); Karl Münchinger; Stuttgarter Kammerorchester` |

該当は計 33 ファイル。パルジファル以外はいずれも単発のファイルで、`バッハ` フォルダ直下（アルバム `This Is Bach`）と `魔弾の射手` フォルダにある。

パルジファルと魔弾の射手の値は日本語表記であり 3.1 の言語規則に反するが、**保護が優先する**。ラテン文字化するなら配役の情報量を落とさずに行う必要があり、それは別フィールドを用意してからの作業になる。

**保護対象と紛らわしいが、保護してはならないもの**: `albumartist` に複数の実体が並ぶ値は他にも 2 種ある。いずれも配役情報ではないので、区切り文字の有無だけで保護対象を判定しないこと。

| 値 | 件数 | 実体 | 扱い |
|---|---|---|---|
| `Mozart, Wolfgang Amadeus` | 7 | 作曲家名が `albumartist` に入っている | 修正対象（R-204） |
| `Kirov Orchestra, Mariinsky Theatre` | 6 | 楽団名の表記揺れ | 修正対象（5.3.1 `ru-mariinsky`） |

将来これらを整理する場合は、配役を格納する別フィールドを用意してから行う。

### 2.4 その他

| フィールド | 規則 |
|---|---|
| `genre` | 全ファイル `Classic` に固定（`Classical` ではない） |
| `album` | 「作曲家: 作品名 - 演奏者/年」形式を目標とする（後述 6.1 の未完了事項） |
| `title` | 楽章名。番号書式は後述 6.2 の未完了事項 |
| `date` | 録音年を4桁で。`1993-01-22T08:00:00Z` のような ISO 形式は使わない |
| `discnumber` | 単一ディスクでも `1/1` を設定する（`tracknumber` は対象外。現状は未設定の検査ルールが無い） |

`date` は 3.1 の団体名規則（規則3）の入力でもある。単なる付加情報ではない。

---

## 3. 表記規則

### 3.1 言語

`artist` / `conductor` / `albumartist` / `composer` は**ラテン文字で表記する**。キリル文字・仮名・漢字は用いない。

##### 3.1 補足：なぜラテン文字で統一するか - 判断理由
candidate: `Евгений Александрович Мравинский` / `Ленинградская филармония` / `Дмитрий Дмитриевич Шостакович`

理想を言えばキリル文字表記にしたいところだが、Android Auto の検索機能はラテン文字の頭文字入力を前提としており、キリル文字（日本語と同様）は「その他大勢」に分類され頭文字検索が機能しなくなる。実運用上の検索性を優先し、ラテン文字表記に統一している。

#### 3.1.1 人名

- **ラテン文字圏** — 現地表記を用いる
  `Karl Böhm` / `Rafael Kubelík` / `Václav Smetáček` / `Günter Wand`
- **非ラテン文字圏** — 一般的なラテン転写を用いる
  `Yevgeny Mravinsky` / `Vladimir Fedoseyev` / `Takashi Asahina` / `Seiji Ozawa`

書式（フルネーム・生没年・語順・大文字）については 3.2 を参照。

##### 3.1.1 補足 キリル文字のラテン文字転写
`Yevgeny Mravinsky` は `Yevgeny` か `Evgeny` であるか流儀が分かれる。
ここでは `Evgeny` を選択すると Android Auto 上で `E` を選択した時に `Eugen Jochum` と被るという実務上の理由で `Yevgeny` 表記を採用した。

#### 3.1.2 団体名

1. **ラテン文字圏は現地語の正式名称を優先する。** 英語圏の団体は英語のまま。
2. **非ラテン文字圏は英語圏での一般的な表記を用いる。ラテン転写は用いない。**
   ○ `Moscow Radio Symphony Orchestra`
   × `Bolshoy simfonicheskiy orkestr Vsesoyuznogo radio`（転写）
   × `Всесоюзного радио`（キリル文字）
3. **収録時点での名称を採用する（全言語共通）。** 該当する団体は 5.3.1 に列挙する。
4. **ロシア語圏（旧ソ連圏）の団体に限り**、名誉称号（`Заслуженный коллектив России` 等）および冠名（`имени 〜` ＝「〜名称」）を名称に含めない。
5. **規則3は規則4に優越する。** 収録時点の名称が冠名を含む場合は、冠名ごと採用する。
   例: 1993年以降の録音は `Tchaikovsky Symphony Orchestra`（冠名を含むが規則3が優先）
6. **個別例外は 5.3.2 に列挙する。** 例外は**団体単位**で記述し、規則そのものを一般化しない。

規則1の例:

- ○ `Wiener Philharmoniker` ／ × `Vienna Philharmonic` ／ × `ウィーン・フィルハーモニー管弦楽団`
- ○ `Münchner Philharmoniker` ／ × `Münchener Philharmoniker`
- ○ `Berliner Philharmoniker`
- ○ `Staatskapelle Dresden`
- ○ `Stuttgarter Kammerorchester`

英語圏の団体は英語のまま。

- `Chicago Symphony Orchestra` / `New York Philharmonic` / `London Symphony Orchestra`
- `Academy of St Martin in the Fields`（`St.` のピリオドは付けない）

**補足**: `Berliner Philharmoniker` の旧称は `Berliner Philharmonisches Orchester` だが、2002年の改組以前もレコード録音を行っていた主体は `Berliner Philharmoniker`（民法上の組合）であり、盤面表記も当時から同じである。したがって規則3を適用しても値は変わらない。

### 3.2 人名の書式

- 姓のみは使わない。フルネームで書く。`Brahms` ではなく `Johannes Brahms`
- 生没年を含めない。`Franz LISZT (1811-1886)` ではなく `Franz Liszt`
- 「姓, 名」順にしない。`Mozart, Wolfgang Amadeus` ではなく `Wolfgang Amadeus Mozart`
- 大文字の強調をしない。`LISZT` ではなく `Liszt`

### 3.3 発音区別符号（ウムラウト等）

人名・団体名では正しく付ける。`Böhm` `Münchinger` `Furtwängler` `Günter Wand` `Pešek` `Tomšič` `Kubelík` `Smetáček` `Dvořák` `Antonín`

**曲名中の扱いは未確定**（後述 6.3）。

### 3.4 区切り文字

**`;` を値の区切りに使わない。** AIMP は保存時に `;` を複数値の区切りとして解釈し、1つの値を複数値に分割する（4.3 で検証済み）。連結が必要な場合は 2.3 の方針どおりフィールドを分ける。

---

## 4. フォーマット別の格納先

### 4.1 対応表（検証済み）

| 論理フィールド | M4A (MP4 atom) | FLAC (Vorbis comment) | MP3 / AIFF (ID3v2) |
|---|---|---|---|
| title | `©nam` | `TITLE` | `TIT2` |
| artist | `©ART` | `ARTIST` | `TPE1` |
| albumartist | `aART` | `ALBUMARTIST` | `TPE2` |
| composer | `©wrt` | `COMPOSER` | `TCOM` |
| **conductor** | **`©con`** | `CONDUCTOR` | `TPE3` |
| album | `©alb` | `ALBUM` | `TALB` |
| genre | `©gen` | `GENRE` | `TCON` |
| date | `©day` | `DATE` | `TDRC` |
| track | `trkn` | `TRACKNUMBER`（`3/12` のように番号/総数をまとめて1フィールドに格納） | `TRCK` |
| disc | `disk` | `DISCNUMBER`（同上） | `TPOS` |

### 4.2 conductor の注意点

MP4/M4A には**指揮者の標準 atom が存在しない**。`©con` は規格外の4文字 atom だが、AIMP がこれを採用しているため本ライブラリでも `©con` を用いる。

FLAC の `CONDUCTOR` と ID3v2 の `TPE3` は規格内であり問題ない。

### 4.3 AIMP の実測挙動（2026-08-02 検証）

テストファイルに候補タグを書き込み、AIMP のタグ編集画面で読み書きを確認した結果。

| 検証項目 | 結果 |
|---|---|
| `----:com.apple.iTunes:PERFORMER` 等のフリーフォーム atom | **読まない**（PERFORMER / Performer / CONDUCTOR / Conductor / SOLOISTS / ARTISTS の6種すべて画面に現れない） |
| `©con` | **読み書きとも対応**。AIMP の「指揮者」欄にマップされ、AIMP から保存した値も `©con` に書き戻された |
| `©prf` | 読まない |
| M4A のソリスト／演奏者専用フィールド | **存在しない** |
| `;` 区切りの値 | 保存時に**複数値へ分割される**（`aART` が3要素の配列になった） |

この結果、M4A ではソリストを格納する専用の置き場所がない。2.2 の「協奏曲は `artist` にソリスト、`conductor` に指揮者」という規則は、この制約への対処でもある。

---

## 5. 正規化辞書

### 5.1 作曲家（確定36名）

```
Adolphe Adam / Anatoly Lyadov / Alexander Glazunov / Anton Bruckner /
Anton Rubinstein / Antonín Dvořák / Antonio Vivaldi / Bedřich Smetana /
Carl Maria von Weber / Carl Nielsen / Dmitri Shostakovich / Edvard Grieg /
Felix Mendelssohn / Franz Liszt / Franz Schubert / Georges Bizet /
Gioachino Rossini / Gustav Mahler / Hector Berlioz / Jean Sibelius /
Johann Sebastian Bach / Johannes Brahms / Ludwig van Beethoven /
Mikhail Glinka / Modest Mussorgsky / Nikolai Rimsky-Korsakov /
Otto Nicolai / Pierre Degeyter / Pyotr Ilyich Tchaikovsky /
Richard Strauss / Richard Wagner / Robert Schumann / Sergei Prokofiev /
Vasily Kalinnikov / Wolfgang Amadeus Mozart
```

表記揺れの実例（→ の右が正）:

| 誤 | 正 |
|---|---|
| `Dmitry Shostakovich` / `Domitri Shostakovich` / `Shostakovich` | `Dmitri Shostakovich` |
| `Pyotr Il'yich Tchaikovsky` / `Peter Ilyich Tchaikovsky` | `Pyotr Ilyich Tchaikovsky` |
| `Mozart, Wolfgang Amadeus` / `Mozart` | `Wolfgang Amadeus Mozart` |
| `Johann Sebastian Bach(1685-1750)` | `Johann Sebastian Bach` |
| `Franz LISZT (1811-1886)` | `Franz Liszt` |
| `Kalinnikov , Vasily Sergeevich (1866-1901)` | `Vasily Kalinnikov` |
| `Anton Bruckner; Anton Bruckner` | `Anton Bruckner` |

### 5.2 指揮者・演奏者

| 誤 | 正 |
|---|---|
| `Evgeni Muravinsky` / `Mravinsky` | `Yevgeny Mravinsky` |
| `Leonard Bernsterin` | `Leonard Bernstein` |
| `Vladimir Fedseev` | `Vladimir Fedoseyev` |
| `Gunter Wand` | `Günter Wand` |
| `Karl Böhm; Karl Böhm` | `Karl Böhm` |

日本語表記からの変換例:

| 日本語 | 英語 |
|---|---|
| カラヤン | `Herbert von Karajan` |
| ベーム | `Karl Böhm` |
| ムラヴィンスキー | `Yevgeny Mravinsky` |
| ヨッフム | `Eugen Jochum` |
| ショルティ | `Georg Solti` |
| ヴァント | `Günter Wand` |
| フルトヴェングラー | `Wilhelm Furtwängler` |
| チェリビダッケ | `Sergiu Celibidache` |
| クナッパーツブッシュ | `Hans Knappertsbusch` |
| スクロヴァチェフスキ | `Stanisław Skrowaczewski` |
| クーベリック | `Rafael Kubelík` |
| スメターチェク | `Václav Smetáček` |
| 朝比奈 | `Takashi Asahina` |
| 小澤 | `Seiji Ozawa` |

### 5.3 楽団

誤記・表記揺れの正規化:

| 誤 | 正 |
|---|---|
| `Berlin PhilHarmonic Orchestra / Herbelt Von Karajan` | `Berliner Philharmoniker` |
| `Sinfornieorchester Of The Bavarian Radio` | `Symphonieorchester des Bayerischen Rundfunks` |
| `KIorov Orchestra, Marinsky theatre` | `Kirov Orchestra`（5.3.1 参照） |
| `Saarbrucken Radio Symphony Orchestra` | `Saarbrücken Radio Symphony Orchestra` |
| `Mravinsky / Ussr State So.` | `USSR State Symphony Orchestra` |

#### 5.3.1 時代分割エントリ（3.1 規則3の対象）

収録時点の名称を採用するため、同一楽団が録音年によって異なる値を持つ。**これは表記揺れではないので片寄せしないこと。**

本表は、7.5 の保留判定の対象リスト、および `music-library-search-app` 側エイリアス辞書の生成元を兼ねる。

| 実体ID | 期間 | 採用値 | 備考 |
|---|---|---|---|
| `ru-spb-phil` | 〜1991 | `Leningrad Philharmonic Orchestra` | ЗКР。名誉称号は規則4で除去 |
| | 1991〜 | `Saint Petersburg Philharmonic Orchestra` | |
| `ru-spb-radio` | 〜1991 | `Leningrad Radio Orchestra` | **`ru-spb-phil` とは別団体。束ねないこと** |
| | 1991〜 | `Saint Petersburg Academic Symphony Orchestra` | |
| `ru-mariinsky` | 〜1992 | `Kirov Orchestra` | 劇場名の復称は1992年とされる |
| | 1992〜 | `Mariinsky Theatre Orchestra` | |
| `ru-tchaikovsky-so` | 〜1993 | `Moscow Radio Symphony Orchestra` | |
| | 1993〜 | `Tchaikovsky Symphony Orchestra` | 冠名を含むが規則3優越 |
| `uk-philharmonia` | 〜1964 / 1977〜 | `Philharmonia Orchestra` | |
| | 1964–1977 | `New Philharmonia Orchestra` | |
| `ru-ussr-state` | 全期間 | `USSR State Symphony Orchestra` | ジャケット確認済み。該当は1枚のみのため分割不要 |

**名前の類似で束ねてはならない。** `Leningrad Philharmonic Orchestra` と `Saint Petersburg Philharmonic Orchestra` は名前が全く似ていないが同一実体であり、`Leningrad Philharmonic Orchestra` と `Leningrad Radio Orchestra` は名前が似ているが別団体である。文字列距離ではなく実体IDで判定すること。

#### 5.3.2 個別例外

| 団体 | 採用値 | 例外の内容と理由 |
|---|---|---|
| コンセルトヘボウ | `Concertgebouworkest` | 全期間この値を用い、規則3による時代分割を行わない。1988年に授与された `Koninklijk`（Royal）は付けない。所蔵が授与年の前後にまたがり（例: 1987年のバーンスタイン）、`date` 不明盤が保留に落ちるのを避けるための実務判断。**ラテン文字圏一般に称号除去を拡大するものではない**（`Royal Philharmonic Orchestra` 等は称号を含む形が正名） |

検索側には旧タグ `Royal Concertgebouw Orchestra` および「ロイヤル・コンセルトヘボウ」をエイリアスとして残す。

#### 5.3.3 表記ゆれ（改名ではないもの）

| 誤 | 正 |
|---|---|
| `Münchener Philharmoniker` | `Münchner Philharmoniker` |

※ `Münchener Bach-Chor` / `Münchener Bach-Orchester`（カール・リヒターの団体）は **-ener が正式表記**。`Münchener` を一律に置換しないこと。置換は団体名単位で行う。

### 5.4 楽語のスペルミス

実際に検出された誤りの一覧。検査ルールの初期値として使う。

| 誤 | 正 |
|---|---|
| `Allgro` / `aLLegro` | `Allegro` |
| `Allgretto` / `Alleretto` | `Allegretto` |
| `Adadio` | `Adagio` |
| `Andate` / `Andatente` | `Andante` |
| `andatino` | `andantino` |
| `catabile` | `cantabile` |
| `energio` | `energico` |
| `Majestoso` | `Maestoso` |
| `Iamentoso`（先頭が大文字のI） | `lamentoso`（小文字のL） |
| `Prelue` | `Prelude` |
| `Schezo` | `Scherzo` |
| `lehaft` | `lebhaft` |
| `schenll` / `schell` | `schnell` |
| `Ziemich` | `Ziemlich` |
| `Gemassight` | `Gemäßigt` |
| `Vospiel` | `Vorspiel` |
| `Aufzung` | `Aufzug` |
| `Liedestod` | `Liebestod` |
| `Sympohony` / `Symphone` | `Symphony` |
| `Concerito` | `Concerto` |
| `Qualtet` | `Quartet` |
| `Dnces` | `Dances` |
| `Apotheosys` | `Apotheosis` |
| `Sity` | `City` |
| `Tybalut` | `Tybalt` |
| `Siegfired` | `Siegfried` |
| `murmus` | `murmurs` |
| `Promnade` | `Promenade` |
| `Reconare` | `Recordare` |
| `Riecare` | `Ricercar` |
| `Paresto` | `Presto` |
| `Walz` | `Valse` |
| `Btuckner` | `Bruckner` |
| `Brahmus` | `Brahms` |
| `Knappertsbush` | `Knappertsbusch` |

**照合は正規表現で行うこと。** 完全一致で辞書を引くと、`Finale- Allgro molto` と `Finale: Allgro molto` のような区切り文字違いを取りこぼす（実作業で10件の見落としが発生した）。

ただし**団体名は正規表現の一括置換対象にしない**（5.3.3 の `Münchener` 参照）。本表は楽語に限る。

---

## 6. 未確定・未完了の事項

本書の策定時点で判断を保留した項目。実装時はこれらを「エラー」ではなく「要確認」として扱う。

### 6.1 アルバム名

`Symphony No.5`（43件）のような汎用的な名前に、作曲家も演奏者も異なる複数の録音が同居している。「作曲家: 作品名 - 演奏者/年」形式への移行が望ましいが未着手。

日本語略称（`ベト1`〜`ベト9`、`ブル4`〜`ブル8`、`ショス7`、`チャイ6`、`モツァ25`〜`モツァ41`）と英語名の混在も未解消。

### 6.2 楽章番号の書式

`1.` / `5-1.` / `1 Allegro` / `I.` / `(I. …)` / 番号なし が混在。日本語では `第１楽章`（全角）/ `第1楽章`（半角）/ `第一楽章`（漢数字）が混在。統一方針は未決定。

### 6.3 曲名中のウムラウト

`Walkure` `Gotterdammerung` `Tannhauser` `Freischutz` `Sangerkrieg` `Jagervergnugen` `Konig` `Nurnberg` など。人名・団体名（3.3）とは異なり、CD 原盤が意図的に ASCII 表記である可能性があるため一律変換していない。

### 6.4 指揮者・演奏者が特定できないファイル

21フォルダ 108ファイル。CD 実物の確認が必要。`albumartist` に作曲家名が残っているもの（約99件）も、楽団名が不明のため据え置いている。

### 6.5 composer 空欄 26件

シベリウス21件（`artist` に `Sibelius` / `Siberius` とあるため `Jean Sibelius` で確定可能）、シューベルト2件、ショスタコーヴィチ・ブラームス・ブルックナー 各1件。いずれもフォルダから一意に決まる。

### 6.6 文字化けファイル 4件

`シベリウス/エン・サガ`、`シベリウス/シベリウス 5` の MP3。Shift-JIS が誤解釈された状態で `アルバム情報なし` `アーティスト情報なし` `ジャンル情報なし` `トラック N` が格納されている。実質的にタグ未設定であり、内容の再入力が必要。

### 6.7 ファイル名・フォルダ名

タグとは別に未整理。

- 作曲家の略称と正式名の混在（`ベト7` と `ベートーヴェン 7`、`モツァ25`、`ドボ 8`、`シュベ 9`、`ショス15`）
- 区切りのスペース揺れ（`ブルックナー 4- Wand`、`ブルックナー 8 -ヨッフム`、`シューベルト8 - バーンスタイン`）
- 指揮者名の和洋混在（`Wand` と `ヴァント`、`Young`、`Bongartz`、`チェリー`）
- タグ側で修正した typo がファイル名に残存（`Sympohony No.1-1.flac` など）
- `スラブ` と `スラヴ`、`ドボルザーク` と `ドヴォルザーク`、`ベートーベン` と `ベートーヴェン`

### 6.8 曲名中のセミコロン

`title` に `;` を含むファイルが 7 件ある（2026-08-03 のスキャンで確認）。いずれも区切り記号ではなく、ドイツ語の楽章指示に含まれる正当な句読点である。

| フォルダ | `title` |
|---|---|
| ブルックナー 4- Wand | `3. Scherzo: Bewegt; Trio: Nicht zu schnell. Keinesfalls schleppend` |
| ブルックナー 7 - 朝比奈 | `2. Adagio:; Sehr feierilich und sehr langsam` |
| ブルックナー 8 - Wand | `3. Adagio. Feierlich langsam; doch nicht schleppend` |
| ブルックナー 8 - 朝比奈 | `3. Adagio. Feierlich lansam:; doch nicht schleppend` |
| ブルックナー 9- ヨッフム | `2. Scherzo: Bewegt, Lebhaft - Trio:; Schnell` |
| シュベ 9 | `Andante; Allegro ma non troppo` |
| ワルキューレ/第二幕 | `Zweiter Aufzug: Dritte Szene: Raste Nun Hier ; Gönne Dir Ruh! (Siegmund)` |

問題は 3.4 のとおり **AIMP が保存時に `;` を分割する**こと。これらのファイルを AIMP で編集・保存すると楽章名が壊れる。一方で `;` を除くと楽譜どおりの表記でなくなる。

**方針は未決定。** 現時点では自動修正の対象にせず、一覧として提示するに留める。なお `:;` という並び（ブルックナー 7・8 の朝比奈盤）は明らかに入力ミスであり、こちらは 5.4 と同種の誤記として別途扱える。

---

## 7. 作業手順の原則

1. **変更前に必ずタグのスナップショットを取る。** 音声ファイル本体の複製は不要（本ライブラリは30GB）。全ファイルのタグを JSON に書き出し、復元スクリプトを添えれば完全に巻き戻せる。
2. **変更計画を差分（変更前 / 変更後 / 根拠）の形で提示し、承認を得てから適用する。**
3. **適用後は全項目を読み戻して照合する。** 書き込み成功と、意図した値になっていることは別である。
4. **確信が持てない項目は書き換えず、一覧として残す。** 誤った値で埋めるより空欄または現状維持のほうが、後から対処できる。
5. **収録時点の名称が確定できない場合は保留する。**
   `date` が空欄で、かつ 5.3.1 の時代分割エントリに該当する団体の場合、`albumartist` を書き換えず保留一覧に落とす。
   - 保留理由はコード（`HOLD_ERA_UNKNOWN`）で保持し、`date` が埋まった時点で自動的に再判定できるようにする
   - 5.3.1 に載っていない団体は、`date` が不明でも値が一意に決まるため保留対象にしない
