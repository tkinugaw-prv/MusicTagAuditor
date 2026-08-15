using System.Text;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Inspection.Rules;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Tests.Inspection;

/// <summary>
/// 段階 7 で追加した R-3xx / R-4xx / R-5xx と、6.9 由来の R-210 のテスト。
///
/// docs/library-baseline-2026-08-03.md の実測に合わせた判定条件を固定する。
/// </summary>
public sealed class RemainingRuleTests
{
    /// <summary>テストで使うライブラリルート。</summary>
    private const string LIBRARY_ROOT = @"D:\Library";

    /// <summary>既定辞書の索引。</summary>
    private static readonly DictionaryIndex DICTIONARY = new(DictionaryLoader.LoadDefault());

    /// <summary>
    /// R-504 用の辞書。既定辞書に作品エントリと個別例外を足したもの。
    ///
    /// **同梱の既定辞書には作品エントリを入れない**（docs/SPEC.md 13章 D5）。所蔵に依存するため。
    /// ここでは判定の形だけを確かめるので、必要な数件だけを組み立てる。
    /// </summary>
    private static readonly DictionaryIndex WORKS_DICTIONARY = new(DictionaryLoader.LoadDefault() with
    {
        Works =
        [
            new WorkEntry
            {
                Composer = "Anton Bruckner",
                Canonical = "Symphony No. 8",
                Aliases = ["Symphony No.8"],
                AliasesJa = ["ブルックナー 8"],
            },
            new WorkEntry
            {
                Composer = "Franz Schubert",
                Canonical = "Symphony No. 8",
                Aliases = ["Symphony No.8"],
                AliasesJa = ["シューベルト 8"],
            },
            new WorkEntry
            {
                Composer = "Franz Schubert",
                Canonical = "Symphony No. 9",
                Aliases = ["Symphony No.9"],
                AliasesJa = ["シューベルト 9"],
            },
        ],
        AlbumOverrides =
        [
            new AlbumOverrideEntry
            {
                Folder = "リスト 交響詩",
                Exclude = true,
                Note = "3.5 規則6 交響詩 4 曲",
            },
            new AlbumOverrideEntry
            {
                Folder = "シェエラザード",
                Exclude = true,
                Note = "3.5 規則6 複数の作曲家",
            },
            new AlbumOverrideEntry
            {
                Folder = @"ショスタコーヴィチ\オリンピア盤",
                WorkName = "Symphony No. 5 (Olympia)",
                Note = "3.5 規則7 同一演奏の別リリース",
            },
            new AlbumOverrideEntry
            {
                Folder = @"ブルックナー\ブルックナー 8 - 主作品とカップリング",
                Date = "1993",
                Note = "3.5 規則2・規則5 主作品は交響曲第8番",
            },
            new AlbumOverrideEntry
            {
                Folder = @"ブルックナー\ブルックナー 8 - 年が直った",
                Date = "1994",
                Note = "タグを直したあとに残った古い年の指定",
            },
        ],
    });

    /// <summary>ブルックナー 8 番の 1 ファイル分のタグ。R-504 のテストで繰り返し使う。</summary>
    private static readonly (TagField Field, string[] Values)[] BRUCKNER_8 =
    [
        (TagField.Composer, ["Anton Bruckner"]),
        (TagField.Album, ["Symphony No.8"]),
        (TagField.Artist, ["Günter Wand"]),
        (TagField.Date, ["1993"]),
    ];

