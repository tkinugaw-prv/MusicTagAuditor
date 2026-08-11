using System.Windows;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.App.ViewModels;

namespace MusicTagAuditor.App;

/// <summary>
/// 検査結果からアルバム単位の個別例外を足すダイアログ（docs/SPEC.md 7.3.2 / 7.4.5）。
/// </summary>
public partial class AlbumOverrideWindow : Window
{
    /// <summary>バインドしているビューモデル。</summary>
    private readonly AlbumOverrideViewModel _viewModel;

    /// <summary>
    /// ダイアログを初期化する。
    /// </summary>
    /// <param name="viewModel">バインドするビューモデル。</param>
    public AlbumOverrideWindow(AlbumOverrideViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
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
    /// 入力を確かめてからダイアログを閉じる。
    /// **理由が空のまま閉じさせない**（docs/SPEC.md 7.3.2）。
    /// </summary>
    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanApply(out string reason))
        {
            MessageBox.Show(reason, "入力を確認してください", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    /// <summary>
    /// 何もせずに閉じる。
    /// </summary>
    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
