using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 曲名の入力欄に出す作品名の候補のテスト。
/// 作曲家が 1 人に定まるときだけ、その作曲家の作品を出す。
/// </summary>
public sealed class WorkSuggestionIndexTests
{
    /// <summary>テストで使う辞書。同名の作品を別の作曲家に持たせる。</summary>
    private static readonly TagDictionary DICTIONARY = new()
    {
        Composers =
        [
            new ComposerEntry { Canonical = "Ludwig van Beethoven", Aliases = ["Beethoven"], AliasesJa = ["ベートーヴェン"] },
            new ComposerEntry { Canonical = "Gustav Mahler" },
            new ComposerEntry { Canonical = "Carl Nielsen" },
        ],
        Works =
        [
            new WorkEntry { Composer = "Ludwig van Beethoven", Canonical = "Leonore Overture No. 3", AliasesJa = ["レオノーレ"] },
            new WorkEntry { Composer = "Ludwig van Beethoven", Canonical = "Symphony No. 5", AliasesJa = ["運命"] },
            new WorkEntry { Composer = "Gustav Mahler", Canonical = "Symphony No. 5", AliasesJa = ["マーラー5"] },
        ],
    };

    /// <summary>索引。</summary>
    private static readonly WorkSuggestionIndex INDEX = new(DICTIONARY, new DictionaryIndex(DICTIONARY));

    /// <summary>
    /// その作曲家の作品だけが出ることを確認する。別名の作曲家タグでも引ける。
    /// </summary>
    /// <param name="composer">タグの作曲家。</param>
    [Theory]
    [InlineData("Ludwig van Beethoven")]
    [InlineData("ベートーヴェン")]
    public void ReturnsWorksOfResolvedComposer(string composer)
    {
        string[] canonicals = [.. INDEX.For([[composer]]).Select(entry => entry.Canonical)];

        Assert.Equal(["Leonore Overture No. 3", "Symphony No. 5"], canonicals);
    }

    /// <summary>
    /// 同名の作品が作曲家ごとに分かれ、別名が混ざらないことを確認する。
    /// 混ざると、別の作曲家の略称で探せてしまう。
    /// </summary>
    [Fact]
    public void KeepsSameNamedWorksSeparatedByComposer()
    {
        SuggestionEntry entry = Assert.Single(INDEX.For([["Gustav Mahler"]]));

        Assert.Contains("マーラー5", entry.Names);
        Assert.DoesNotContain("運命", entry.Names);
    }

    /// <summary>
    /// 別名で探しても、確定して入るのは作品名の正規形であることを確認する。
    /// </summary>
    [Fact]
    public void AliasSearchSuggestsCanonical()
    {
        IReadOnlyList<SuggestionEntry> candidates = INDEX.For([["Ludwig van Beethoven"]]);

        DictionarySuggestion suggestion = Assert.Single(DictionarySuggester.Filter(candidates, "レオノ"));

        Assert.Equal("Leonore Overture No. 3", suggestion.Canonical);
    }

    /// <summary>
    /// 作曲家が定まらない行があれば候補を出さないことを確認する。
    /// </summary>
    [Fact]
    public void ReturnsNothingUnlessEveryRowHasSameComposer()
    {
        // 空の行
        Assert.Empty(INDEX.For([[]]));
        Assert.Empty(INDEX.For([["Ludwig van Beethoven"], []]));

        // 辞書に無い作曲家
        Assert.Empty(INDEX.For([["Unknown Composer"]]));

        // 行をまたいで複数
        Assert.Empty(INDEX.For([["Ludwig van Beethoven"], ["Gustav Mahler"]]));

        // 1 行の複数値
        Assert.Empty(INDEX.For([["Ludwig van Beethoven", "Gustav Mahler"]]));

        // 作品が無い作曲家・行が無い
        Assert.Empty(INDEX.For([["Carl Nielsen"]]));
        Assert.Empty(INDEX.For([]));
    }

    /// <summary>
    /// 表記が違っても同じ作曲家なら 1 人として数えることを確認する。
    /// </summary>
    [Fact]
    public void TreatsAliasesAsSameComposer()
    {
        Assert.Equal(2, INDEX.For([["Beethoven"], ["Ludwig van Beethoven"]]).Count);
        Assert.Equal(2, INDEX.For([["Beethoven", "ベートーヴェン"]]).Count);
    }
}
