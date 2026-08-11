using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 「このアルバムを対象外にする」ダイアログのテスト（docs/SPEC.md 7.3.2 / 7.4.5）。
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
        Assert.Contains("作曲家か作品名", reason, StringComparison.Ordinal);

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
    /// 作曲家が 2 人いる単位を対象にしたダイアログを作る。
    /// </summary>
    /// <param name="overrides">辞書に入れておく個別例外。</param>
    /// <returns>ビューモデル。</returns>
    private static AlbumOverrideViewModel Create(IReadOnlyList<AlbumOverrideEntry>? overrides = null)
    {
        TagDictionary dictionary = new()
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Anton Bruckner" },
                new ComposerEntry { Canonical = "Johannes Brahms" },
                new ComposerEntry { Canonical = "Franz Schubert" },
            ],
            AlbumOverrides = overrides ?? [],
        };

        AlbumUnit unit = new(
            FOLDER,
            2,
            [],
            ["Anton Bruckner", "Johannes Brahms"],
            ["Georg Solti"],
            ["1990"],
            ["名曲集"]);

        return new AlbumOverrideViewModel(dictionary, unit);
    }
}
