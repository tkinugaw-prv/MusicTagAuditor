using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 冗長な別名の掃除のテスト。
///
/// 掃除して**引ける範囲が変わってはならない**。索引が捨てていた別名だけを落とすのが前提であり、
/// これが崩れると辞書を掃除した瞬間に検査結果が変わる。
/// </summary>
public sealed class RedundantAliasCleanerTests
{
    /// <summary>
    /// ダイアクリティカルマークだけが違う別名を落とし、先に書いたほうを残すことを確認する。
    /// </summary>
    [Fact]
    public void RemovesAliasFoldedByDiacritics()
    {
        TagDictionary before = new()
        {
            Persons =
            [
                new PersonEntry
                {
                    Canonical = "Rafael Kubelík",
                    Roles = ["Conductor"],
                    Aliases = ["Rafael Kubelik", "Kubelik", "Kubelík"],
                },
            ],
        };

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(before);

        Assert.Equal(["Kubelik"], after.Persons[0].Aliases);
        Assert.Equal(2, removed.Count);
        Assert.Contains(removed, item => item.Name == "Rafael Kubelik" && item.KeptName == "Rafael Kubelík");
        Assert.Contains(removed, item => item.Name == "Kubelík" && item.KeptName == "Kubelik");
    }

    /// <summary>
    /// 記号・空白だけが違う別名を落とすことを確認する。
    /// </summary>
    [Fact]
    public void RemovesAliasFoldedBySymbols()
    {
        TagDictionary before = new()
        {
            Works =
            [
                new WorkEntry
                {
                    Composer = "Anton Bruckner",
                    Canonical = "Symphony No. 7",
                    Aliases = ["Symphony No.7", "Bruckner 7"],
                },
            ],
        };

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(before);

        Assert.Equal(["Bruckner 7"], after.Works[0].Aliases);

        // 対象の名指しは辞書タブの一覧の見出しにそろえる（docs/SPEC.md 7.3）。
        Assert.Equal("Anton Bruckner: Symphony No. 7", Assert.Single(removed).Owner);
    }

    /// <summary>
    /// 濁点だけが違う日本語表記を落とすことを確認する。
    /// </summary>
    [Fact]
    public void RemovesAliasFoldedByDakuten()
    {
        TagDictionary before = new()
        {
            Composers =
            [
                new ComposerEntry
                {
                    Canonical = "Carl Maria von Weber",
                    Aliases = ["Weber"],
                    AliasesJa = ["ウェーバー", "ヴェーバー"],
                },
            ],
        };

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(before);

        Assert.Equal(["ウェーバー"], after.Composers[0].AliasesJa);
        Assert.Equal(["Weber"], after.Composers[0].Aliases);
        Assert.Equal("ヴェーバー", Assert.Single(removed).Name);
    }

    /// <summary>
    /// 掃除しても引ける値が変わらないことを確認する。**これが掃除の前提**。
    /// </summary>
    [Fact]
    public void KeepsEveryValueResolvable()
    {
        TagDictionary before = DictionaryLoader.LoadDefault() with
        {
            Persons =
            [
                new PersonEntry
                {
                    Canonical = "Václav Smetáček",
                    Roles = ["Conductor"],
                    Aliases = ["Vaclav Smetacek", "Smetacek", "Smetáček"],
                },
            ],
        };

        DictionaryIndex after = new(RedundantAliasCleaner.Clean(before).Dictionary);

        string[] values = ["Václav Smetáček", "Vaclav Smetacek", "Smetacek", "Smetáček"];

        foreach (string value in values)
        {
            Assert.True(after.TryResolvePerson(value, out PersonEntry person), value);
            Assert.Equal("Václav Smetáček", person.Canonical);
        }
    }

