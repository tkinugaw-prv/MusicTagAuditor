using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MusicTagAuditor.App.Controls;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App;

/// <summary>
/// メインウィンドウ。画面構成は docs/SPEC.md 5章を参照。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// メインウィンドウを初期化する。
    /// </summary>
    /// <param name="viewModel">バインドするビューモデル。</param>
    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;

        viewModel.TrackRevealRequested += OnTrackRevealRequested;
    }

    /// <summary>
    /// HWND 確定後に OS のタイトルバーをダークテーマへ合わせる。
    /// </summary>
    /// <param name="e">イベント引数。</param>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmDarkTitleBar.Apply(this);
    }

    /// <summary>
    /// ツリーの選択変更をビューモデルへ渡す。
    /// TreeView.SelectedItem は読み取り専用でバインドできないため、コードビハインドで中継する。
    /// </summary>
    private void OnFolderSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedFolder = e.NewValue as FolderNodeViewModel;
        }
    }

    /// <summary>
    /// 検査結果の差分明細をダブルクリックしたら、ファイル一覧の当該行へ飛ぶ。
    /// </summary>
    private void OnInspectionChangeRowDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is DataGridRow { Item: TagChangeViewModel change } && DataContext is MainViewModel viewModel)
        {
            viewModel.RevealTrack(change.RelativePath);
        }
    }

    /// <summary>
    /// ファイル一覧タブへ切り替えて、指定された行を選択・スクロール表示する。
    ///
    /// **一連の操作は入力イベントを抜けてから行う。** ここへ来るのはダブルクリックの
    /// 処理中で、そのあと DataGrid が明細のセルへフォーカスを戻す。その場でタブを
    /// 切り替えると、<c>TabItem</c> がフォーカスの移動に追従して検査結果へ選び直され、
    /// 切り替えが即座に取り消される。
    /// </summary>
    private void OnTrackRevealRequested(object? sender, TrackRowViewModel row)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                MainTabs.SelectedItem = FileListTab;

                // 行のコンテナはタブを描き直すまで作られず、ScrollIntoView が空振りする。
                MainTabs.UpdateLayout();

                // 複数選択のままだと、どの行へ飛んだのか分からなくなる。
                TrackGrid.UnselectAll();
                TrackGrid.SelectedItem = row;
                TrackGrid.ScrollIntoView(row);
                _ = TrackGrid.Focus();
            });
    }

    /// <summary>
    /// ファイル一覧に出ている行をすべて選ぶ。
    ///
    /// 選ばれるのは<b>絞り込みで残っている行だけ</b>である（<c>DataGrid.Items</c> はビュー）。
    /// フォルダをまたいだ修正では、絞り込んだ結果の全体が一括入力の対象になる。
    ///
    /// **先に編集中の行を確定させる。** 編集を開いたまま選択を広げると、DataGrid は
    /// 開いている行から抜けられず選択が途中で止まる（<see cref="OnTrackGridKeyboardFocusWithinChanged"/>）。
    /// </summary>
    private void OnSelectAllTracks(object sender, RoutedEventArgs e)
    {
        _ = TrackGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        TrackGrid.SelectAll();

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.NotifyTracksSelected(TrackGrid.SelectedItems.Count);
        }
    }

    /// <summary>
    /// 検証結果の行を押したら、その対象のエントリを開く。
    ///
    /// **既に選ばれている行を押したときのための導線。** 選択が変わる場合はビューモデルが
    /// <c>SelectedIssue</c> の変化で同じ処理を行う。<c>ListBox</c> は選択が変わらないと
    /// 通知を上げないため、それだけでは「もう一度押しても何も起きない」状態になる。
    /// 移動は何度行っても同じ結果になるので、両方から呼ばれても差し支えない。
    /// </summary>
    private void OnIssueRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: DictionaryIssue issue } item
            && ItemsControl.ItemsControlFromItemContainer(item)?.DataContext is DictionaryViewModel dictionary)
        {
            dictionary.RevealIssue(issue);
        }
    }

    /// <summary>
    /// セルの編集が終わったら、入力をその場で書き戻し、見送っていた絞り込みを掛け直させる。
    ///
    /// **ここで書き戻さないと、同じ行の中で移動しただけでは保留に入らない。**
    /// 理由は <see cref="CellEditCommit"/> に書いた。
    ///
    /// 取り消し（Esc）では書き戻さない。捨てるつもりの入力を保留に入れては意味が反転する。
    /// </summary>
    private void OnTrackCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            CellEditCommit.Flush(e.EditingElement);
        }

        ScheduleTrackEditFinished();
    }

    /// <summary>
    /// 行の編集が終わったら、見送っていた絞り込みを掛け直させる。確定でも取り消しでも上がる。
    /// </summary>
    private void OnTrackRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        ScheduleTrackEditFinished();
    }

    /// <summary>
    /// ファイル一覧から入力が離れたら、編集中の行をその場で確定させる。
    ///
    /// **DataGrid は入力が離れても行の編集を開いたままにする。** 開いたままだとビューの
    /// 編集トランザクションも開き続け、絞り込みの掛け直しも一覧の作り直しもできない。
    /// タブを切り替えるとその状態のまま視界から外れ、戻るまで絞り込みが黙って効かなくなる。
    /// 手編集はファイルへ書き込まれず保留されるだけなので、ここで確定させて失うものは無い。
    ///
    /// **ウィンドウが非アクティブになっただけなら確定しない。** 別のアプリへ切り替えると
    /// キーボードフォーカスは一旦アプリの外へ出る。そこで確定すると、戻ってきたときに
    /// 打ちかけの編集が閉じている。
    /// </summary>
    private void OnTrackGridKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (TrackGrid.IsKeyboardFocusWithin || !IsActive)
        {
            return;
        }

        _ = TrackGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }

    /// <summary>
    /// 差分明細のセル編集が終わったら、見送っていた絞り込みを掛け直させる。
    /// </summary>
    private void OnInspectionCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        ScheduleInspectionEditFinished();
    }

    /// <summary>
    /// 差分明細の行の編集が終わったら、見送っていた絞り込みを掛け直させる。
    /// </summary>
    private void OnInspectionRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        ScheduleInspectionEditFinished();
    }

    /// <summary>
    /// 差分明細から入力が離れたら、編集中の行をその場で確定させる。
    ///
    /// **適用チェックのクリックも編集トランザクションを開く。** 開いたままだと
    /// 「チェック済みのみ」の掛け直しが持ち越されたまま止まり、外したはずの行が残る。
    /// ファイル一覧と同じく、ウィンドウが非アクティブになっただけなら確定しない。
    /// </summary>
    private void OnInspectionGridKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (InspectionChangeGrid.IsKeyboardFocusWithin || !IsActive)
        {
            return;
        }

        _ = InspectionChangeGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        ScheduleInspectionEditFinished();
    }

    /// <summary>
    /// 編集トランザクションが閉じたあとに、保留していた絞り込みを掛け直させる。
    ///
    /// **その場では呼ばない。** ここへ来るのは DataGrid が確定処理を行う前で、
    /// ビューの編集トランザクションはまだ開いている。
    /// </summary>
    private void ScheduleTrackEditFinished()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () => viewModel.NotifyTrackEditFinished());
    }

    /// <summary>
    /// 差分明細の編集トランザクションが閉じたあとに、保留していた絞り込みを掛け直させる。
    ///
    /// **その場では呼ばない。** 理由は <see cref="ScheduleTrackEditFinished"/> と同じで、
    /// ここへ来るのは DataGrid が確定処理を行う前である。
    /// </summary>
    private void ScheduleInspectionEditFinished()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () => viewModel.NotifyInspectionEditFinished());
    }
}
