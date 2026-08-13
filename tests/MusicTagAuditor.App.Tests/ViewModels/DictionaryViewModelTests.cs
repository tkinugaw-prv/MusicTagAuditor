using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 辞書タブの一覧の並べ替えと、検証結果の表示のテスト。
///
/// 一覧は辞書ファイルの配列順で出ていた。エントリを足すほど末尾が無秩序になり、
/// 同じ人物が 2 エントリに分かれていないかを目で確かめられなくなる。
/// **並べるのは表示だけで、保存する順序は動かさない**ことをここで固定する。
/// </summary>
public sealed class DictionaryViewModelTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-dict-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public DictionaryViewModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// 人物の一覧が名前の昇順で表示されることを確認する。
    ///
    /// 既定辞書の <c>persons</c> は役割ごと（指揮者 → ソリスト）に並んでいて、
    /// 全体では昇順になっていない。ビューが並べていなければここで落ちる。
    /// </summary>
    [Fact]
    public void 人物の一覧は名前の昇順で表示される()
    {
        DictionaryViewModel viewModel = CreateViewModel();

        string[] shown = [.. View(viewModel.Persons).Cast<PersonRowViewModel>().Select(row => row.Canonical)];

        Assert.NotEmpty(shown);
        Assert.Equal([.. shown.Order(StringComparer.CurrentCulture)], shown);
    }

    /// <summary>
    /// 作曲家の一覧も名前の昇順で表示されることを確認する。
    /// </summary>
    [Fact]
    public void 作曲家の一覧は名前の昇順で表示される()
    {
        DictionaryViewModel viewModel = CreateViewModel();

        string[] shown = [.. View(viewModel.Composers).Cast<ComposerRowViewModel>().Select(row => row.Canonical)];

        Assert.NotEmpty(shown);
        Assert.Equal([.. shown.Order(StringComparer.CurrentCulture)], shown);
    }

    /// <summary>
    /// 表示の並べ替えが、保存に使うコレクションの順序を変えないことを確認する。
    ///
    /// **誤記（<c>typos</c>）は書かれた順に置換を重ねる。** 表示の都合で並べ替えたものを
    /// 保存すると置換の結果が変わりうる。JSON の差分も無用に膨らむ。
    /// </summary>
    [Fact]
    public void 表示の並べ替えは保存する順序を変えない()
    {
        DictionaryStore store = new(_root);
        DictionaryViewModel viewModel = new(store);

        string[] onFile = [.. (store.Dictionary.Persons ?? []).Select(entry => entry.Canonical)];
        string[] inCollection = [.. viewModel.Persons.Select(row => row.Canonical)];

        // 実体はファイルの順序のまま。
        Assert.Equal(onFile, inCollection);

        // 一方でビューは昇順。並べ替えが実体に及んでいないことを、両者の食い違いで確かめる。
        string[] shown = [.. View(viewModel.Persons).Cast<PersonRowViewModel>().Select(row => row.Canonical)];

        Assert.NotEqual(inCollection, shown);
    }

    /// <summary>
    /// 追加した行が末尾に積まれること（＝保存順は追加順）を確認する。
    /// </summary>
    [Fact]
    public void 追加した行は実体の末尾に積まれる()
    {
        DictionaryViewModel viewModel = CreateViewModel();

        viewModel.AddComposerCommand.Execute(null);

        Assert.Same(viewModel.SelectedComposer, viewModel.Composers[^1]);
    }

    /// <summary>
    /// 検証結果を閉じられること、閉じても検証をやり直せば出し直せることを確認する。
    ///
    /// 直しようのない警告が居座ると編集領域の高さを取り続ける。**消えるのは表示だけ**で、
    /// 問題そのものが解決したことにはならない。
    /// </summary>
    [Fact]
    public void 検証結果は閉じられて検証で出し直せる()
    {
        DictionaryViewModel viewModel = CreateViewModel();

        // 既定辞書の内容に依存しないよう、確実に問題になる行を足す。
        viewModel.AddComposerCommand.Execute(null);
        viewModel.Composers[^1].Canonical = "テスト作曲家";
        viewModel.Composers[^1].AliasesText = viewModel.Composers[0].Canonical;

        viewModel.ValidateCommand.Execute(null);
        Assert.NotEmpty(viewModel.Issues);

        viewModel.DismissIssuesCommand.Execute(null);
        Assert.Empty(viewModel.Issues);

        viewModel.ValidateCommand.Execute(null);
        Assert.NotEmpty(viewModel.Issues);
    }

    /// <summary>
    /// 作業ディレクトリを片付ける。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 掃除に失敗してもテストの結果は変えない。
        }
    }

    /// <summary>
    /// 既定辞書を読み込んだビューモデルを作る。
    /// </summary>
    private DictionaryViewModel CreateViewModel()
    {
        return new DictionaryViewModel(new DictionaryStore(_root));
    }

    /// <summary>
    /// 画面に出る順序（＝絞り込みと並べ替えを通したビュー）を取り出す。
    /// </summary>
    private static ICollectionView View(System.Collections.IEnumerable source)
    {
        return CollectionViewSource.GetDefaultView(source);
    }
}