    /// <summary>
    /// 時代分割の正規形は落とさないことを確認する。
    ///
    /// 同じ名前に戻った団体（<c>uk-philharmonia</c>）で区分を 1 つ落とすと、その期間の録音が
    /// 解決できなくなる。**別名ではないものを別名として扱わない。**
    /// </summary>
    [Fact]
    public void KeepsRepeatedEraCanonical()
    {
        TagDictionary before = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "uk-philharmonia",
                    Eras =
                    [
                        new EnsembleEra { Until = 1964, Canonical = "Philharmonia Orchestra" },
                        new EnsembleEra { From = 1964, Until = 1977, Canonical = "New Philharmonia Orchestra" },
                        new EnsembleEra { From = 1977, Canonical = "Philharmonia Orchestra" },
                    ],
                },
            ],
        };

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(before);

        Assert.Empty(removed);
        Assert.Equal(3, after.Ensembles[0].Eras.Count);
    }

    /// <summary>
    /// 団体の別名が時代分割の正規形と衝突する場合は落とすことを確認する。
    /// </summary>
    [Fact]
    public void RemovesEnsembleAliasCollidingWithEra()
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
                    Aliases = ["St. Petersburg Philharmonic Orchestra", "St Petersburg Philharmonic Orchestra"],
                },
            ],
        };

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(before);

        Assert.Equal(["St. Petersburg Philharmonic Orchestra"], after.Ensembles[0].Aliases);
        Assert.Equal("St Petersburg Philharmonic Orchestra", Assert.Single(removed).Name);
    }

    /// <summary>
    /// 姓の照合に効いている別名は、キーが重なっていても残すことを確認する。
    ///
    /// <see cref="DictionaryIndex"/> は作曲家の <c>aliases</c> のうち**空白を含まないものだけ**を
    /// 姓として索引に足す（R-203 / R-204 の判定に使う）。<c>VonWeber</c> を消して
    /// <c>Von Weber</c> だけ残すと、正規化キーは同じでも姓としては引けなくなる。
    /// **引ける範囲を変えないのが掃除の前提**なので、この形だけは残す。
    /// </summary>
    [Fact]
    public void KeepsSpacelessAliasNeededForSurnameMatching()
    {
        TagDictionary before = new()
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Carl Maria von Weber", Aliases = ["Von Weber", "VonWeber"] },
            ],
        };

        Assert.True(new DictionaryIndex(before).ContainsComposerName("Overture by VonWeber", out _));

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(before);

        Assert.Empty(removed);
        Assert.True(new DictionaryIndex(after).ContainsComposerName("Overture by VonWeber", out _));
    }

    /// <summary>
    /// 掃除した辞書が検証を通ることを確認する。掃除の目的そのもの。
    /// </summary>
    [Fact]
    public void CleanedDictionaryPassesValidation()
    {
        TagDictionary before = new()
        {
            Composers = [new ComposerEntry { Canonical = "Antonín Dvořák", Aliases = ["Dvorak", "Dvořák"] }],
            Works =
            [
                new WorkEntry
                {
                    Composer = "Antonín Dvořák",
                    Canonical = "Symphony No. 9",
                    Aliases = ["Symphony No.9"],
                },
            ],
        };

        Assert.NotEmpty(DictionaryValidator.Validate(before));
        Assert.Empty(DictionaryValidator.Validate(RedundantAliasCleaner.Clean(before).Dictionary));
    }

    /// <summary>
    /// 掃除するものが無い辞書を変えないことを確認する。
    /// </summary>
    [Fact]
    public void LeavesCleanDictionaryUnchanged()
    {
        TagDictionary bundled = DictionaryLoader.LoadDefault();

        (TagDictionary after, IReadOnlyList<RemovedAlias> removed) = RedundantAliasCleaner.Clean(bundled);

        Assert.Empty(removed);
        Assert.Equal(DictionaryWriter.Serialize(bundled), DictionaryWriter.Serialize(after));
    }

    /// <summary>
    /// 元の辞書を書き換えないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotMutateSource()
    {
        ComposerEntry entry = new() { Canonical = "Antonín Dvořák", Aliases = ["Dvorak", "Dvořák"] };
        TagDictionary before = new() { Composers = [entry] };

        _ = RedundantAliasCleaner.Clean(before);

        Assert.Equal(2, before.Composers[0].Aliases.Count);
    }
}
