using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Inspection;
using MusicTagger.Core.Models;
using MusicTagger.Core.Scanning;

namespace MusicTagger.Core.Tests.Inspection;

/// <summary>
/// 検査ルールのテスト。
///
/// docs/library-baseline-2026-08-03.md で判明した**誤検出の原因**を重点的に固定する。
/// 素朴に実装すると `Smetana Quartet` や `USSR` を拾ってしまう。
/// </summary>
public sealed class InspectionRuleTests
{
    /// <summary>テストで使うライブラリルート。</summary>
    private const string LIBRARY_ROOT = @"D:\Library";

    /// <summary>既定辞書の索引。</summary>
    private static readonly DictionaryIndex DICTIONARY = new(DictionaryLoader.LoadDefault());

    /// <summary>
    /// 楽団名に作曲家の姓が含まれていても R-203 / R-204 で拾わないことを確認する。
    /// `Smetana Quartet` はスメタナ四重奏団であって作曲家スメタナではない。
    /// </summary>
    [Fact]
    public void DoesNotFlagEnsembleNameContainingComposerSurname()
    {
        InspectionResult result = Inspect(
            Track("ベト 弦楽四重奏曲 14/01.flac",
                (TagField.Artist, ["Smetana Quartet"]),
                (TagField.AlbumArtist, ["Smetana Quartet"]),
                (TagField.Composer, ["Ludwig van Beethoven"])));

        Assert.Empty(ChangesOf(result, "R-203"));
        Assert.Empty(ChangesOf(result, "R-204"));
    }

    /// <summary>
    /// <c>Münchener Bach-Chor</c> を作曲家名として拾わないことを確認する。
    /// -ener は正しい表記であり、団体名なので Bach も作曲家ではない（5.3.3）。
    /// </summary>
    [Fact]
    public void DoesNotFlagMuenchenerBachChor()
    {
        InspectionResult result = Inspect(
            Track("バッハ/01.flac",
                (TagField.AlbumArtist, ["Münchener Bach-Chor"]),
                (TagField.Composer, ["Johann Sebastian Bach"])));

        Assert.Empty(ChangesOf(result, "R-204"));
        Assert.Empty(ChangesOf(result, "R-202"));
    }

    /// <summary>
    /// 頭字語を全大文字として拾わないことを確認する。`USSR State Symphony Orchestra` は正しい表記。
    /// </summary>
    [Fact]
    public void DoesNotFlagAcronymAsAllCaps()
    {
        InspectionResult result = Inspect(
            Track("01.flac", (TagField.AlbumArtist, ["USSR State Symphony Orchestra"])));

        Assert.Empty(ChangesOf(result, "R-207"));
    }

    /// <summary>
    /// 団体名の読点を「姓, 名」順として拾わないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotFlagEnsembleCommaAsSurnameFirst()
    {
        InspectionResult result = Inspect(
            Track("01.flac", (TagField.AlbumArtist, ["Kirov Orchestra, Mariinsky Theatre"])));

        Assert.Empty(ChangesOf(result, "R-207"));
    }

    /// <summary>
    /// 保護対象の <c>albumartist</c> が全ルールから除外されることを確認する（2.3）。
    /// 除外しないと R-205 / R-207 / R-208 が誤検出だらけになる。
    /// </summary>
    [Fact]
    public void ExcludesProtectedAlbumArtistFromAllRules()
    {
        const string PROTECTED = "Kommerchor Stuttgart(Chorus); Karl Münchinger; Stuttgarter Kammerorchester";

        InspectionResult result = Inspect(
            Track("バッハ/11.m4a",
                (TagField.AlbumArtist, [PROTECTED]),
                (TagField.Composer, ["Johann Sebastian Bach"])));

        Assert.DoesNotContain(result.AllChanges, change => change.Field == TagField.AlbumArtist);
    }

    /// <summary>
    /// 日本語表記の保護対象も除外されることを確認する。
    /// パルジファルの配役は 3.1 の言語規則に反するが、保護が優先する。
    /// </summary>
    [Fact]
    public void ExcludesJapaneseProtectedAlbumArtist()
    {
        const string PROTECTED =
            "カラヤン/ベルリン・フィル,ベルリン・ドイツ・オペラCho,ホセ・ヴァン・ダム,ヴィクター・フォン・ハーレム,クルト・モル,ペーター・ホフマン etc";

        InspectionResult result = Inspect(
            Track("ワーグナー/パルジファル/1-01.m4a", (TagField.AlbumArtist, [PROTECTED])));

        Assert.Empty(ChangesOf(result, "R-208"));
    }

