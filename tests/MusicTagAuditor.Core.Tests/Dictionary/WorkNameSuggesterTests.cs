using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 作品名の候補のテスト（docs/SPEC.md 7.3.2）。
///
/// 候補が無いと、`Nielsen Symphony No.4` という <c>album</c> を見ながら `Symphony No. 4` を
/// 毎回手で打つことになる。**それでいて候補は既定値ではない。** 出所を添えて並べ、選ぶのは人に残す。
/// </summary>
public sealed class WorkNameSuggesterTests
{
    /// <summary>
    /// album から作曲家名を外し、3.5 規則8 の書式に整えた候補を先頭に出す。
    /// </summary>
    [Fact]
    public void StripsComposerFromAlbumValue()
    {
        IReadOnlyList<WorkNameCandidate> candidates = Suggest(
            new WorkNameCandidate("Nielsen Symphony No.4", "album"),
            new WorkNameCandidate("ニールセン 4", "フォルダ名"));

        Assert.Equal("Symphony No. 4", candidates[0].Value);
        Assert.Equal("album", candidates[0].Source);
    }

    /// <summary>
    /// 日本語の手がかりからは候補を作らない。
    /// 作品名はジャンル名が英語・固有の題名が原語で、原語が非ラテン文字なら英語圏での題名を使う（3.5 規則8）。
    /// </summary>
    [Fact]
    public void DoesNotSuggestJapaneseValues()
    {
        IReadOnlyList<WorkNameCandidate> candidates = Suggest(
            new WorkNameCandidate("ニールセン 4", "フォルダ名"));

        Assert.DoesNotContain(candidates, candidate => candidate.Value.Contains('ニ', StringComparison.Ordinal));
    }

    /// <summary>
    /// 手がかりが日本語だけでも、辞書にある作品名の書式に番号を差し替えて候補を作る。
    ///
    /// 素材は作曲家をまたいだ全作品にする。第 4 番を誰も登録していなくても
    /// <c>Symphony No. 8</c> があれば <c>Symphony No. 4</c> は作れる。
    /// </summary>
    [Fact]
    public void BuildsCandidateFromExistingWorkFormat()
    {
        IReadOnlyList<WorkNameCandidate> candidates = Suggest(
            new WorkNameCandidate("ニールセン 4", "フォルダ名"));

        WorkNameCandidate candidate = Assert.Single(
            candidates,
            candidate => candidate.Value == "Symphony No. 4");

        Assert.Equal(WorkNameSuggester.SOURCE_TEMPLATE, candidate.Source);
    }

    /// <summary>
    /// 手がかりに出てくる語を含む書式を先に出す。
    /// <c>Symphony</c> と書いてあるのに <c>Piano Concerto</c> を先頭に置くと、読む手間が増えるだけになる。
    /// </summary>
    [Fact]
    public void PrefersFormatsThatMatchTheHint()
    {
        IReadOnlyList<WorkNameCandidate> candidates = Suggest(
            new WorkNameCandidate("ニールセン 交響曲 4 Symphony", "フォルダ名"));

        WorkNameCandidate first = candidates.First(candidate => candidate.Source == WorkNameSuggester.SOURCE_TEMPLATE);

        Assert.Equal("Symphony No. 4", first.Value);
    }

    /// <summary>
    /// 同じ作曲家に既にある作品名も候補にする。ただし**手がかりの番号と合うものだけ**。
    ///
    /// **これは重複登録ではない。** 手がかりが引けなかったのは別名が足りないからで、
    /// 選べば既存のエントリに別名だけが足される。
    /// 第 4 番を探しているときに第 5 番を並べても選ぶことはなく、候補欄が埋まって他が見えなくなる。
    /// </summary>
    [Fact]
    public void OffersExistingWorksOfTheSameComposerWithTheSameNumber()
    {
        TagDictionary dictionary = CreateDictionary() with
        {
            Works =
            [
                new WorkEntry { Composer = "Carl Nielsen", Canonical = "Symphony No. 4" },
                new WorkEntry { Composer = "Carl Nielsen", Canonical = "Symphony No. 5" },
            ],
        };

        IReadOnlyList<WorkNameCandidate> candidates = WorkNameSuggester.Suggest(
            dictionary,
            new DictionaryIndex(dictionary),
            "Carl Nielsen",
            [new WorkNameCandidate("ニールセン 4", "フォルダ名")]);

        Assert.Contains(
            candidates,
            candidate => candidate.Value == "Symphony No. 4" && candidate.Source == WorkNameSuggester.SOURCE_SAME_COMPOSER);

        Assert.DoesNotContain(candidates, candidate => candidate.Value == "Symphony No. 5");
    }

