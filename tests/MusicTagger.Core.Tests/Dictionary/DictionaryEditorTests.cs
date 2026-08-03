using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Tests.Dictionary;

/// <summary>
/// 辞書編集のテスト。
/// 「検査結果から直接辞書に追加して再スキャンできる導線」（docs/SPEC.md 7.3）の中核。
/// </summary>
public sealed class DictionaryEditorTests
{
    /// <summary>
    /// 別名を足すと、その値で正規形を引けるようになることを確認する。
    /// これができないと導線が成立しない。
    /// </summary>
    [Fact]
    public void AddedAliasResolvesToCanonical()
    {
        TagDictionary before = new()
        {
            Persons = [new PersonEntry { Canonical = "Yevgeny Mravinsky", Roles = ["Conductor"] }],
        };

        Assert.False(new DictionaryIndex(before).TryResolvePerson("Evgeni Muravinsky", out _));

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Person, "Yevgeny Mravinsky", "Evgeni Muravinsky");

        Assert.True(new DictionaryIndex(after).TryResolvePerson("Evgeni Muravinsky", out PersonEntry person));
        Assert.Equal("Yevgeny Mravinsky", person.Canonical);
    }

    /// <summary>
    /// 元の辞書が書き換わらないことを確認する。
    /// 検証に落ちた編集を捨てられるよう、操作は必ず新しい辞書を返す。
    /// </summary>
    [Fact]
    public void DoesNotMutateSource()
    {
        TagDictionary before = new()
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }],
        };

        _ = DictionaryEditor.AddAlias(before, DictionaryCategory.Composer, "Anton Bruckner", "Btuckner");

        Assert.Empty(before.Composers[0].Aliases);
    }

    /// <summary>
    /// 日本語表記の別名が <c>aliasesJa</c> に入ることを確認する。
    /// </summary>
    [Fact]
    public void RoutesJapaneseAliasToAliasesJa()
    {
        TagDictionary before = new()
        {
            Persons = [new PersonEntry { Canonical = "Herbert von Karajan", Roles = ["Conductor"] }],
        };

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Person, "Herbert von Karajan", "カラヤン");

        Assert.Contains("カラヤン", after.Persons[0].AliasesJa);
        Assert.Empty(after.Persons[0].Aliases);
    }

    /// <summary>
    /// 時代分割エントリには、どの時代の正規形を指定しても別名を足せることを確認する。
    /// </summary>
    [Fact]
    public void AddsAliasToEraSplitEnsembleByAnyEraName()
    {
        TagDictionary before = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "ru-spb-phil",
                    Eras =
                    [
                        new EnsembleEra { Until = 1991, Canonical = "Leningrad Philharmonic Orchestra" },
                        new EnsembleEra { From = 1991, Canonical = "Saint Petersburg Philharmonic Orchestra" },
                    ],
                },
            ],
        };

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Ensemble, "Saint Petersburg Philharmonic Orchestra", "Leningrad Philharmonic");

        Assert.True(new DictionaryIndex(after).TryResolveEnsemble("Leningrad Philharmonic", out EnsembleEntry ensemble));
        Assert.Equal("ru-spb-phil", ensemble.EntityId);
    }

    /// <summary>
    /// 存在しない正規形を指定したら例外になることを確認する。
    /// 黙って新規作成すると、綴りを間違えたときに重複エントリができる。
    /// </summary>
    [Fact]
    public void ThrowsWhenCanonicalNotFound()
    {
        TagDictionary dictionary = new();

        Assert.Throws<InvalidOperationException>(() =>
            DictionaryEditor.AddAlias(dictionary, DictionaryCategory.Person, "Nobody", "someone"));
    }

    /// <summary>
    /// 新規の団体を足すと索引から引けることを確認する。
    /// </summary>
    [Fact]
    public void AddsNewEnsemble()
    {
        TagDictionary after = DictionaryEditor.AddEnsemble(
            new TagDictionary(), "jp-nhk-so", "NHK Symphony Orchestra", ["NHK交響楽団"]);

        Assert.True(new DictionaryIndex(after).TryResolveEnsemble("NHK交響楽団", out EnsembleEntry ensemble));
        Assert.Equal("jp-nhk-so", ensemble.EntityId);
        Assert.Contains("NHK交響楽団", ensemble.AliasesJa);
    }

    /// <summary>
    /// 正規形と同じ値の別名は落ちることを確認する。索引を無駄に膨らませない。
    /// </summary>
    [Fact]
    public void DropsAliasIdenticalToCanonical()
    {
        TagDictionary after = DictionaryEditor.AddComposer(
            new TagDictionary(), "Anton Bruckner", ["Anton Bruckner", "anton  bruckner"]);

        Assert.Empty(after.Composers[0].Aliases);
    }

    /// <summary>
    /// 検出フィールドから種別を推定できることを確認する（docs/TAGGING_POLICY.md 2.1）。
    /// </summary>
    [Theory]
    [InlineData(TagField.Composer, DictionaryCategory.Composer)]
    [InlineData(TagField.Artist, DictionaryCategory.Person)]
    [InlineData(TagField.Conductor, DictionaryCategory.Person)]
    [InlineData(TagField.AlbumArtist, DictionaryCategory.Ensemble)]
    public void SuggestsCategoryFromField(TagField field, DictionaryCategory expected)
    {
        Assert.Equal(expected, DictionaryEditor.SuggestCategory(field));
    }

    /// <summary>
    /// 実体 ID の候補が既存と重複しないことを確認する。
    /// </summary>
    [Fact]
    public void SuggestsUniqueEntityId()
    {
        TagDictionary dictionary = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "nhk-symphony-orchestra", Canonical = "x" }],
        };

        Assert.Equal("nhk-symphony-orchestra-2", DictionaryEditor.SuggestEntityId(dictionary, "NHK Symphony Orchestra"));
    }

    /// <summary>
    /// 既に辞書にある値を検出できることを確認する。二重登録を防ぐための確認。
    /// </summary>
    [Fact]
    public void DetectsAlreadyKnownValue()
    {
        DictionaryIndex index = new(new TagDictionary
        {
            Persons = [new PersonEntry { Canonical = "Karl Böhm", Roles = ["Conductor"], Aliases = ["Bohm"] }],
        });

        Assert.True(DictionaryEditor.IsAlreadyKnown(index, "bohm", out string owner));
        Assert.Contains("Karl Böhm", owner, StringComparison.Ordinal);
        Assert.False(DictionaryEditor.IsAlreadyKnown(index, "Various Artists", out _));
    }
}
