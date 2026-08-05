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
    /// 選択とスクロールはレイアウト後に回す。タブを切り替えた直後は行のコンテナが
    /// まだ作られておらず、<c>ScrollIntoView</c> がその場では効かない。
    /// </summary>
    private void OnTrackRevealRequested(object? sender, TrackRowViewModel row)
    {
        MainTabs.SelectedItem = FileListTab;

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                // 複数選択のままだと、どの行へ飛んだのか分からなくなる。
                TrackGrid.UnselectAll();
                TrackGrid.SelectedItem = row;
                TrackGrid.ScrollIntoView(row);
                _ = TrackGrid.Focus();
            });
    }
}
