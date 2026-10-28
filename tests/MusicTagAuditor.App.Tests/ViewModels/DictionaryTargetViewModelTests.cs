using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// ファイル一覧の右クリック「辞書に追加」に並べる項目のテスト（docs/SPEC.md 5.2）。
///
/// 守りたいのは 3 点。**列ごとに正しい登録先へ振り分ける**こと、**押せない項目も消さずに理由を添える**こと、
/// **曲名から作品を足すときは行の作曲家を使う**こと（カップリング盤では単位の作曲家が 1 人に決まらない）。
/// </summary>
public sealed class DictionaryTargetViewModelTests
{
    /// <summary>
    /// 辞書が扱うフィールドだけを、一覧の列順に並べることを確認する。
    /// ジャンル・年などは辞書の種別に無いので出さない。
    /// </summary>
    [Fact]
    public void ListsDictionaryFieldsInColumnOrder()
    {
        IReadOnlyList<DictionaryTargetViewModel> targets = Build(Row(), Unit());

        Assert.Equal(
            [TagField.Title, TagField.Composer, TagField.Artist, TagField.Conductor, TagField.AlbumArtist, TagField.Album],
            targets.Select(target => target.Field));

        Assert.Equal(
            [DictionaryTargetKind.Work, DictionaryTargetKind.Value, DictionaryTargetKind.Value,
                DictionaryTargetKind.Value, DictionaryTargetKind.Value, DictionaryTargetKind.Work],
            targets.Select(target => target.Kind));

        Assert.Equal(DictionaryCategory.Person, Target(targets, TagField.Artist).Category);
        Assert.Equal(DictionaryCategory.Person, Target(targets, TagField.Conductor).Category);
        Assert.Equal(DictionaryCategory.Ensemble, Target(targets, TagField.AlbumArtist).Category);
    }

    /// <summary>
    /// 辞書で既に引ける値は押せず、どこに登録済みかを理由に出すことを確認する。
    /// まだ無い値は押せる。
    /// </summary>
    [Fact]
    public void BlocksValuesAlreadyInDictionary()
    {
        IReadOnlyList<DictionaryTargetViewModel> targets = Build(Row(), Unit());

        DictionaryTargetViewModel composer = Target(targets, TagField.Composer);

        Assert.False(composer.CanAdd);
        Assert.Contains("Johannes Brahms", composer.BlockedReason, StringComparison.Ordinal);
        Assert.Contains("追加できません", composer.Header, StringComparison.Ordinal);

        Assert.True(Target(targets, TagField.Conductor).CanAdd);
        Assert.True(Target(targets, TagField.AlbumArtist).CanAdd);
    }

