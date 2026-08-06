using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using MusicTagAuditor.App.ViewModels;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// <see cref="TrackViewRefresher"/> のテスト。
///
/// 守りたいのは「編集トランザクション中に Refresh を撃たない」の一点。
/// 撃つと ListCollectionView が例外を投げ、ファイル一覧の行が確定できなくなる
/// （握り潰されて編集が固まる／一括入力ではアプリが落ちる）。
/// </summary>
public sealed class TrackViewRefresherTests
{
    /// <summary>絞り込みを「a だけ」に切り替えるかどうか。</summary>
    private bool _keepsOnlyA;

    /// <summary>
    /// 編集していなければその場で絞り込む。
    /// </summary>
    [Fact]
    public void Request_絞り込みをその場で掛ける()
    {
        ICollectionView view = CreateView();
        TrackViewRefresher refresher = new(view);

        _keepsOnlyA = true;
        refresher.Request();

        Assert.False(refresher.IsPending);
        Assert.Single(view.Cast<string>());
    }

    /// <summary>
    /// 編集中は絞り込まずに見送る。例外を投げないことが本題。
    /// </summary>
    [Fact]
    public void Request_編集中は見送る()
    {
        ICollectionView view = CreateView();
        TrackViewRefresher refresher = new(view);

        ((IEditableCollectionView)view).EditItem("a");

        _keepsOnlyA = true;
        refresher.Request();

        Assert.True(refresher.IsPending);
        Assert.Equal(2, view.Cast<string>().Count());
    }

    /// <summary>
    /// 編集が終わったら、見送っていた分を掛け直す。
    /// </summary>
    [Fact]
    public void Resume_編集の確定後に見送った分を掛け直す()
    {
        ICollectionView view = CreateView();
        TrackViewRefresher refresher = new(view);
        IEditableCollectionView editable = (IEditableCollectionView)view;

        editable.EditItem("a");
        _keepsOnlyA = true;
        refresher.Request();

        editable.CommitEdit();
        refresher.Resume();

        Assert.False(refresher.IsPending);
        Assert.Single(view.Cast<string>());
    }

    /// <summary>
    /// まだ編集が続いているなら、掛け直しはさらに持ち越す。
    /// 呼ぶ側がトランザクションの開閉を気にしなくてよいことの担保。
    /// </summary>
    [Fact]
    public void Resume_編集が続いていれば持ち越す()
    {
        ICollectionView view = CreateView();
        TrackViewRefresher refresher = new(view);

        ((IEditableCollectionView)view).EditItem("a");
        _keepsOnlyA = true;
        refresher.Request();

        refresher.Resume();

        Assert.True(refresher.IsPending);
        Assert.Equal(2, view.Cast<string>().Count());
    }

    /// <summary>
    /// 見送っていないなら掛け直さない。無駄な作り直しで現在行やスクロール位置を飛ばさないため。
    /// </summary>
    [Fact]
    public void Resume_見送っていなければ何もしない()
    {
        ICollectionView view = CreateView();
        TrackViewRefresher refresher = new(view);

        // Request を挟まずに絞り込みの条件だけ変える。掛け直せば 1 件に減るはず。
        _keepsOnlyA = true;
        refresher.Resume();

        Assert.False(refresher.IsPending);
        Assert.Equal(2, view.Cast<string>().Count());
    }

    /// <summary>
    /// 2 件のビューを作る。
    ///
    /// **絞り込みは条件を外から差し替えられる形で最初に入れておく。** Filter の設定自体が
    /// Refresh を伴うため、編集を始めたあとに設定すると本題より先にそこで例外になる。
    /// ファイル一覧の絞り込みもビューモデルの可変な状態を読むので、実物に近い形でもある。
    /// </summary>
    private ICollectionView CreateView()
    {
        ObservableCollection<string> items = ["a", "b"];
        ICollectionView view = CollectionViewSource.GetDefaultView(items);

        view.Filter = item => !_keepsOnlyA || (string)item == "a";

        return view;
    }
}
