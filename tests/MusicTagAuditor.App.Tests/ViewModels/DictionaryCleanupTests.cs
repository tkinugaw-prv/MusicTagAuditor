using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 辞書タブの「冗長な別名を掃除」のテスト（docs/SPEC.md 7.3）。
///
/// 既定辞書を直しても利用者辞書には届かない（「既定辞書から取り込む」は追加しか作らない）。
/// **この導線が利用者辞書を掃除できる唯一の手段**なので、消える件数と残る内容を固定しておく。
/// </summary>
public sealed class DictionaryCleanupTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。辞書をここに置く。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-clean-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public DictionaryCleanupTests()
    {
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// 索引に載っていない別名を洗い出せることを確認する。
    /// </summary>
    [Fact]
    public void FindsRedundantAliases()
    {
        DictionaryViewModel viewModel = new(CreateStore());

        IReadOnlyList<RemovedAlias> plan = viewModel.BuildCleanupPlan();

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, item => item.Name == "Dvořák" && item.KeptName == "Dvorak");
        Assert.Contains(plan, item => item.Name == "Symphony No.9" && item.KeptName == "Symphony No. 9");
    }

    /// <summary>
    /// 掃除すると別名が消え、検証の問題も消えることを確認する。
    /// </summary>
    [Fact]
    public void RemovesRedundantAliasesAndClearsIssues()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        viewModel.ApplyCleanup();

        Assert.Equal("Dvorak", viewModel.Composers[0].AliasesText);
        Assert.Equal(string.Empty, viewModel.Works[0].AliasesText);
        Assert.Empty(viewModel.Issues);
        Assert.False(viewModel.IsDirty);
    }

    /// <summary>
    /// 掃除した内容がファイルに残ることを確認する。
    ///
    /// 画面の一覧だけ変えて保存を忘れると、次の起動で警告がそのまま戻る。
    /// </summary>
    [Fact]
    public void SavesCleanedDictionary()
    {
        DictionaryViewModel viewModel = new(CreateStore());

        viewModel.ApplyCleanup();

        DictionaryStore reopened = new(_directory);

        Assert.Equal(["Dvorak"], reopened.Dictionary.Composers[0].Aliases);
        Assert.Empty(DictionaryValidator.Validate(reopened.Dictionary));
    }

    /// <summary>
    /// 掃除しても引ける値が変わらないことを確認する。**掃除の前提**。
    /// </summary>
    [Fact]
    public void KeepsEveryValueResolvable()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        viewModel.ApplyCleanup();

        DictionaryIndex index = store.Index;

        Assert.True(index.TryResolveComposer("Dvořák", out string canonical));
        Assert.Equal("Antonín Dvořák", canonical);

        Assert.True(index.TryResolveWork("Antonín Dvořák", "Symphony No.9", out WorkEntry work));
        Assert.Equal("Symphony No. 9", work.Canonical);
    }

    /// <summary>
    /// 掃除するものが無ければ何もしないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNothingWhenAlreadyClean()
    {
        DictionaryStore store = new(_directory);
        DictionaryViewModel viewModel = new(store);

        Assert.Empty(viewModel.BuildCleanupPlan());

        viewModel.ApplyCleanup();

        Assert.Contains("ありません", viewModel.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 作業ディレクトリを片付ける。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 掃除に失敗してもテストの結果は変えない。
        }
    }

    /// <summary>
    /// 冗長な別名を持つ辞書のストアを作る。
    /// </summary>
    /// <returns>ストア。</returns>
    private DictionaryStore CreateStore()
    {
        DictionaryStore store = new(_directory);

        store.Save(store.Dictionary with
        {
            Composers = [new ComposerEntry { Canonical = "Antonín Dvořák", Aliases = ["Dvorak", "Dvořák"] }],
            Persons = [],
            Ensembles = [],
            Works =
            [
                new WorkEntry
                {
                    Composer = "Antonín Dvořák",
                    Canonical = "Symphony No. 9",
                    Aliases = ["Symphony No.9"],
                },
            ],
            AlbumOverrides = [],
            Typos = [],
            ProtectedAlbumArtists = [],
        });

        return store;
    }
}
