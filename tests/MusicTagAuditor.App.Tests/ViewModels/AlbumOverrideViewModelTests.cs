using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 「このアルバムの扱いを決める」ダイアログのテスト（docs/SPEC.md 7.3.2 / 7.4.5）。
///
/// 守りたいのは 3 点。**フォルダと disc を明細から埋める**こと、**理由を空のまま登録させない**こと、
/// **何も起きない例外を作らせない**こと。理由の書いていない例外は後から消してよいか判断できない。
/// </summary>
public sealed class AlbumOverrideViewModelTests
{
    /// <summary>対象のフォルダ。</summary>
    private static readonly string FOLDER = Path.Combine("その他", "名曲集");

    /// <summary>
    /// フォルダと disc が明細から埋まり、理由が入るまで登録させないことを確認する。
    /// </summary>
    [Fact]
    public void FillsFolderAndDiscAndRequiresNote()
    {
        AlbumOverrideViewModel viewModel = Create();

        Assert.Equal(FOLDER, viewModel.Folder);
        Assert.Equal(2, viewModel.Disc);

        Assert.False(viewModel.CanApply(out string reason));
        Assert.Contains("理由", reason, StringComparison.Ordinal);

        viewModel.Note = "3.5 規則6 コンピレーション";

        Assert.True(viewModel.CanApply(out _));
        Assert.Equal(2, viewModel.BuildEntry().Disc);
    }

    /// <summary>
    /// フォルダ全体に広げると disc が空（＝そのフォルダの全ディスク）になることを確認する。
    /// </summary>
    [Fact]
    public void ClearsDiscForWholeFolder()
    {
        AlbumOverrideViewModel viewModel = Create();

        viewModel.Note = "3.5 規則6 コンピレーション";
        viewModel.AppliesToWholeFolder = true;

        Assert.Null(viewModel.BuildEntry().Disc);
    }

    /// <summary>
    /// 対象外でも作曲家でも作品名でもない例外を登録させないことを確認する（docs/SPEC.md 7.3.1）。
    /// 何も起きない例外は、後から見ても何がしたかったのか分からない。
    /// </summary>
    [Fact]
    public void RejectsOverrideThatDoesNothing()
    {
        AlbumOverrideViewModel viewModel = Create();

        viewModel.Note = "版が違う";
        viewModel.Excludes = false;

        Assert.False(viewModel.CanApply(out string reason));
        Assert.Contains("作曲家・作品名・年", reason, StringComparison.Ordinal);

        viewModel.WorkName = "Symphony No. 5 (Olympia)";

        Assert.True(viewModel.CanApply(out _));

        AlbumOverrideEntry entry = Assert.Single(viewModel.Apply().AlbumOverrides);

        Assert.False(entry.Exclude);
        Assert.Equal("Symphony No. 5 (Olympia)", entry.WorkName);
    }

