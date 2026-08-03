using MusicTagger.Core.Dictionary;

namespace MusicTagger.Core.Tests.Dictionary;

/// <summary>
/// 同梱辞書からの取り込みのテスト。
///
/// 利用者辞書は初回起動時にコピーされたきり更新されない。段階 7 で足した
/// <c>noConductor</c> が既存の利用者に届かず、R-402 が 22 件多く検出される状態になっていた。
/// </summary>
public sealed class DictionaryMergerTests
{
    /// <summary>
    /// 同梱側で立った <c>noConductor</c> を差分として拾うことを確認する。
    /// **これが取り込めないと R-402 が誤検出し続ける。**
    /// </summary>
    [Fact]
    public void DetectsNewNoConductorFlag()
    {
        TagDictionary user = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "it-i-musici", Canonical = "I Musici" }],
        };

        TagDictionary bundled = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "it-i-musici", Canonical = "I Musici", NoConductor = true }],
        };

        DictionaryMergeItem item = Assert.Single(DictionaryMerger.BuildPlan(user, bundled));

        Assert.Equal(DictionaryMergeKind.EnableNoConductor, item.Kind);

        TagDictionary merged = DictionaryMerger.Apply(user, bundled, [item]);

        Assert.True(merged.Ensembles[0].NoConductor);
    }

    /// <summary>
    /// 利用者が下ろした旗を勝手に戻さないことを確認する。
    /// 同梱側で false のものは差分にしない。
    /// </summary>
    [Fact]
    public void DoesNotReenableFlagTheUserTurnedOff()
    {
        TagDictionary user = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "a", Canonical = "X", NoConductor = true }],
        };

        TagDictionary bundled = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "a", Canonical = "X" }],
        };

        Assert.Empty(DictionaryMerger.BuildPlan(user, bundled));
    }

    /// <summary>
    /// 同梱側にしか無いエントリを差分にすることを確認する。
    /// </summary>
    [Fact]
    public void DetectsNewEntries()
    {
        TagDictionary user = new();

        TagDictionary bundled = new()
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }],
            Persons = [new PersonEntry { Canonical = "Karl Böhm", Roles = ["Conductor"] }],
            Ensembles = [new EnsembleEntry { EntityId = "at-wiener-phil", Canonical = "Wiener Philharmoniker" }],
            Typos = [new TypoEntry { Pattern = "\\bAllgro\\b", Replacement = "Allegro" }],
            ProtectedAlbumArtists = ["Peter Pears(T); Hermann Prey(BR)"],
        };

        IReadOnlyList<DictionaryMergeItem> plan = DictionaryMerger.BuildPlan(user, bundled);

        Assert.Equal(5, plan.Count);

        TagDictionary merged = DictionaryMerger.Apply(user, bundled, plan);

        Assert.Single(merged.Composers);
        Assert.Single(merged.Persons);
        Assert.Single(merged.Ensembles);
        Assert.Single(merged.Typos);
        Assert.Single(merged.ProtectedAlbumArtists);
    }

    /// <summary>
    /// 既存エントリに足りない別名を差分にすることを確認する。
    /// </summary>
    [Fact]
    public void DetectsMissingAliases()
    {
        TagDictionary user = new()
        {
            Persons = [new PersonEntry { Canonical = "Georg Solti", Roles = ["Conductor"], Aliases = ["Solti"] }],
        };

        TagDictionary bundled = new()
        {
            Persons =
            [
                new PersonEntry
                {
                    Canonical = "Georg Solti",
                    Roles = ["Conductor"],
                    Aliases = ["Solti", "Solt"],
                    AliasesJa = ["ショルティ"],
                },
            ],
        };

        IReadOnlyList<DictionaryMergeItem> plan = DictionaryMerger.BuildPlan(user, bundled);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, item => Assert.Equal(DictionaryMergeKind.AddAlias, item.Kind));

        TagDictionary merged = DictionaryMerger.Apply(user, bundled, plan);
        DictionaryIndex index = new(merged);

        Assert.True(index.TryResolvePerson("Solt", out _));
        Assert.True(index.TryResolvePerson("ショルティ", out _));

        // 日本語表記は aliasesJa 側に入る。
        Assert.Contains("ショルティ", merged.Persons[0].AliasesJa);
        Assert.DoesNotContain("ショルティ", merged.Persons[0].Aliases);
    }

    /// <summary>
    /// 正規化キーが同じ別名を差分にしないことを確認する。
    /// 登録しても索引に載らないものを増やさない。
    /// </summary>
    [Fact]
    public void IgnoresAliasesThatNormalizeToExistingNames()
    {
        TagDictionary user = new()
        {
            Composers = [new ComposerEntry { Canonical = "Antonín Dvořák" }],
        };

        TagDictionary bundled = new()
        {
            Composers = [new ComposerEntry { Canonical = "Antonín Dvořák", Aliases = ["Antonin Dvorak"] }],
        };

        Assert.Empty(DictionaryMerger.BuildPlan(user, bundled));
    }

    /// <summary>
    /// チェックを外した差分を取り込まないことを確認する。
    /// **利用者が意図的に消したエントリを復活させないための逃げ道。**
    /// </summary>
    [Fact]
    public void SkipsUnselectedItems()
    {
        TagDictionary user = new();

        TagDictionary bundled = new()
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Anton Bruckner" },
                new ComposerEntry { Canonical = "Gustav Mahler" },
            ],
        };

        IReadOnlyList<DictionaryMergeItem> plan = DictionaryMerger.BuildPlan(user, bundled);
        plan[0].IsSelected = false;

        TagDictionary merged = DictionaryMerger.Apply(user, bundled, plan);

        Assert.Equal("Gustav Mahler", Assert.Single(merged.Composers).Canonical);
    }

    /// <summary>
    /// 利用者が編集した正規形を上書きしないことを確認する。取り込みは追加だけ行う。
    /// </summary>
    [Fact]
    public void DoesNotOverwriteUserEdits()
    {
        TagDictionary user = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "at-wiener-phil", Canonical = "利用者が変えた名前" }],
        };

        TagDictionary bundled = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "at-wiener-phil", Canonical = "Wiener Philharmoniker" }],
        };

        TagDictionary merged = DictionaryMerger.Apply(user, bundled, DictionaryMerger.BuildPlan(user, bundled));

        Assert.Equal("利用者が変えた名前", merged.Ensembles[0].Canonical);
    }

    /// <summary>
    /// 団体の同一性を実体 ID で判断することを確認する（docs/TAGGING_POLICY.md 5.3.1）。
    /// 名前が違っても同一実体、名前が似ていても別実体。
    /// </summary>
    [Fact]
    public void MatchesEnsemblesByEntityId()
    {
        TagDictionary user = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "ru-spb-phil",
                    Eras = [new EnsembleEra { Until = 1991, Canonical = "Leningrad Philharmonic Orchestra" }],
                },
            ],
        };

        TagDictionary bundled = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "ru-spb-phil",
                    Eras = [new EnsembleEra { Until = 1991, Canonical = "Leningrad Philharmonic Orchestra" }],
                },
                new EnsembleEntry { EntityId = "ru-spb-radio", Canonical = "Leningrad Radio Orchestra" },
            ],
        };

        DictionaryMergeItem item = Assert.Single(DictionaryMerger.BuildPlan(user, bundled));

        Assert.Equal(DictionaryMergeKind.AddEnsemble, item.Kind);
        Assert.Equal("ru-spb-radio", item.Target);
    }

    /// <summary>
    /// 取り込むと版が同梱側に揃うことを確認する。
    /// </summary>
    [Fact]
    public void UpdatesVersionToBundled()
    {
        TagDictionary user = new() { Version = 2 };
        TagDictionary bundled = new() { Version = 3 };

        Assert.Equal(3, DictionaryMerger.Apply(user, bundled, []).Version);
    }

    /// <summary>
    /// 同じ辞書どうしなら差分が出ないことを確認する。
    /// 初回起動でコピーした直後に「更新があります」と出ては困る。
    /// </summary>
    [Fact]
    public void ProducesNoPlanForIdenticalDictionaries()
    {
        TagDictionary bundled = DictionaryLoader.LoadDefault();

        Assert.Empty(DictionaryMerger.BuildPlan(bundled, bundled));
    }

    /// <summary>
    /// 取り込んだ結果が検証を通ることを確認する。
    /// 索引に載らない別名を増やしていないかの確認も兼ねる。
    /// </summary>
    [Fact]
    public void MergedDictionaryPassesValidation()
    {
        TagDictionary bundled = DictionaryLoader.LoadDefault();

        // 段階 3 相当の古い辞書を模す。noConductor を持たない。
        TagDictionary user = bundled with
        {
            Version = 2,
            Ensembles = [.. bundled.Ensembles.Select(entry => entry with { NoConductor = false })],
        };

        IReadOnlyList<DictionaryMergeItem> plan = DictionaryMerger.BuildPlan(user, bundled);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, item => Assert.Equal(DictionaryMergeKind.EnableNoConductor, item.Kind));

        TagDictionary merged = DictionaryMerger.Apply(user, bundled, plan);

        Assert.DoesNotContain(
            DictionaryValidator.Validate(merged),
            issue => issue.Severity == DictionaryIssueSeverity.Error);

        DictionaryIndex index = new(merged);

        Assert.True(index.TryResolveEnsemble("I Musici", out EnsembleEntry musici));
        Assert.True(musici.NoConductor);
    }
}