    /// <summary>
    /// R-301: 辞書の誤記を置換することを確認する。
    /// **区切り文字が違っても取りこぼさない**（docs/TAGGING_POLICY.md 5.4）。
    /// </summary>
    [Theory]
    [InlineData("Finale- Allgro molto", "Finale- Allegro molto")]
    [InlineData("Finale: Allgro molto", "Finale: Allegro molto")]
    [InlineData("2. Allgretto", "2. Allegretto")]
    public void FixesTypoRegardlessOfSeparator(string before, string after)
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("01.flac", (TagField.Title, [before]))), "R-301"));

        Assert.Equal(after, change.AfterText);
        Assert.True(change.IsSelected);
    }

    /// <summary>
    /// R-301: 団体名を誤記の置換対象にしないことを確認する。
    /// <c>Münchener Bach-Chor</c> の -ener は正しい表記である（5.3.3）。
    /// </summary>
    [Fact]
    public void DoesNotApplyTyposToPerformerFields()
    {
        InspectionResult result = Inspect(
            Track("01.flac",
                (TagField.AlbumArtist, ["Münchener Bach-Chor"]),
                (TagField.Artist, ["Brahmus"])));

        Assert.Empty(ChangesOf(result, "R-301"));
    }

    /// <summary>
    /// R-302: 曲名の末尾から拡張子を落とすことを確認する。
    /// </summary>
    [Fact]
    public void RemovesExtensionFromTitle()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("01.flac", (TagField.Title, ["Sympohony No.1-1.flac"]))), "R-302"));

        Assert.Equal("Sympohony No.1-1", change.AfterText);
    }

    /// <summary>
    /// R-303: プレースホルダを検出し、ファイル名から補完することを確認する。
    /// 先頭のトラック番号は落とす。
    /// </summary>
    [Theory]
    [InlineData("ショス15/02 Symphony No.15 in A Major.flac", "ショス15 - 02", "Symphony No.15 in A Major")]
    [InlineData("マーラー1/01 1. Langsam, Schleppend.m4a", "マーラー1 - 01", "1. Langsam, Schleppend")]
    [InlineData("チャイ6/2-08 Adagio - Allegro non troppo.flac", "チャイ6 - 01", "Adagio - Allegro non troppo")]
    [InlineData("x/Track04.flac", "Track04", null)]
    public void CompletesPlaceholderTitleFromFileName(string path, string title, string? expected)
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track(path, (TagField.Title, [title]))), "R-303"));

        if (expected is null)
        {
            Assert.False(change.HasFix);
            return;
        }

        Assert.Equal(expected, change.AfterText);
    }

    /// <summary>
    /// R-303: 補完できても**既定ではチェックしない**ことを確認する。
    ///
    /// ファイル名には Windows で使えない文字の代替が混じっており、1 件ずつ人間が見て決める。
    /// 区分も「確定」ではなく「要確認」でなければ、チェック状態と表示がずれる。
    /// </summary>
    [Fact]
    public void DoesNotSelectFileNameCompletionByDefault()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Inspect(Track("ブル4/01 Symphony No.4.flac", (TagField.Title, ["ブル4 - 01"]))),
                "R-303"));

        Assert.True(change.HasFix);
        Assert.False(change.IsSelected);
        Assert.Equal("要確認", change.Classification);
    }

    /// <summary>
    /// R-303: ファイル名もプレースホルダなら補完しないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotCompleteWhenFileNameIsAlsoPlaceholder()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("シューマン 1/01 1-1.flac", (TagField.Title, ["1-1"]))), "R-303"));

        Assert.False(change.HasFix);
    }

    /// <summary>
    /// R-303: 正当な曲名をプレースホルダとして拾わないことを確認する。
    /// </summary>
    [Theory]
    [InlineData("1. Allegro con brio")]
    [InlineData("Symphony No.5")]
    [InlineData("Adagio - Allegro non troppo")]
    public void DoesNotFlagRealTitleAsPlaceholder(string title)
    {
        Assert.Empty(ChangesOf(Inspect(Track("01.flac", (TagField.Title, [title]))), "R-303"));
    }

    /// <summary>
    /// R-304: 既定では動かないことを確認する（docs/SPEC.md 6.2）。
    /// </summary>
    [Fact]
    public void DiacriticRuleIsDisabledByDefault()
    {
        InspectionResult result = Inspect(Track("01.flac", (TagField.Title, ["Die Walkure - Walkurenritt"])));

        Assert.DoesNotContain(result.Results, rule => rule.RuleId == DiacriticMissingRule.RULE_ID);
    }

    /// <summary>
    /// R-304: 明示的に有効にすると動き、正しい綴りを根拠に出すことを確認する。
    /// **自動修正はしない。** 原盤が意図的に ASCII 表記の可能性がある。
    /// </summary>
    [Fact]
    public void DiacriticRuleReportsCandidateWithoutFixing()
    {
        InspectionResult result = Inspect(
            [Track("01.flac", (TagField.Title, ["Die Walkure - The ride of the Valkyries"]))],
            enableDiacritic: true);

        TagChange change = Assert.Single(ChangesOf(result, DiacriticMissingRule.RULE_ID));

        Assert.False(change.HasFix);
        Assert.Contains("Walküre", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-401: 同一フォルダ / artist / パス の順に作曲家を特定することを確認する。
    /// </summary>
    [Fact]
    public void FindsComposerFromSibling()
    {
        InspectionResult result = Inspect(
            Track("ブラームス/ブラ1/01.flac", (TagField.Composer, ["Johannes Brahms"])),
            Track("ブラームス/ブラ1/02.flac"));

        TagChange change = Assert.Single(ChangesOf(result, "R-401"));

        Assert.Equal("Johannes Brahms", change.AfterText);
        Assert.Contains("同一フォルダ", change.Rationale, StringComparison.Ordinal);
        Assert.True(change.IsSelected);
    }

    /// <summary>
    /// R-401: <c>artist</c> に残った作曲家名から特定することを確認する。
    /// 実ライブラリのシベリウス 21 件がこの経路（docs/TAGGING_POLICY.md 6.5）。
    /// </summary>
    [Fact]
    public void FindsComposerFromArtist()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("シベリウス/シベリウス 2/01.flac", (TagField.Artist, ["Siberius"]))), "R-401"));

        Assert.Equal("Jean Sibelius", change.AfterText);
    }

    /// <summary>
    /// R-401: パスのフォルダ名から特定することを確認する。
    /// </summary>
    [Fact]
    public void FindsComposerFromFolderName()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("シベリウス/エン・サガ/01.mp3")), "R-401"));

        Assert.Equal("Jean Sibelius", change.AfterText);
        Assert.Contains("フォルダ名", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-401: 手がかりが無ければ修正値を出さないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotGuessComposerWithoutEvidence()
    {
        TagChange change = Assert.Single(ChangesOf(Inspect(Track("その他/01.flac")), "R-401"));

        Assert.False(change.HasFix);
    }

    /// <summary>
    /// R-402: **指揮者が居ないのが正しい録音を検出しない**ことを確認する
    /// （docs/TAGGING_POLICY.md 2.2）。
    ///
    /// これを外すと実ライブラリで 22 件（I Musici / Smetana Quartet）を誤検出する。
    /// </summary>
    [Theory]
    [InlineData("I Musici")]
    [InlineData("Smetana Quartet")]
    [InlineData("Peter Hurford")]
    public void DoesNotFlagConductorlessPerformance(string artist)
    {
        Assert.Empty(ChangesOf(Inspect(Track("01.flac", (TagField.Artist, [artist]))), "R-402"));
    }

    /// <summary>
    /// R-402: <c>artist</c> が指揮者なら <c>conductor</c> にも入れることを確認する。
    /// 「指揮者がいる録音では artist が誰であっても conductor に指揮者を必ず入れる」（2.2）。
    /// </summary>
    [Fact]
    public void CopiesConductorFromArtist()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("01.flac", (TagField.Artist, ["Karl Böhm"]))), "R-402"));

        Assert.Equal("Karl Böhm", change.AfterText);
        Assert.True(change.IsSelected);
    }

    /// <summary>
    /// R-402: 手がかりが無ければ修正値を出さないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotGuessConductorWithoutEvidence()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("ワーグナー/Wagner Opera Choruses/01.m4a", (TagField.Artist, ["Richard Wagner"]))), "R-402"));

        Assert.False(change.HasFix);
    }

    /// <summary>
    /// R-403: Shift-JIS の誤解釈を検出し、元の日本語を根拠に出すことを確認する。
    ///
    /// 既知の文字列との照合では引っかからない。検出方式は
    /// docs/library-baseline-2026-08-03.md の追記で確定したもの。
    /// </summary>
    [Fact]
    public void DetectsMojibakeAndDecodesOriginal()
    {
        string garbled = Garble("アーティスト情報なし");

        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("01.mp3", (TagField.Artist, [garbled]))), "R-403"));

        Assert.Contains("アーティスト情報なし", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-403: 修正案は「値を消す」で、**既定ではチェックしない**ことを確認する。
    /// 実質的にタグ未設定であり、消すか入れ直すかは人間が決める。
    /// </summary>
    [Fact]
    public void ProposesClearingMojibakeWithoutSelecting()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("01.mp3", (TagField.Title, [Garble("トラック 1")]))), "R-403"));

        Assert.True(change.ClearsValue);
        Assert.True(change.HasFix);
        Assert.False(change.IsSelected);
        Assert.Equal("要確認", change.Classification);
    }

    /// <summary>
    /// R-403: 正しく読めている値を文字化けとして拾わないことを確認する。
    /// </summary>
    [Theory]
    [InlineData("カラヤン")]
    [InlineData("Karl Böhm")]
    [InlineData("Symphony No.5")]
    [InlineData("Antonín Dvořák")]
    public void DoesNotFlagReadableValueAsMojibake(string value)
    {
        Assert.Empty(ChangesOf(Inspect(Track("01.flac", (TagField.Title, [value]))), "R-403"));
    }

    /// <summary>
    /// R-210: ファイル名に <c>composer</c> と違う作曲家名が出てくることを検出する。
    ///
    /// docs/TAGGING_POLICY.md 6.9 の実例。演奏会 1 回分のプログラムが 1 フォルダに入っており、
    /// 全ファイルが主作品の作曲家で埋められていた。
    /// </summary>
    [Fact]
    public void DetectsComposerMismatchInFileName()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Inspect(Track(
                    "ショスタコーヴィチ/ショス5 - ムラヴィンスキー/01 Weber Oberon.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]))),
                "R-210"));

        Assert.Contains("Carl Maria von Weber", change.Rationale, StringComparison.Ordinal);
        Assert.Contains("Dmitri Shostakovich", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-210: ファイル名と曲名が同じ作曲家を指すとき、根拠にその名前を 2 回書かないことを確認する。
    ///
    /// 実ライブラリの該当はすべてこの形で、分けて書くと同じ名前が根拠列に並ぶ。
    /// 根拠は読めることが要件である（docs/SPEC.md 5.3）。
    /// </summary>
    [Fact]
    public void MergesRationaleWhenFileNameAndTitleAgree()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Inspect(Track(
                    "x/01 Weber Oberon.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]),
                    (TagField.Title, ["01 Weber Oberon"]))),
                "R-210"));

        Assert.Contains("ファイル名・曲名「01 Weber Oberon」", change.Rationale, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(change.Rationale, "Carl Maria von Weber"));
    }

    /// <summary>
    /// R-210: ファイル名と曲名が別の作曲家を指すときは、両方を出所ごとに並べることを確認する。
    /// </summary>
    [Fact]
    public void ListsBothSourcesWhenTheyDisagree()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Inspect(Track(
                    "x/01 Weber Oberon.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]),
                    (TagField.Title, ["Schubert Sym8-1 Allegro moderato"]))),
                "R-210"));

        Assert.Contains("ファイル名「01 Weber Oberon」に「Carl Maria von Weber」", change.Rationale, StringComparison.Ordinal);
        Assert.Contains("曲名「Schubert Sym8-1 Allegro moderato」に「Franz Schubert」", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-210: <c>title</c> 側の作曲家名でも検出することを確認する。
    /// </summary>
    [Fact]
    public void DetectsComposerMismatchInTitle()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Inspect(Track(
                    "ショスタコーヴィチ/ショス5 - ムラヴィンスキー/02.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]),
                    (TagField.Title, ["Schubert Sym8-1 Allegro moderato"]))),
                "R-210"));

        Assert.Contains("Franz Schubert", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-210: **修正案を出さず、既定でチェックしない**ことを確認する。
    ///
    /// 曲名が別の作曲家名を正当に含む作品があり、この判別は機械ではできない。
    /// 人間に判断を促すためだけのルールである（docs/SPEC.md 6.2）。
    /// </summary>
    [Fact]
    public void DoesNotProposeFixForComposerMismatch()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Inspect(Track("x/01 Weber Oberon.flac", (TagField.Composer, ["Dmitri Shostakovich"]))),
                "R-210"));

        Assert.False(change.HasFix);
        Assert.False(change.IsSelected);
        Assert.Equal("要確認", change.Classification);
    }

    /// <summary>
    /// R-210: <c>composer</c> と同じ作曲家名なら拾わないことを確認する。
    /// 表記揺れも同一人物として扱う（正規形に寄せてから比べる）。
    /// </summary>
    [Theory]
    [InlineData("Johannes Brahms")]
    [InlineData("Brahms")]
    public void DoesNotFlagSameComposerInFileName(string composer)
    {
        Assert.Empty(
            ChangesOf(
                Inspect(Track("x/01 Brahms Symphony No.1.flac", (TagField.Composer, [composer]))),
                "R-210"));
    }

    /// <summary>
    /// R-210: 団体名に含まれる作曲家の姓を拾わないことを確認する。
    ///
    /// <c>Münchener Bach-Chor</c> は語に割ると <c>Bach</c> が出てくる。**割る前に辞書で団体名を外す**
    /// （docs/SPEC.md 6.2 / <c>DictionaryIndex.ContainsComposerName</c> と同じ扱い）。
    /// </summary>
    [Fact]
    public void DoesNotFlagEnsembleNameContainingComposerSurname()
    {
        Assert.Empty(
            ChangesOf(
                Inspect(Track(
                    "x/01.flac",
                    (TagField.Composer, ["Johannes Brahms"]),
                    (TagField.Title, ["Münchener Bach-Chor"]))),
                "R-210"));
    }

    /// <summary>
    /// R-210: 辞書に無い作曲家名は検出しないことを確認する。
    ///
    /// ブラームス『ハイドンの主題による変奏曲』は <c>composer</c> が正しく、曲名の <c>Haydn</c> は
    /// 作品名の一部である。現時点でハイドンが辞書（docs/TAGGING_POLICY.md 5.1）に無いため検出されない。
    /// **辞書にハイドンを足した時点で誤検出に変わる。** この性質をここに固定しておく。
    /// </summary>
    [Fact]
    public void DoesNotDetectComposerOutsideDictionary()
    {
        Assert.Empty(
            ChangesOf(
                Inspect(Track(
                    "ブラームス 1 - ベーム/05 Variation uber ein Thema von Joseph Haydn.flac",
                    (TagField.Composer, ["Johannes Brahms"]))),
                "R-210"));
    }

    /// <summary>
    /// R-210: フォルダ名の作曲家名でも検出することを確認する。
    ///
    /// 実ライブラリの `チャイコフスキー\チャイコフスキー 6 - ムラヴィンスキー 1982` が該当する。
    /// ファイル名にも曲名にも作曲家名が出てこないため、フォルダ名を見ないと拾えない。
    /// </summary>
    [Fact]
    public void DetectsComposerMismatchInFolderName()
    {
        IReadOnlyList<TagChange> changes = ChangesOf(
            Inspect(
                Track("チャイコフスキー/チャイコフスキー 6 - ムラヴィンスキー 1982/01.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]),
                    (TagField.Title, ["1. Adagio - Allegro non troppo"])),
                Track("チャイコフスキー/チャイコフスキー 6 - ムラヴィンスキー 1982/02.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]),
                    (TagField.Title, ["2. Allegro con grazia"]))),
            "R-210");

        // フォルダ全体が同じ状態なので、単位内の全ファイルが出る。
        Assert.Equal(2, changes.Count);
        Assert.All(changes, change =>
        {
            Assert.Contains("Pyotr Ilyich Tchaikovsky", change.Rationale, StringComparison.Ordinal);
            Assert.Contains("フォルダ", change.Rationale, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// R-210: フォルダ名が <c>composer</c> と一致していれば拾わないことを確認する。
    ///
    /// **これが最も多い形である。** 拾ってしまうとライブラリのほぼ全ファイルが検出される。
    /// </summary>
    [Fact]
    public void DoesNotFlagFolderNameMatchingComposer()
    {
        Assert.Empty(
            ChangesOf(
                Inspect(Track("ブルックナー/ブルックナー 3 - ベーム/01.flac",
                    (TagField.Composer, ["Anton Bruckner"]),
                    (TagField.Title, ["1. Mehr langsam, Misterioso"]))),
                "R-210"));
    }

    /// <summary>
    /// R-210: カップリング盤の併録曲をフォルダ名で誤検出しないことを確認する。
    ///
    /// フォルダ名は主作品の作曲家を名乗るので、併録曲は「フォルダ名と違う作曲家」になるのが
    /// **正しい**（docs/TAGGING_POLICY.md 3.5 規則5）。**フォルダ内の composer が 1 種類のときだけ**
    /// フォルダ名を手がかりにすることで除外する。これを外すと該当 4 単位が丸ごと誤検出になる。
    /// </summary>
    [Fact]
    public void DoesNotFlagCouplingInMultiComposerFolder()
    {
        Assert.Empty(
            ChangesOf(
                Inspect(
                    Track("ドヴォルザーク/ドヴォルザーク 9 - カラヤン/01.flac",
                        (TagField.Composer, ["Antonín Dvořák"]),
                        (TagField.Title, ["1. Adagio - Allegro molto"])),
                    Track("ドヴォルザーク/ドヴォルザーク 9 - カラヤン/05.flac",
                        (TagField.Composer, ["Bedřich Smetana"]),
                        (TagField.Title, ["Vltava"]))),
                "R-210"));
    }

    /// <summary>
    /// R-210: <c>composer</c> 未設定のファイルを拾わないことを確認する。
    ///
    /// 6.9 は「値が辞書の正規形と一致するのに誤り」という状態を指す。未設定は R-401 が扱うため、
    /// ここで拾うと同じファイルが二重に明細へ出る。
    /// </summary>
    [Fact]
    public void DoesNotFlagFileWithoutComposer()
    {
        Assert.Empty(ChangesOf(Inspect(Track("x/01 Weber Oberon.flac")), "R-210"));
    }

    /// <summary>
    /// R-501: 同名アルバムに複数の作曲家が混在していることを検出する。
    /// </summary>
    [Fact]
    public void DetectsAlbumNameCollision()
    {
        InspectionResult result = Inspect(
            Track("a/01.flac", (TagField.Album, ["Symphony No.5"]), (TagField.Composer, ["Ludwig van Beethoven"])),
            Track("b/01.flac", (TagField.Album, ["Symphony No.5"]), (TagField.Composer, ["Anton Bruckner"])));

        Assert.Equal(2, ChangesOf(result, "R-501").Count);
        Assert.All(ChangesOf(result, "R-501"), change => Assert.False(change.HasFix));
    }

    /// <summary>
    /// R-501: 表記揺れを人数に数えないことを確認する。
    /// <c>Pyotr Il'yich Tchaikovsky</c> と <c>Pyotr Ilyich Tchaikovsky</c> は同一人物。
    /// </summary>
    [Fact]
    public void DoesNotCountSpellingVariantsAsDifferentComposers()
    {
        InspectionResult result = Inspect(
            Track("a/01.flac", (TagField.Album, ["Symphony No.5"]), (TagField.Composer, ["Pyotr Il'yich Tchaikovsky"])),
            Track("b/01.flac", (TagField.Album, ["Symphony No.5"]), (TagField.Composer, ["Pyotr Ilyich Tchaikovsky"])));

        Assert.Empty(ChangesOf(result, "R-501"));
    }

    /// <summary>
    /// R-502: 日本語のアルバム名を検出することを確認する。**略称に絞らない。**
    /// </summary>
    [Theory]
    [InlineData("マーラー2")]
    [InlineData("歌劇 「ローエングリン」 (ケンペ)")]
    [InlineData("Vivaldi:四季")]
    public void DetectsJapaneseAlbumName(string album)
    {
        Assert.Single(ChangesOf(Inspect(Track("01.flac", (TagField.Album, [album]))), "R-502"));
    }

    /// <summary>
    /// R-502: 略称と判別できたら作曲家の正規形を根拠に出すことを確認する。
    /// </summary>
    [Fact]
    public void ResolvesComposerForAbbreviatedAlbumName()
    {
        TagChange change = Assert.Single(
            ChangesOf(Inspect(Track("01.flac", (TagField.Album, ["ベト7"]))), "R-502"));

        Assert.Contains("Ludwig van Beethoven", change.Rationale, StringComparison.Ordinal);
        Assert.Contains("第 7 番", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-502: ラテン文字だけのアルバム名を拾わないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotFlagLatinAlbumName()
    {
        Assert.Empty(ChangesOf(Inspect(Track("01.flac", (TagField.Album, ["Symphony No.5"]))), "R-502"));
    }

    /// <summary>
    /// R-503: フォルダ内で楽章番号の書式が混在していることを検出する。
    /// </summary>
    [Fact]
    public void DetectsMixedMovementNumberStyles()
    {
        InspectionResult result = Inspect(
            Track("ブル8/01.flac", (TagField.Title, ["1. Allegro moderato"])),
            Track("ブル8/02.flac", (TagField.Title, ["Scherzo. Allegro moderato"])));

        Assert.Equal(2, ChangesOf(result, "R-503").Count);
        Assert.All(ChangesOf(result, "R-503"), change => Assert.False(change.HasFix));
    }

    /// <summary>
    /// R-503: 書式が揃っているフォルダを拾わないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotFlagConsistentMovementNumberStyles()
    {
        InspectionResult result = Inspect(
            Track("ブル8/01.flac", (TagField.Title, ["1. Allegro moderato"])),
            Track("ブル8/02.flac", (TagField.Title, ["2. Scherzo"])));

        Assert.Empty(ChangesOf(result, "R-503"));
    }

    /// <summary>
    /// R-504: 3.5 の書式でアルバム名を組み立てることを確認する。
    /// **単位内の全ファイルに同じ値を出す**（規則3）。
    /// </summary>
    [Fact]
    public void BuildsAlbumNameFromWorkEntry()
    {
        InspectionResult result = Works(
            Track("ブルックナー/ブルックナー 8 - Wand/01.flac", BRUCKNER_8),
            Track("ブルックナー/ブルックナー 8 - Wand/02.flac", BRUCKNER_8));

        IReadOnlyList<TagChange> changes = ChangesOf(result, "R-504");

        Assert.Equal(2, changes.Count);
        Assert.All(changes, change =>
        {
            Assert.Equal("Anton Bruckner: Symphony No. 8 - 1993/Günter Wand", change.AfterText);
            Assert.True(change.IsSelected);
            Assert.Equal("確定", change.Classification);
        });
    }

    /// <summary>
    /// R-504: 複数ディスクは別の単位になるが、同じアルバム名になることを確認する（3.5 規則3）。
    /// </summary>
    [Fact]
    public void GivesSameAlbumNameToEveryDiscOfOneAlbum()
    {
        InspectionResult result = Works(
            Track("ブルックナー/ブルックナー 8 - Wand/01.flac", [.. BRUCKNER_8, (TagField.DiscNumber, ["1/2"])]),
            Track("ブルックナー/ブルックナー 8 - Wand/05.flac", [.. BRUCKNER_8, (TagField.DiscNumber, ["2/2"])]));

        Assert.All(
            ChangesOf(result, "R-504"),
            change => Assert.Equal("Anton Bruckner: Symphony No. 8 - 1993/Günter Wand", change.AfterText));
    }

    /// <summary>
    /// R-504: フォルダ名だけからでも作品を引けることを確認する。
    /// フォルダ名には演奏者が付いているので「最初の <c>-</c> より前」でも引く。
    /// </summary>
    [Fact]
    public void FindsWorkFromFolderNameWithoutAlbumTag()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Works(Track("ブルックナー/ブルックナー 8 - Wand/01.flac",
                    (TagField.Composer, ["Anton Bruckner"]),
                    (TagField.Artist, ["Günter Wand"]),
                    (TagField.Date, ["1993"]))),
                "R-504"));

        Assert.Equal("Anton Bruckner: Symphony No. 8 - 1993/Günter Wand", change.AfterText);
        Assert.Contains("フォルダ名", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-504: <c>album</c> とフォルダ名が別の作品を指すなら保留することを確認する。
    ///
    /// **<c>album</c> だけを信用しない。** 実ライブラリには `シューベルト 9` のフォルダに
    /// `Schubert Symphony No.8` が付いた例がある（docs/TAGGING_POLICY.md 3.5 補足2）。
    /// </summary>
    [Fact]
    public void HoldsWhenAlbumAndFolderDisagree()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Works(Track("シューベルト/シューベルト 9 - ベーム/01.flac",
                    (TagField.Composer, ["Franz Schubert"]),
                    (TagField.Album, ["Symphony No.8"]),
                    (TagField.Artist, ["Karl Böhm"]),
                    (TagField.Date, ["1963"]))),
                "R-504"));

        Assert.False(change.HasFix);
        Assert.Equal(HoldReason.WorkUnknown, change.HoldReason);
        Assert.Equal("保留", change.Classification);
    }

    /// <summary>
    /// R-504: 決められない要素があれば保留し、理由をコードで持つことを確認する（SPEC 7.4.4）。
    /// </summary>
    [Theory]
    [InlineData(null, "Günter Wand", HoldReason.DateUnknown)]
    [InlineData("1993", null, HoldReason.ArtistUnknown)]
    public void HoldsWhenElementIsMissing(string? date, string? artist, HoldReason expected)
    {
        (TagField Field, string[] Values)[] fields =
        [
            (TagField.Composer, ["Anton Bruckner"]),
            (TagField.Album, ["Symphony No.8"]),
            .. date is null ? Array.Empty<(TagField, string[])>() : [(TagField.Date, [date])],
            .. artist is null ? Array.Empty<(TagField, string[])>() : [(TagField.Artist, [artist])],
        ];

        TagChange change = Assert.Single(
            ChangesOf(Works(Track("ブルックナー/ブルックナー 8 - Wand/01.flac", fields)), "R-504"));

        Assert.Equal(expected, change.HoldReason);
        Assert.False(change.IsSelected);
    }

    /// <summary>
    /// R-504: <c>date</c> が単位内で割れていたら保留することを確認する。
    /// **最古年・最頻値のような機械的な選び方をしない**（3.5 規則2）。
    /// </summary>
    [Fact]
    public void HoldsWhenDateIsSplitWithinUnit()
    {
        InspectionResult result = Works(
            Track("ブルックナー/ブルックナー 8 - Wand/01.flac", [.. BRUCKNER_8]),
            Track("ブルックナー/ブルックナー 8 - Wand/02.flac",
                (TagField.Composer, ["Anton Bruckner"]),
                (TagField.Album, ["Symphony No.8"]),
                (TagField.Artist, ["Günter Wand"]),
                (TagField.Date, ["1994"])));

        Assert.All(
            ChangesOf(result, "R-504"),
            change => Assert.Equal(HoldReason.DateUnknown, change.HoldReason));
    }

    /// <summary>
    /// R-504: 主作品 + カップリングで年が割れている単位は、個別例外の年で解けることを確認する
    /// （3.5 規則2・規則5）。
    ///
    /// **フォルダを分けるのも date を揃えるのも誤り。** 主作品と併録曲が別セッションなのは
    /// 普通のことで、分ければ 1 枚のアルバムが割れ（規則3）、揃えればタグが実際の録音年と食い違う。
    /// </summary>
    [Fact]
    public void UsesOverrideDateWhenDateIsSplit()
    {
        const string folder = @"ブルックナー\ブルックナー 8 - 主作品とカップリング";

        InspectionResult result = Works(
            Track(folder + @"\01.flac", [.. BRUCKNER_8]),
            Track(
                folder + @"\05.flac",
                (TagField.Composer, ["Anton Bruckner"]),
                (TagField.Album, ["Symphony No.8"]),
                (TagField.Artist, ["Günter Wand"]),
                (TagField.Date, ["1994"])));

        Assert.All(
            ChangesOf(result, "R-504"),
            change =>
            {
                Assert.Equal(HoldReason.None, change.HoldReason);
                Assert.Equal("Anton Bruckner: Symphony No. 8 - 1993/Günter Wand", Assert.Single(change.AfterValues));

                // どの年をなぜ採ったのかが読めないと、この修正案は承認できない（SPEC 5.3）。
                Assert.Contains("個別例外の指定「1993」を採った", change.Rationale, StringComparison.Ordinal);
            });
    }

    /// <summary>
    /// R-504: タグの年が一意なら、個別例外の年では上書きしないことを確認する。
    ///
    /// **一意な値まで上書きできると、辞書に古い年が残っているのにタグは直っている、
    /// という食い違いが検出されないまま残る。**
    /// </summary>
    [Fact]
    public void PrefersTagDateOverOverrideDate()
    {
        // 個別例外は 1994 を指しているが、タグの 1993 は単位内で一意になっている。
        TagChange change = Assert.Single(ChangesOf(
            Works(Track(@"ブルックナー\ブルックナー 8 - 年が直った\01.flac", [.. BRUCKNER_8])),
            "R-504"));

        Assert.Equal("Anton Bruckner: Symphony No. 8 - 1993/Günter Wand", Assert.Single(change.AfterValues));
        Assert.DoesNotContain("個別例外の指定", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-504: 個別例外では解けない保留の根拠に、直す手順まで書いてあることを確認する（SPEC 7.4.4）。
    ///
    /// **保留の理由だけを書くと、利用者は手近な導線を試すしかない。** <c>date</c> と <c>artist</c> は
    /// <c>albumOverrides</c> に無いので「このアルバムの扱いを決める」では解けないが、対象外にすれば
    /// 一覧からは消える。タグが割れたまま消えるのを、根拠の書き方で防いでいる。
    /// </summary>
    [Theory]
    [InlineData(TagField.Date, "1994", "date")]
    [InlineData(TagField.Artist, "Herbert von Karajan", "artist")]
    public void SplitValueHoldTellsHowToFix(TagField field, string other, string label)
    {
        InspectionResult result = Works(
            Track("ブルックナー/ブルックナー 8 - Wand/01.flac", [.. BRUCKNER_8]),
            Track(
                "ブルックナー/ブルックナー 8 - Wand/02.flac",
                [.. BRUCKNER_8.Where(tag => tag.Field != field), (field, new[] { other })]));

        Assert.All(
            ChangesOf(result, "R-504"),
            change =>
            {
                Assert.Contains("フォルダを分ける", change.Rationale, StringComparison.Ordinal);
                Assert.Contains($"ファイル一覧タブで {label} を揃える", change.Rationale, StringComparison.Ordinal);
            });
    }

    /// <summary>
    /// R-504: 単位内に作曲家が複数いたら保留することを確認する（3.5 規則5・規則6 の対象）。
    /// </summary>
    [Fact]
    public void HoldsWhenUnitHasMultipleComposers()
    {
        InspectionResult result = Works(
            Track("ドヴォルザーク/ドヴォルザーク 9 - カラヤン/01.flac",
                (TagField.Composer, ["Antonín Dvořák"]),
                (TagField.Artist, ["Herbert von Karajan"]),
                (TagField.Date, ["1985"])),
            Track("ドヴォルザーク/ドヴォルザーク 9 - カラヤン/05.flac",
                (TagField.Composer, ["Bedřich Smetana"]),
                (TagField.Artist, ["Herbert von Karajan"]),
                (TagField.Date, ["1985"])));

        Assert.All(
            ChangesOf(result, "R-504"),
            change => Assert.Equal(HoldReason.WorkUnknown, change.HoldReason));
    }

    /// <summary>
    /// R-504: 既に正しい書式のファイルを検出しないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotFlagAlbumAlreadyInFormat()
    {
        InspectionResult result = Works(
            Track("ブルックナー/ブルックナー 8 - Wand/01.flac",
                (TagField.Composer, ["Anton Bruckner"]),
                (TagField.Album, ["Anton Bruckner: Symphony No. 8 - 1993/Günter Wand"]),
                (TagField.Artist, ["Günter Wand"]),
                (TagField.Date, ["1993"])));

        Assert.Empty(ChangesOf(result, "R-504"));
    }

    /// <summary>
    /// R-504: <c>albumOverrides</c> で対象外にした単位を検出しないことを確認する（3.5 規則6）。
    /// **一覧にも出さない。** 直す必要があるのに出ていないのではなく、対象外だと決めたものである。
    /// </summary>
    [Fact]
    public void SkipsExcludedFolder()
    {
        InspectionResult result = Works(
            Track("リスト 交響詩/01.flac",
                (TagField.Composer, ["Franz Liszt"]),
                (TagField.Artist, ["Karl Böhm"]),
                (TagField.Date, ["1970"])));

        Assert.Empty(ChangesOf(result, "R-504"));
    }

    /// <summary>
    /// R-504: フォルダ名の Unicode 正規化形が違っても個別例外が効くことを確認する。
    ///
    /// 濁点付きの仮名は「ザ」1 文字（NFC）と「サ + 濁点」2 文字（NFD）のどちらでも保存できる。
    /// 実ライブラリの <c>シェエラザード</c> が NFD で保存されており、辞書に手で書いた NFC の
    /// 綴りと一致せず、対象外にしたはずのフォルダが検出に出ていた（2026-08-12）。
    /// </summary>
    [Fact]
    public void MatchesOverrideAcrossUnicodeForms()
    {
        string decomposed = "シェエラザード".Normalize(NormalizationForm.FormD);

        // 前提: 分解形は文字数が増え、単純な文字列比較では一致しない。
        Assert.NotEqual("シェエラザード", decomposed, StringComparer.Ordinal);

        InspectionResult result = Works(
            Track($"{decomposed}/01.m4a",
                (TagField.Composer, ["Nikolai Rimsky-Korsakov"]),
                (TagField.Artist, ["Yevgeny Mravinsky"]),
                (TagField.Date, ["1966"])));

        Assert.Empty(ChangesOf(result, "R-504"));
    }

    /// <summary>
    /// R-504: <c>albumOverrides</c> の作品名で組み立てることを確認する（3.5 規則4・規則7）。
    /// </summary>
    [Fact]
    public void UsesWorkNameFromOverride()
    {
        TagChange change = Assert.Single(
            ChangesOf(
                Works(Track("ショスタコーヴィチ/オリンピア盤/01.flac",
                    (TagField.Composer, ["Dmitri Shostakovich"]),
                    (TagField.Artist, ["Yevgeny Mravinsky"]),
                    (TagField.Date, ["1978"]))),
                "R-504"));

        Assert.Equal("Dmitri Shostakovich: Symphony No. 5 (Olympia) - 1978/Yevgeny Mravinsky", change.AfterText);
        Assert.Contains("個別例外", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// 楽章番号の書式判定を確認する。
    /// </summary>
    [Theory]
    [InlineData("1. Allegro", "1. 形式")]
    [InlineData("5-1. Allegro", "5-1. 形式")]
    [InlineData("1 Allegro", "1 Allegro 形式")]
    [InlineData("I. Allegro", "I. 形式")]
    [InlineData("(I. Allegro)", "(I. 形式")]
    [InlineData("第1楽章 Allegro", "第N楽章 形式")]
    [InlineData("Allegro", "番号なし")]
    public void ClassifiesMovementNumberStyle(string title, string expected)
    {
        Assert.Equal(expected, MovementNumberStyleRule.GetStyle(title));
    }

    /// <summary>
    /// 全フィールドを走査するルールが <c>comment</c> を拾わないことを確認する
    /// （docs/TAGGING_POLICY.md 2.4「正規形を定めず、検査ルールの対象にしない」）。
    ///
    /// <c>comment</c> は自由記述なので、<c>;</c> も重複した語も、文字化けに見えるバイト列さえ
    /// 正当な内容でありうる。**同じ値を <c>title</c> に入れると検出されることも併せて見る。**
    /// 片側だけを見ていると、ルール自体が壊れて何も検出しなくなったときにも緑のままになる。
    /// </summary>
    [Theory]
    [InlineData("R-205")]
    [InlineData("R-206")]
    [InlineData("R-403")]
    public void DoesNotInspectFreeTextField(string ruleId)
    {
        string value = ruleId switch
        {
            "R-205" => "ハース版; ノヴァーク版",
            "R-206" => "Anton Bruckner; Anton Bruckner",
            _ => Garble("ハース版"),
        };

        Assert.Empty(ChangesOf(Inspect(Track("01.flac", (TagField.Comment, [value]))), ruleId));
        Assert.NotEmpty(ChangesOf(Inspect(Track("01.flac", (TagField.Title, [value]))), ruleId));
    }

    /// <summary>
    /// 日本語を Shift-JIS として書き、Latin-1 として読み違えた状態を作る。
    /// 実ライブラリの 4 ファイルと同じ壊れ方を再現する。
    /// </summary>
    private static string Garble(string original)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        return System.Text.Encoding.Latin1.GetString(System.Text.Encoding.GetEncoding(932).GetBytes(original));
    }

    /// <summary>
    /// 文字列に含まれる語の出現回数を数える。根拠の重複を見るために使う。
    /// </summary>
    private static int CountOf(string text, string word)
    {
        return (text.Length - text.Replace(word, string.Empty, StringComparison.Ordinal).Length) / word.Length;
    }

    /// <summary>
    /// 指定したルールの検出結果を取り出す。
    /// </summary>
    private static IReadOnlyList<TagChange> ChangesOf(InspectionResult result, string ruleId)
    {
        return result.Results.Single(rule => rule.RuleId == ruleId).Changes;
    }

    /// <summary>
    /// 検査を実行する。
    /// </summary>
    private static InspectionResult Inspect(params TrackTags[] tracks)
    {
        return Inspect(tracks, DICTIONARY, enableDiacritic: false);
    }

    /// <summary>
    /// 作品エントリを持つ辞書で検査を実行する。R-504 のテストで使う。
    /// </summary>
    private static InspectionResult Works(params TrackTags[] tracks)
    {
        return Inspect(tracks, WORKS_DICTIONARY, enableDiacritic: false);
    }

    /// <summary>
    /// 検査を実行する。
    /// </summary>
    private static InspectionResult Inspect(TrackTags[] tracks, bool enableDiacritic)
    {
        return Inspect(tracks, DICTIONARY, enableDiacritic);
    }

    /// <summary>
    /// 検査を実行する。
    /// </summary>
    private static InspectionResult Inspect(TrackTags[] tracks, DictionaryIndex dictionary, bool enableDiacritic)
    {
        ScanResult scan = new(LIBRARY_ROOT, tracks, [], TimeSpan.Zero);

        InspectionOptions options = new()
        {
            EnabledOptionalRuleIds = enableDiacritic
                ? new HashSet<string>(StringComparer.Ordinal) { DiacriticMissingRule.RULE_ID }
                : new HashSet<string>(StringComparer.Ordinal),
        };

        return new InspectionEngine().Inspect(new InspectionContext(scan, dictionary, options));
    }

    /// <summary>
    /// テスト用のタグを組み立てる。
    /// </summary>
    private static TrackTags Track(string relativePath, params (TagField Field, string[] Values)[] fields)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);

        return new TrackTags
        {
            RelativePath = normalized,
            FullPath = Path.Combine(LIBRARY_ROOT, normalized),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
                fields.Select(pair =>
                    new KeyValuePair<TagField, IReadOnlyList<string>>(pair.Field, pair.Values))),
            RawTags = new Dictionary<string, string[]>(),
        };
    }
}
