using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 手編集の入力候補のテスト。
/// 候補から確定して入るのは常に正規形であり、別名や日本語表記は探すための手掛かりにすぎない。
/// </summary>
public sealed class DictionarySuggesterTests
{
    /// <summary>テストで使う辞書。</summary>
    private static readonly TagDictionary DICTIONARY = new()
    {
        Composers =
        [
            new ComposerEntry
            {
                Canonical = "Johannes Brahms",
                Aliases = ["Brahms", "J. Brahms"],
                AliasesJa = ["ブラームス"],
            },
            new ComposerEntry { Canonical = "Anton Bruckner", AliasesJa = ["ブルックナー"] },
        ],
        Persons =
        [
            new PersonEntry
            {
                Canonical = "Herbert von Karajan",
                Roles = ["Conductor"],
                AliasesJa = ["カラヤン"],
            },
        ],
        Ensembles =
        [
            new EnsembleEntry
            {
                EntityId = "berlin-phil",
                Eras =
                [
                    new EnsembleEra { Until = 2002, Canonical = "Berliner Philharmoniker" },
                    new EnsembleEra { From = 2002, Canonical = "Berliner Philharmoniker (new)" },
                ],
                AliasesJa = ["ベルリン・フィル"],
            },
        ],
    };

    /// <summary>
    /// 日本語表記で打っても正規形の候補が出ることを確認する。
    /// **確定して入るのは正規形。** 日本語表記をそのまま書くと次の検査で指摘される。
    /// </summary>
    [Fact]
    public void MatchesJapaneseAliasAndSuggestsCanonical()
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Composer);

        IReadOnlyList<DictionarySuggestion> suggestions = DictionarySuggester.Filter(candidates, "ぶらーむす");

        DictionarySuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("Johannes Brahms", suggestion.Canonical);
        Assert.Equal("ブラームス", suggestion.MatchedAlias);
    }

    /// <summary>
    /// ラテン文字の別名でも正規形が出ることを確認する。
    /// </summary>
    [Fact]
    public void MatchesLatinAlias()
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Composer);

        IReadOnlyList<DictionarySuggestion> suggestions = DictionarySuggester.Filter(candidates, "J. Brah");

        DictionarySuggestion suggestion = Assert.Single(suggestions);
        Assert.Equal("Johannes Brahms", suggestion.Canonical);
    }

    /// <summary>
    /// 正規形自身で当たった場合は、併記する別名を持たないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotReportAliasWhenCanonicalMatches()
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Person);

        DictionarySuggestion suggestion = Assert.Single(DictionarySuggester.Filter(candidates, "Karajan"));

        Assert.Equal("Herbert von Karajan", suggestion.Canonical);
        Assert.Null(suggestion.MatchedAlias);
        Assert.Equal("Herbert von Karajan", suggestion.DisplayText);
    }

    /// <summary>
    /// 大文字小文字・記号・ダイアクリティカルマークの差を吸収することを確認する。
    /// 照合を辞書引きと同じ正規化キーで行っているため。
    /// </summary>
    [Theory]
    [InlineData("BRUCKNER")]
    [InlineData("bruckner")]
    [InlineData("ぶるっくなー")]
    [InlineData("ブルックナー")]
    public void IgnoresCaseAndSymbolDifferences(string input)
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Composer);

        DictionarySuggestion suggestion = Assert.Single(DictionarySuggester.Filter(candidates, input));

        Assert.Equal("Anton Bruckner", suggestion.Canonical);
    }

    /// <summary>
    /// 時代分割エントリは、区分ごとの正規形がそれぞれ候補になることを確認する。
    /// どちらを選ぶかは収録年を知っている人間が決める。
    /// </summary>
    [Fact]
    public void ListsEveryEraCanonicalOfEnsemble()
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Ensemble);

        string[] canonicals = [.. DictionarySuggester.Filter(candidates, "ベルリン").Select(item => item.Canonical)];

        Assert.Equal(["Berliner Philharmoniker", "Berliner Philharmoniker (new)"], canonicals);
    }

    /// <summary>
    /// 前方一致が部分一致より先に来ることを確認する。
    /// 打ち始めた文字で始まる名前が沈むと、候補があるのに無いように見える。
    /// </summary>
    [Fact]
    public void RanksPrefixMatchesFirst()
    {
        TagDictionary dictionary = new()
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Carl Nielsen" },
                new ComposerEntry { Canonical = "Nielsen Junior" },
            ],
        };

        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(dictionary, DictionaryCategory.Composer);

        string[] canonicals = [.. DictionarySuggester.Filter(candidates, "Nielsen").Select(item => item.Canonical)];

        Assert.Equal(["Nielsen Junior", "Carl Nielsen"], canonicals);
    }

    /// <summary>
    /// 入力が空なら、上限まで候補をそのまま出すことを確認する。
    /// 欄に入った時点で何を選べるかが分かるようにするため。
    /// </summary>
    [Fact]
    public void ReturnsLeadingCandidatesForEmptyInput()
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Composer);

        IReadOnlyList<DictionarySuggestion> suggestions = DictionarySuggester.Filter(candidates, string.Empty);

        Assert.Equal(["Anton Bruckner", "Johannes Brahms"], suggestions.Select(item => item.Canonical));
    }

    /// <summary>
    /// 上限を超えないことを確認する。候補で画面を埋めない。
    /// </summary>
    [Fact]
    public void RespectsLimit()
    {
        TagDictionary dictionary = new()
        {
            Composers = [.. Enumerable.Range(0, 50).Select(index => new ComposerEntry { Canonical = $"Composer {index:D2}" })],
        };

        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(dictionary, DictionaryCategory.Composer);

        Assert.Equal(5, DictionarySuggester.Filter(candidates, "Composer", 5).Count);
        Assert.Equal(DictionarySuggester.MAX_SUGGESTIONS, DictionarySuggester.Filter(candidates, "Composer").Count);
    }

    /// <summary>
    /// 辞書が扱わないフィールドでは候補を出さないことを確認する。
    /// 曲名や年の欄に人物名が並ぶと、誤って入れてしまう。
    /// </summary>
    [Theory]
    [InlineData(TagField.Title)]
    [InlineData(TagField.Album)]
    [InlineData(TagField.Genre)]
    [InlineData(TagField.Date)]
    [InlineData(TagField.TrackNumber)]
    [InlineData(TagField.DiscNumber)]
    [InlineData(TagField.Comment)]
    public void HasNoCategoryForFieldsOutsideDictionary(TagField field)
    {
        Assert.Null(DictionarySuggester.CategoryFor(field));
    }

    /// <summary>
    /// 名前を扱うフィールドが、辞書の正しい表に結び付くことを確認する。
    /// </summary>
    [Theory]
    [InlineData(TagField.Composer, DictionaryCategory.Composer)]
    [InlineData(TagField.AlbumArtist, DictionaryCategory.Ensemble)]
    [InlineData(TagField.Artist, DictionaryCategory.Person)]
    [InlineData(TagField.Conductor, DictionaryCategory.Person)]
    public void MapsNameFieldsToCategory(TagField field, DictionaryCategory expected)
    {
        Assert.Equal(expected, DictionarySuggester.CategoryFor(field));
    }

    /// <summary>
    /// 一致しなければ空を返すことを確認する。**辞書に無い値を弾くための仕組みではない。**
    /// 候補が出ないだけで、入力そのものは自由に行える。
    /// </summary>
    [Fact]
    public void ReturnsEmptyWhenNothingMatches()
    {
        IReadOnlyList<SuggestionEntry> candidates =
            DictionarySuggester.BuildCandidates(DICTIONARY, DictionaryCategory.Composer);

        Assert.Empty(DictionarySuggester.Filter(candidates, "Zemlinsky"));
    }
}
