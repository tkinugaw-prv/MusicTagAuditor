using System.ComponentModel;
using System.Windows.Data;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 編集トランザクションを避けてコレクションビューを絞り直す。
///
/// **DataGrid が行を編集している最中は絞り直せない。** DataGrid は行の編集を始めるとき
/// ビューに <see cref="IEditableCollectionView.EditItem"/> のトランザクションを開き、
/// 行の確定・取り消しまで開いたままにする（セルを 1 つ確定しても閉じない）。
/// <see cref="ListCollectionView"/> はその最中の <see cref="ICollectionView.Refresh"/> を
/// InvalidOperationException で撥ねる。
///
/// この例外はセルの値をビューモデルへ書き戻す途中で起きるとバインディングに握り潰され、
/// 行の確定だけが黙って失敗し続ける（1 行目を編集すると他の行へ移れなくなる）。
/// 一括入力のようにコマンドから起きると、そのままアプリが落ちる。
///
/// 掛け直しは <see cref="Resume"/> まで持ち越す。
///
/// ファイル一覧と検査結果の差分明細で 1 つずつ使う。どちらもチェックボックス列か
/// セル編集を持ち、絞り込みの条件が編集の最中に変わりうる。
/// </summary>
public sealed class GridViewRefresher
{
    /// <summary>絞り込みを掛け直す対象のビュー。</summary>
    private readonly ICollectionView _view;

    /// <summary>掛け直しを見送っているか。</summary>
    private bool _isPending;

    /// <summary>
    /// 絞り直す相手を決める。
    /// </summary>
    /// <param name="view">グリッドが表示しているコレクションビュー。</param>
    public GridViewRefresher(ICollectionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _view = view;
    }

    /// <summary>掛け直しを見送っている状態か。</summary>
    public bool IsPending => _isPending;

    /// <summary>
    /// 絞り直す。編集中なら見送って <see cref="IsPending"/> を立てる。
    /// </summary>
    public void Request()
    {
        if (_view is IEditableCollectionView { IsEditingItem: true } or IEditableCollectionView { IsAddingNew: true })
        {
            _isPending = true;
            return;
        }

        _isPending = false;
        _view.Refresh();
    }

    /// <summary>
    /// 編集が終わったので、見送っていた分を掛け直す。
    ///
    /// まだ編集が続いていれば <see cref="Request"/> がもう一度見送るだけなので、
    /// 呼ぶ側はトランザクションが本当に閉じたかを気にしなくてよい。
    /// </summary>
    public void Resume()
    {
        if (!_isPending)
        {
            return;
        }

        Request();
    }
}