    /// <summary>
    /// 単位の作曲家が選択肢の先頭に出ることを確認する。
    /// 主作品 + カップリング（3.5 規則5）で選ぶのはこの中の 1 人である。
    /// </summary>
    [Fact]
    public void ListsUnitComposersFirst()
    {
        AlbumOverrideViewModel viewModel = Create();

        Assert.Equal(["Anton Bruckner", "Johannes Brahms"], viewModel.Composers.Take(2));
        Assert.Contains("Anton Bruckner", viewModel.ComposersText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 同じフォルダに既に例外があれば、置き換えになることを伝える。
    /// 黙って 2 件目を作ると、先に見つかったほうしか効かない。
    /// </summary>
    [Fact]
    public void WarnsWhenOverrideAlreadyExists()
    {
        AlbumOverrideViewModel viewModel = Create(
        [
            new AlbumOverrideEntry { Folder = FOLDER, Disc = 2, Exclude = true, Note = "前の理由" },
        ]);

        Assert.Contains("前の理由", viewModel.Notice, StringComparison.Ordinal);

        viewModel.Note = "やっぱり作品名を決める";
        viewModel.Excludes = false;
        viewModel.WorkName = "Symphony No. 8";

        Assert.Single(viewModel.Apply().AlbumOverrides);
    }

    /// <summary>
    /// 年の保留から開いたときは、対象外を選ばせないことを確認する（docs/SPEC.md 7.4.4）。
    ///
    /// **そこまで来た単位は作品が決まっている＝主作品が定まっている**ので、規則6 には当たらない。
    /// 選べるままだと、タグが割れた単位を検出から消すだけの操作ができてしまう。
    /// </summary>
    [Fact]
    public void HidesExcludeForDateHold()
    {
        AlbumOverrideViewModel viewModel = CreateForSplitDate();

        Assert.False(viewModel.CanExclude);
        Assert.False(viewModel.Excludes);
        Assert.Contains("対象外（規則6）には当たりません", viewModel.Notice, StringComparison.Ordinal);
    }

    /// <summary>
    /// 年は単位内にある値からしか選べず、選んだ値が個別例外に入ることを確認する（3.5 規則2）。
    ///
    /// **手で打たせない。** どのファイルにも入っていない年を書けると、
    /// アルバム名だけが実在しない録音年を名乗る。
    /// </summary>
    [Fact]
    public void OffersOnlyDatesFoundInUnit()
    {
        AlbumOverrideViewModel viewModel = CreateForSplitDate();

        Assert.True(viewModel.HasSplitDates);
        Assert.Equal(["1971", "1972"], viewModel.Dates);

        viewModel.Note = "3.5 規則5 主作品は交響曲第6番";
        viewModel.Date = "1971";

        Assert.True(viewModel.CanApply(out _));
        Assert.Equal("1971", viewModel.BuildEntry().Date);
    }

    /// <summary>
    /// 年が割れていない単位では、年の指定を出さないことを確認する。選ぶものが 1 つしかない。
    /// </summary>
    [Fact]
    public void HidesDateWhenUnitHasSingleDate()
    {
        Assert.False(Create().HasSplitDates);
    }

    /// <summary>
    /// 作曲家が 2 人いる単位を対象にしたダイアログを作る。
    /// </summary>
    /// <param name="overrides">辞書に入れておく個別例外。</param>
    /// <returns>ビューモデル。</returns>
    private static AlbumOverrideViewModel Create(IReadOnlyList<AlbumOverrideEntry>? overrides = null)
    {
        AlbumUnit unit = new(
            FOLDER,
            2,
            [],
            ["Anton Bruckner", "Johannes Brahms"],
            ["Georg Solti"],
            ["1990"],
            ["名曲集"]);

        return new AlbumOverrideViewModel(Dictionary(overrides), unit);
    }

    /// <summary>
    /// 主作品 + カップリングで年が割れている単位のダイアログを作る（3.5 規則2・規則5）。
    /// </summary>
    /// <returns>ビューモデル。</returns>
    private static AlbumOverrideViewModel CreateForSplitDate()
    {
        AlbumUnit unit = new(
            FOLDER,
            1,
            [],
            ["Ludwig van Beethoven"],
            ["Karl Böhm"],
            ["1971", "1972"],
            ["Symphony No.6"]);

        return new AlbumOverrideViewModel(Dictionary(null), unit, HoldReason.DateUnknown);
    }

    /// <summary>
    /// テスト用の辞書を作る。
    /// </summary>
    /// <param name="overrides">辞書に入れておく個別例外。</param>
    /// <returns>辞書。</returns>
    private static TagDictionary Dictionary(IReadOnlyList<AlbumOverrideEntry>? overrides)
    {
        return new TagDictionary
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Anton Bruckner" },
                new ComposerEntry { Canonical = "Johannes Brahms" },
                new ComposerEntry { Canonical = "Franz Schubert" },
            ],
            AlbumOverrides = overrides ?? [],
        };
    }
}