    /// <summary>
    /// 曲名からは、行の作曲家の正規形を作品の作曲家にすることを確認する。
    ///
    /// **単位の作曲家を使わない。** 単位内に作曲家が複数いても（カップリング盤）、
    /// 曲名が属する作品の作曲家はその行の <c>composer</c> で決まる。
    /// </summary>
    [Fact]
    public void TitleUsesRowComposerEvenWhenUnitHasSeveral()
    {
        IReadOnlyList<DictionaryTargetViewModel> targets = Build(
            Row((TagField.Composer, "Brahms")),
            Unit(composers: ["Johannes Brahms", "Antonín Dvořák"]));

        DictionaryTargetViewModel title = Target(targets, TagField.Title);

        Assert.True(title.CanAdd);
        Assert.Equal("Johannes Brahms", title.Composer);

        // アルバムは単位の作曲家で引くので、複数いれば押せない（7.3.2 と同じ線引き）。
        DictionaryTargetViewModel album = Target(targets, TagField.Album);

        Assert.False(album.CanAdd);
        Assert.Contains("このアルバムの扱いを決める", album.BlockedReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 行の作曲家が辞書に無ければ、曲名から作品を足させないことを確認する。
    /// 作品の作曲家は <c>composers</c> の正規形と一致させる必要がある（7.4.1）。
    /// </summary>
    [Fact]
    public void BlocksTitleWhenComposerIsUnknown()
    {
        DictionaryTargetViewModel title = Target(
            Build(Row((TagField.Composer, "Unknown Composer")), Unit()),
            TagField.Title);

        Assert.False(title.CanAdd);
        Assert.Null(title.Composer);
        Assert.Contains("先に作曲家を登録", title.BlockedReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 検査前（アルバム単位が無い）は作品の項目を押させず、検査を促すことを確認する。
    /// 作曲家・人物・団体は単位を使わないので押せる。
    /// </summary>
    [Fact]
    public void BlocksWorksBeforeInspection()
    {
        IReadOnlyList<DictionaryTargetViewModel> targets = Build(Row(), null);

        Assert.All(
            targets.Where(target => target.Kind == DictionaryTargetKind.Work),
            target => Assert.Contains("検査を実行", target.BlockedReason, StringComparison.Ordinal));

        Assert.True(Target(targets, TagField.Conductor).CanAdd);
    }

    /// <summary>
    /// 曲名が既に同じ作曲家の作品として引けるなら押させないことを確認する。
    /// </summary>
    [Fact]
    public void BlocksTitleAlreadyRegisteredAsWork()
    {
        DictionaryTargetViewModel title = Target(
            Build(Row(), Unit(), [new WorkEntry { Composer = "Johannes Brahms", Canonical = "Alto Rhapsody" }]),
            TagField.Title);

        Assert.False(title.CanAdd);
        Assert.Contains("作品「Alto Rhapsody」", title.BlockedReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 配役情報を含む <c>albumartist</c> は団体として登録させないことを確認する（TAGGING_POLICY 2.3）。
    /// </summary>
    [Fact]
    public void BlocksProtectedAlbumArtist()
    {
        const string PROTECTED = "Peter Pears(T); Hermann Prey(BR)";

        TagDictionary dictionary = Dictionary() with { ProtectedAlbumArtists = [PROTECTED] };
        TrackRowViewModel row = Row((TagField.AlbumArtist, PROTECTED));

        DictionaryTargetViewModel albumArtist = Target(
            DictionaryTargetViewModel.Build(row, new DictionaryIndex(dictionary), Unit(), null),
            TagField.AlbumArtist);

        Assert.False(albumArtist.CanAdd);
        Assert.Contains("protectedAlbumArtists", albumArtist.BlockedReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定したフィールドの項目を 1 つ取り出す。
    /// </summary>
    private static DictionaryTargetViewModel Target(IEnumerable<DictionaryTargetViewModel> targets, TagField field)
    {
        return Assert.Single(targets, target => target.Field == field);
    }

    /// <summary>
    /// 項目を組み立てる。
    /// </summary>
    private static IReadOnlyList<DictionaryTargetViewModel> Build(
        TrackRowViewModel row,
        AlbumUnit? unit,
        IReadOnlyList<WorkEntry>? works = null)
    {
        TagDictionary dictionary = Dictionary() with { Works = works ?? [] };

        return DictionaryTargetViewModel.Build(row, new DictionaryIndex(dictionary), unit, null);
    }

    /// <summary>
    /// ブラームスだけを持つ辞書を作る。
    /// </summary>
    private static TagDictionary Dictionary()
    {
        return new TagDictionary
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Johannes Brahms", Aliases = ["Brahms"], AliasesJa = ["ブラームス"] },
            ],
        };
    }

    /// <summary>
    /// ブラームスのカップリング盤の単位を作る。
    /// </summary>
    private static AlbumUnit Unit(IReadOnlyList<string>? composers = null)
    {
        return new AlbumUnit(
            Path.Combine("ブラームス", "ブラームス 悲劇的序曲 - ベーム"),
            1,
            [],
            composers ?? ["Johannes Brahms"],
            ["Karl Böhm"],
            ["1976"],
            ["Tragic Overture, Alto Rhapsody"]);
    }

    /// <summary>
    /// スクリーンショットの行（ブラームス「アルト・ラプソディ」）を作る。指定したフィールドだけ差し替える。
    /// </summary>
    private static TrackRowViewModel Row(params (TagField Field, string Value)[] overrides)
    {
        Dictionary<TagField, string> values = new()
        {
            [TagField.Title] = "Alto Rhapsody",
            [TagField.Composer] = "Johannes Brahms",
            [TagField.Artist] = "Karl Böhm",
            [TagField.Conductor] = "Karl Böhm",
            [TagField.AlbumArtist] = "Wiener Philharmoniker",
            [TagField.Album] = "Tragic Overture, Alto Rhapsody",
            [TagField.Genre] = "Classic",
            [TagField.Date] = "1976",
        };

        foreach ((TagField field, string value) in overrides)
        {
            values[field] = value;
        }

        TrackTags tags = new()
        {
            RelativePath = Path.Combine("ブラームス", "ブラームス 悲劇的序曲 - ベーム", "05 Alto Rhapsody.flac"),
            FullPath = Path.Combine(@"D:\Library", "ブラームス", "ブラームス 悲劇的序曲 - ベーム", "05 Alto Rhapsody.flac"),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
                values.Select(pair => new KeyValuePair<TagField, IReadOnlyList<string>>(pair.Key, [pair.Value]))),
            RawTags = new Dictionary<string, string[]>(),
        };

        return new TrackRowViewModel(tags, new ManualEditSet());
    }
}
