using System.Windows;
using MusicTagger.App.ViewModels;

namespace MusicTagger.App;

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
        InitializeComponent();
        DataContext = viewModel;
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
}
