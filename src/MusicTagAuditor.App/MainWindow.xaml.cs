using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Models;

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
        if (sender is DataGridRow { Item: TagChange change } && DataContext is MainViewModel viewModel)
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
}
