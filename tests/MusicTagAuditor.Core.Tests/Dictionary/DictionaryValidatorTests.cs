using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 辞書の検証のテスト。
///
/// 最重要は正規化キーの衝突検出。<see cref="DictionaryIndex"/> は <c>TryAdd</c> で索引を作るため、
/// 衝突した別名は例外も出ずに捨てられる。ここで止められないと「登録したのに効かない」状態になる。
/// </summary>
public sealed class DictionaryValidatorTests
{
    /// <summary>
    /// 同梱の既定辞書がエラーを持たないことを確認する。
    /// 既定辞書が検証を通らない状態で配るわけにはいかない。
    /// </summary>
    [Fact]
    public void DefaultDictionaryHasNoError()
    {
        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(DictionaryLoader.LoadDefault());

        Assert.DoesNotContain(issues, issue => issue.Severity == DictionaryIssueSeverity.Error);
    }

    /// <summary>
    /// 別のエントリと同じ正規化キーになる別名をエラーとして検出することを確認する。
    /// </summary>
    [Fact]
    public void DetectsNormalizedKeyCollisionAcrossEntries()
    {
        TagDictionary dictionary = new()
        {
            Persons =
            [
                new PersonEntry { Canonical = "Karl Böhm", Roles = ["Conductor"], Aliases = ["Bohm"] },

                // 「Boehm」は NFKC・ダイアクリティカル除去を経ても別キーだが、
                // 「Bohm」は上のエントリと同じキーになるため索引に載らない。
                new PersonEntry { Canonical = "Karl Boehm 2", Roles = ["Conductor"], Aliases = ["Bohm"] },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.True(DictionaryValidator.HasError(issues));
        Assert.Contains(issues, issue => issue.Message.Contains("Karl Böhm", StringComparison.Ordinal));
    }

    /// <summary>
    /// 記号・大文字小文字だけが違う別名も衝突として扱われることを確認する。
    /// 正規化キーは記号を落とすため、「St. Petersburg」と「St Petersburg」は同じキーになる。
    /// </summary>
    [Fact]
    public void TreatsPunctuationOnlyDifferenceAsCollision()
    {
        TagDictionary dictionary = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "a",
                    Canonical = "Saint Petersburg Philharmonic Orchestra",
                    Aliases = ["St. Petersburg Philharmonic", "St Petersburg Philharmonic"],
                },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        // 同じエントリ内の重複なので警告。索引は壊れないが、書いた本人の意図とは違う。
        Assert.Contains(issues, issue =>
            issue.Severity == DictionaryIssueSeverity.Warning
            && issue.Message.Contains("重複", StringComparison.Ordinal));
    }

    /// <summary>
    /// 作品の問題が、作曲家を添えて名指しされることを確認する。
    ///
    /// **「Symphony No. 7」だけでは誰の第 7 番か読めない。** 番号で呼ぶ作品は作曲家をまたいで
    /// 何件も並ぶため、作品名だけを出されても一覧のどの行を直せばよいのか分からない。
    /// </summary>
    [Fact]
    public void NamesWorkWithItsComposer()
    {
        TagDictionary dictionary = new()
        {
            Composers = [new ComposerEntry { Canonical = "Dmitri Shostakovich" }],
            Works =
            [
                new WorkEntry
                {
                    Composer = "Dmitri Shostakovich",
                    Canonical = "Symphony No. 7",

                    // 正規化キーは記号と空白を落とすので、正規形と同じキーになる別名。
                    Aliases = ["Symphony No.7"],
                },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        DictionaryIssue issue = Assert.Single(issues, item => item.Message.Contains("重複", StringComparison.Ordinal));

        Assert.Equal("Dmitri Shostakovich: Symphony No. 7", issue.Target);
    }

    /// <summary>
    /// 作曲家が空の作品でも、対象の名指しが壊れないことを確認する。
    /// </summary>
    [Fact]
    public void NamesWorkWithoutComposer()
    {
        TagDictionary dictionary = new()
        {
            Works = [new WorkEntry { Composer = string.Empty, Canonical = "Symphony No. 7" }],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.All(issues, issue => Assert.Equal("Symphony No. 7", issue.Target));
    }

    /// <summary>
    /// 実体 ID の重複をエラーとして検出することを確認する。
    /// **同一性は実体 ID で判断する**ため、ID が重複すると別団体が同一視されうる。
    /// </summary>
    [Fact]
    public void DetectsDuplicateEntityId()
    {
        TagDictionary dictionary = new()
        {
            Ensembles =
            [
                new EnsembleEntry { EntityId = "ru-spb-phil", Canonical = "Leningrad Philharmonic Orchestra" },
                new EnsembleEntry { EntityId = "ru-spb-phil", Canonical = "Leningrad Radio Orchestra" },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.True(DictionaryValidator.HasError(issues));
        Assert.Contains(issues, issue => issue.Message.Contains("実体 ID", StringComparison.Ordinal));
    }

    /// <summary>
    /// 不正な正規表現をエラーとして検出することを確認する（docs/SPEC.md 7.3）。
    /// </summary>
    [Fact]
    public void DetectsInvalidTypoPattern()
    {
        TagDictionary dictionary = new()
        {
            Typos = [new TypoEntry { Pattern = "[unclosed", Replacement = "x" }],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.True(DictionaryValidator.HasError(issues));
    }

    /// <summary>
    /// 時代分割の期間が重なっている場合、警告になることを確認する。
    /// エラーにしないのは、意図した重なりがありうるため。
    /// </summary>
    [Fact]
    public void WarnsOnOverlappingEras()
    {
        TagDictionary dictionary = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "test",
                    Eras =
                    [
                        new EnsembleEra { Until = 1995, Canonical = "Old Name" },
                        new EnsembleEra { From = 1990, Canonical = "New Name" },
                    ],
                },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.False(DictionaryValidator.HasError(issues));
        Assert.Contains(issues, issue => issue.Message.Contains("重なって", StringComparison.Ordinal));
    }

    /// <summary>
    /// 時代分割に隙間がある場合、その年の録音が保留になる旨を警告することを確認する。
    /// </summary>
    [Fact]
    public void WarnsOnGapBetweenEras()
    {
        TagDictionary dictionary = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "test",
                    Eras =
                    [
                        new EnsembleEra { Until = 1990, Canonical = "Old Name" },
                        new EnsembleEra { From = 1995, Canonical = "New Name" },
                    ],
                },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.False(DictionaryValidator.HasError(issues));
        Assert.Contains(issues, issue => issue.Message.Contains("隙間", StringComparison.Ordinal));
    }

    /// <summary>
    /// 既定辞書の <c>uk-philharmonia</c> のように、境界が接している期間は警告にならないことを確認する。
    /// </summary>
    [Fact]
    public void AcceptsAdjacentEras()
    {
        TagDictionary dictionary = new()
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
                        new EnsembleEra { From = 1977, Canonical = "Philharmonia Orchestra 2" },
                    ],
                },
            ],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.DoesNotContain(issues, issue =>
            issue.Message.Contains("隙間", StringComparison.Ordinal)
            || issue.Message.Contains("重なって", StringComparison.Ordinal));
    }

    /// <summary>
    /// 未知の役割名をエラーとして検出することを確認する。
    /// </summary>
    [Fact]
    public void DetectsUnknownRole()
    {
        TagDictionary dictionary = new()
        {
            Persons = [new PersonEntry { Canonical = "Someone", Roles = ["Pianist"] }],
        };

        Assert.True(DictionaryValidator.HasError(DictionaryValidator.Validate(dictionary)));
    }

    /// <summary>
    /// 正規形が空のエントリをエラーとして検出することを確認する。
    /// </summary>
    [Fact]
    public void DetectsEmptyCanonical()
    {
        TagDictionary dictionary = new()
        {
            Composers = [new ComposerEntry { Canonical = "  " }],
        };

        Assert.True(DictionaryValidator.HasError(DictionaryValidator.Validate(dictionary)));
    }

    /// <summary>
    /// 種別をまたいだ同名は警告に留まることを確認する。
    /// 照合の優先順位が変わるだけで、索引そのものは壊れない。
    /// </summary>
    [Fact]
    public void WarnsOnCrossCategoryDuplicate()
    {
        TagDictionary dictionary = new()
        {
            Composers = [new ComposerEntry { Canonical = "Bedřich Smetana", Aliases = ["Smetana"] }],
            Ensembles = [new EnsembleEntry { EntityId = "cz-smetana-quartet", Canonical = "Smetana" }],
        };

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        Assert.False(DictionaryValidator.HasError(issues));
        Assert.Contains(issues, issue => issue.Severity == DictionaryIssueSeverity.Warning);
    }
}
