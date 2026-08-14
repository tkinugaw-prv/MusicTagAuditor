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
    /// 同梱の既定辞書が問題を 1 件も持たないことを確認する。
    ///
    /// **警告まで 0 件であることを見る。** 既定辞書は初回起動時にそのまま利用者辞書へコピーされる。
    /// 冗長な別名が混じっていると、利用者は自分で足した覚えのない警告を最初から抱えることになり、
    /// 自分が起こした問題との区別が付かなくなる（2026-08-14 に 22 件を削除した）。
    /// </summary>
    [Fact]
    public void DefaultDictionaryHasNoIssue()
    {
        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(DictionaryLoader.LoadDefault());

        Assert.Empty(issues);
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
                        new EnsembleEra { From = 1977, Canonical = "Philharmonia Orchestra" },
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
    /// 改名して元の名前に戻った団体を、別名の重複として報せないことを確認する。
    ///
    /// 既定辞書の <c>uk-philharmonia</c> は 1964 年までと 1977 年以降がどちらも
    /// <c>Philharmonia Orchestra</c> である。**これは重複した別名ではなく、消す先も無い。**
    /// 警告にすると、辞書を掃除しきっても消えない 1 件が残り続ける。
    /// </summary>
    [Fact]
    public void AcceptsRepeatedEraCanonical()
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
                        new EnsembleEra { From = 1977, Canonical = "Philharmonia Orchestra" },
                    ],
                },
            ],
        };

        Assert.Empty(DictionaryValidator.Validate(dictionary));
    }

    /// <summary>
    /// 時代分割を持つ団体でも、別名どうしの重複は変わらず報せることを確認する。
    /// 区分の正規形を畳んだせいで、本当に冗長な別名まで見逃さないための歯止め。
    /// </summary>
    [Fact]
    public void StillDetectsRedundantAliasOnEnsembleWithEras()
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
                        new EnsembleEra { From = 1964, Canonical = "New Philharmonia Orchestra" },
                    ],
                    Aliases = ["Philharmonia  Orchestra"],
                },
            ],
        };

        Assert.Contains(
            DictionaryValidator.Validate(dictionary),
            issue => issue.Message.Contains("同じエントリ内で重複", StringComparison.Ordinal));
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
    /// 個別例外の年が 4 桁でなければエラーにすることを確認する（docs/TAGGING_POLICY.md 2.4）。
    ///
    /// **ここに書いた値はアルバム名にそのまま入る。** ISO 形式や範囲表記を通すと、
    /// R-104 がタグに対して禁じている形が辞書経由で復活する。
    /// </summary>
    [Theory]
    [InlineData("1993-01-22T08:00:00Z")]
    [InlineData("1971/1972")]
    [InlineData("71")]
    public void DetectsInvalidOverrideDate(string date)
    {
        TagDictionary dictionary = new()
        {
            AlbumOverrides = [new AlbumOverrideEntry { Folder = "ベートーヴェン", Date = date, Note = "主作品の年" }],
        };

        Assert.True(DictionaryValidator.HasError(DictionaryValidator.Validate(dictionary)));
    }

    /// <summary>
    /// 年だけを指定した個別例外は「何もしない例外」ではないことを確認する（3.5 規則2）。
    /// </summary>
    [Fact]
    public void AcceptsOverrideWithDateOnly()
    {
        TagDictionary dictionary = new()
        {
            AlbumOverrides = [new AlbumOverrideEntry { Folder = "ベートーヴェン", Date = "1971", Note = "主作品の年" }],
        };

        Assert.Empty(DictionaryValidator.Validate(dictionary));
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
