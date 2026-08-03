using System.Windows;
using MusicTagger.App.ViewModels;

namespace MusicTagger.App;

/// <summary>
/// 検査結果から辞書に値を足すダイアログ（docs/SPEC.md 7.3）。
/// </summary>
public partial class AddToDictionaryWindow : Window
{
    /// <summary>バインドしているビューモデル。</summary>
    private readonly AddToDictionaryViewModel _viewModel;

    /// <summary>
    /// ダイアログを初期化する。
    /// </summary>
    /// <param name="viewModel">バインドするビューモデル。</param>
    public AddToDictionaryWindow(AddToDictionaryViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
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
