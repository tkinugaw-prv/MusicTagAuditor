using System.ComponentModel;
using System.Windows;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App;

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

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 演奏団体の新規登録欄が現れたら、実体 ID の先頭（国コードを打つ位置）へ
    /// カーソルを合わせる。自動提案の値をそのまま見落とすのを防ぐため。
    /// </summary>
    /// <param name="sender">送信元。</param>
    /// <param name="e">変更されたプロパティ名。</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AddToDictionaryViewModel.AddsToExisting)
            or nameof(AddToDictionaryViewModel.Category)))
        {
            return;
        }

        if (_viewModel.AddsToExisting || _viewModel.Category != DictionaryCategory.Ensemble)
        {
            return;
        }

        // Visibility の切り替えがレイアウトへ反映されてからでないとフォーカスが効かない。
        Dispatcher.BeginInvoke(() =>
        {
            EntityIdTextBox.Focus();
            EntityIdTextBox.CaretIndex = 0;
        });
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