    /// <summary>
    /// 録音年を作品の番号として扱わないことを確認する。
    ///
    /// フォルダ名には録音年が入っていることが多い（<c>チャイコフスキー 6 - ムラヴィンスキー 1982</c>）。
    /// 年を番号にすると <c>Symphony No. 1982</c> という候補ができる。
    /// </summary>
    [Fact]
    public void DoesNotTreatRecordingYearAsWorkNumber()
    {
        IReadOnlyList<WorkNameCandidate> candidates = Suggest(
            new WorkNameCandidate("ニールセン 4 - ブロムシュテット 1982", "フォルダ名"));

        Assert.DoesNotContain(candidates, candidate => candidate.Value.Contains("1982", StringComparison.Ordinal));
        Assert.Contains(candidates, candidate => candidate.Value == "Symphony No. 4");
    }

    /// <summary>
    /// 日本語の手がかりに混ざったラテン文字から候補を作らないことを確認する。
    ///
    /// <c>ブルックナー 8 - Wand</c> の <c>Wand</c> は演奏者であって作品名ではない。
    /// 作曲家名だけを外して残りを候補にすると、演奏者名が作品名の候補として並ぶ。
    /// </summary>
    [Fact]
    public void DoesNotSuggestPerformerLeftInJapaneseFolderName()
    {
        IReadOnlyList<WorkNameCandidate> candidates = Suggest(
            new WorkNameCandidate("ニールセン 4 - Blomstedt", "フォルダ名"));

        Assert.DoesNotContain(candidates, candidate => candidate.Value.Contains("Blomstedt", StringComparison.Ordinal));
    }

    /// <summary>
    /// 区切りなしで作曲家名が続く書き方からも作曲家名を外す。
    /// 実ライブラリの <c>album</c> には <c>Bruckner:Sym.No.3</c> のような値がある。
    /// </summary>
    [Fact]
    public void StripsComposerWrittenWithoutSpace()
    {
        TagDictionary dictionary = CreateDictionary();

        Assert.Equal(
            "Symphony No. 8",
            WorkNameSuggester.StripComposer("Dvořák: Symphony No.8", new DictionaryIndex(dictionary)));
    }

    /// <summary>
    /// 候補を作る。作曲家は Carl Nielsen 固定。
    /// </summary>
    private static IReadOnlyList<WorkNameCandidate> Suggest(params WorkNameCandidate[] hints)
    {
        TagDictionary dictionary = CreateDictionary();

        return WorkNameSuggester.Suggest(dictionary, new DictionaryIndex(dictionary), "Carl Nielsen", hints);
    }

    /// <summary>
    /// 他の作曲家の作品だけが入っている辞書を作る。
    /// </summary>
    private static TagDictionary CreateDictionary()
    {
        return new TagDictionary
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Carl Nielsen", Aliases = ["Nielsen"], AliasesJa = ["ニールセン"] },
                new ComposerEntry { Canonical = "Anton Bruckner", Aliases = ["Bruckner"], AliasesJa = ["ブルックナー"] },
                new ComposerEntry { Canonical = "Antonín Dvořák", Aliases = ["Dvořák"], AliasesJa = ["ドヴォルザーク"] },
            ],
            Works =
            [
                new WorkEntry { Composer = "Anton Bruckner", Canonical = "Symphony No. 8" },
                new WorkEntry { Composer = "Anton Bruckner", Canonical = "Symphony No. 9" },
                new WorkEntry { Composer = "Antonín Dvořák", Canonical = "Piano Concerto No. 2" },
            ],
        };
    }
}