    /// <summary>
    /// 録音年から収録時点の団体名を決められることを確認する（5.3.1）。
    /// </summary>
    [Theory]
    [InlineData("1979", "Leningrad Philharmonic Orchestra")]
    [InlineData("1995", "Saint Petersburg Philharmonic Orchestra")]
    public void ResolvesEnsembleNameByRecordingYear(string date, string expected)
    {
        InspectionResult result = Inspect(
            Track("01.flac",
                (TagField.AlbumArtist, ["Leningrad Philharmonic"]),
                (TagField.Date, [date])));

        TagChange change = Assert.Single(ChangesOf(result, "R-209"));

        Assert.Equal(expected, change.AfterText);
        Assert.Equal(HoldReason.None, change.HoldReason);
    }

    /// <summary>
    /// <c>date</c> が空欄の時代分割対象を保留にすることを確認する（7.5 の <c>HOLD_ERA_UNKNOWN</c>）。
    /// 誤った値で埋めるより保留のほうが後から対処できる。
    /// </summary>
    [Fact]
    public void HoldsWhenRecordingYearIsUnknown()
    {
        InspectionResult result = Inspect(
            Track("01.flac", (TagField.AlbumArtist, ["Leningrad Philharmonic"])));

        TagChange change = Assert.Single(ChangesOf(result, "R-209"));

        Assert.Equal(HoldReason.EraUnknown, change.HoldReason);
        Assert.False(change.HasFix);
        Assert.False(change.IsSelected);
        Assert.Equal("保留", change.Classification);
    }

    /// <summary>
    /// 時代分割しない個別例外（5.3.2 コンセルトヘボウ）は <c>date</c> 不明でも保留にしないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotHoldForEnsembleWithoutEraSplit()
    {
        InspectionResult result = Inspect(
            Track("01.flac", (TagField.AlbumArtist, ["Royal Concertgebouw Orchestra"])));

        Assert.Empty(ChangesOf(result, "R-209"));

        TagChange change = Assert.Single(ChangesOf(result, "R-202"));
        Assert.Equal("Concertgebouworkest", change.AfterText);
    }

    /// <summary>
    /// 名前が似ていても別実体なら別扱いすることを確認する。
    /// `Leningrad Philharmonic Orchestra` と `Leningrad Radio Orchestra` は別団体。
    /// </summary>
    [Fact]
    public void TreatsSimilarNamesAsDifferentEntities()
    {
        Assert.True(DICTIONARY.TryResolveEnsemble("Leningrad Philharmonic Orchestra", out EnsembleEntry phil));
        Assert.True(DICTIONARY.TryResolveEnsemble("Leningrad Radio Orchestra", out EnsembleEntry radio));

        Assert.NotEqual(phil.EntityId, radio.EntityId);
    }

    /// <summary>
    /// 名前が全く違っても同一実体なら同じ扱いにすることを確認する。
    /// `Leningrad Philharmonic Orchestra` と `Saint Petersburg Philharmonic Orchestra` は同一団体。
    /// </summary>
    [Fact]
    public void TreatsDifferentNamesAsSameEntity()
    {
        Assert.True(DICTIONARY.TryResolveEnsemble("Leningrad Philharmonic Orchestra", out EnsembleEntry leningrad));
        Assert.True(DICTIONARY.TryResolveEnsemble("Saint Petersburg Philharmonic Orchestra", out EnsembleEntry petersburg));

        Assert.Equal(leningrad.EntityId, petersburg.EntityId);
    }

    /// <summary>
    /// フォルダ名から指揮者を特定できることを確認する（6.2 の手順1）。
    /// </summary>
    [Fact]
    public void IdentifiesConductorFromFolderName()
    {
        InspectionResult result = Inspect(
            Track("ブルックナー/ブルックナー 8 - ショルティ/01.flac",
                (TagField.Artist, ["Anton Bruckner"]),
                (TagField.Composer, ["Anton Bruckner"])));

        TagChange change = Assert.Single(ChangesOf(result, "R-203"));

        Assert.Equal("Georg Solti", change.AfterText);
        Assert.Contains("フォルダ名", change.Rationale, StringComparison.Ordinal);
        Assert.True(change.IsSelected);
    }

