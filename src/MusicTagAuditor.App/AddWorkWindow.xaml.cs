using System.Windows;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.App.ViewModels;

namespace MusicTagAuditor.App;

/// <summary>
/// 検査結果の保留行から作品を辞書に足すダイアログ（docs/SPEC.md 7.3.2）。
/// </summary>
public partial class AddWorkWindow : Window
{
    /// <summary>バインドしているビューモデル。</summary>
    private readonly AddWorkViewModel _viewModel;

    /// <summary>
    /// ダイアログを初期化する。
    /// </summary>
    /// <param name="viewModel">バインドするビューモデル。</param>
    public AddWorkWindow(AddWorkViewModel viewModel)
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
    /// 足りない項目があるまま閉じると、呼び出し側で無言の失敗になる。
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