    /// <summary>
    /// 指揮者を特定できない場合は自動修正しないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotGuessConductorWhenUnidentifiable()
    {
        InspectionResult result = Inspect(
            Track("ワーグナー/Wagner Opera Choruses/01.flac",
                (TagField.Artist, ["Richard Wagner"]),
                (TagField.Composer, ["Richard Wagner"])));

        TagChange change = Assert.Single(ChangesOf(result, "R-203"));

        Assert.False(change.HasFix);
        Assert.Equal(Severity.Info, change.Severity);
        Assert.False(change.IsSelected);
    }

    /// <summary>
    /// 同一フォルダの他ファイルから指揮者を引き継げることを確認する（6.2 の手順3）。
    /// </summary>
    [Fact]
    public void IdentifiesConductorFromSiblingFile()
    {
        InspectionResult result = Inspect(
            Track("なにかのアルバム/01.flac",
                (TagField.Artist, ["Anton Bruckner"]),
                (TagField.Composer, ["Anton Bruckner"])),
            Track("なにかのアルバム/02.flac",
                (TagField.Conductor, ["Günter Wand"]),
                (TagField.Composer, ["Anton Bruckner"])));

        TagChange change = Assert.Single(ChangesOf(result, "R-203"));

        Assert.Equal("Günter Wand", change.AfterText);
        Assert.Contains("同一フォルダ", change.Rationale, StringComparison.Ordinal);
    }

    /// <summary>
    /// 単一ディスクなら <c>1/1</c> を補い、複数ディスクなら補わないことを確認する。
    /// </summary>
    [Fact]
    public void FillsDiscNumberOnlyForSingleDisc()
    {
        InspectionResult single = Inspect(Track("単一/01.flac"));

        Assert.Equal("1/1", Assert.Single(ChangesOf(single, "R-103")).AfterText);

        InspectionResult multi = Inspect(
            Track("複数/1-01.flac"),
            Track("複数/2-01.flac", (TagField.DiscNumber, ["2/2"])));

        TagChange change = Assert.Single(ChangesOf(multi, "R-103"));
        Assert.False(change.HasFix);
    }

    /// <summary>
    /// ISO 形式の日付から年を取り出せることを確認する。
    /// </summary>
    [Fact]
    public void ExtractsYearFromIsoDate()
    {
        InspectionResult result = Inspect(
            Track("01.flac", (TagField.Date, ["1993-01-22T08:00:00Z"])));

        Assert.Equal("1993", Assert.Single(ChangesOf(result, "R-104")).AfterText);
    }

    /// <summary>
    /// 重大度に応じて既定のチェック状態が決まることを確認する（docs/SPEC.md 9章）。
    /// </summary>
    [Fact]
    public void SelectsErrorsByDefaultAndLeavesInfoUnchecked()
    {
        InspectionResult result = Inspect(
            Track("ブルックナー/ブルックナー 8 - ショルティ/01.flac",
                (TagField.Artist, ["Anton Bruckner"]),
                (TagField.Composer, ["Anton Bruckner"]),
                (TagField.Date, [])));

        Assert.True(Assert.Single(ChangesOf(result, "R-203")).IsSelected);
        Assert.False(Assert.Single(ChangesOf(result, "R-105")).IsSelected);
    }

    /// <summary>
    /// ルールを空で渡しても既定の一式で動くことを確認する。
    ///
    /// DI コンテナは <c>IEnumerable&lt;T&gt;</c> を要求されると、T が未登録でも
    /// 既定値ではなく空のコレクションを注入する。これを素通しすると検査が常に 0 件になり、
    /// 例外も出ないため気づけない。実際に一度この状態で動いてしまった。
    /// </summary>
    [Fact]
    public void FallsBackToDefaultRulesWhenNoneProvided()
    {
        Assert.NotEqual(0, new InspectionEngine([]).RuleCount);
        Assert.NotEqual(0, new InspectionEngine().RuleCount);
        Assert.Equal(InspectionEngine.CreateDefaultRules().Count, new InspectionEngine([]).RuleCount);
    }

    /// <summary>
    /// 既定のルール一式に ID の重複が無いことを確認する。
    /// </summary>
    [Fact]
    public void HasNoDuplicateRuleIds()
    {
        string[] ids = [.. InspectionEngine.CreateDefaultRules().Select(rule => rule.Id)];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
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
        ScanResult scan = new(LIBRARY_ROOT, tracks, [], TimeSpan.Zero);

        return new InspectionEngine().Inspect(new InspectionContext(scan, DICTIONARY));
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
