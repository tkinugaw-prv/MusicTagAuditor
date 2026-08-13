using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// 選択された行を必ず見える位置まで送る添付ビヘイビア。
///
/// **WPF の一覧は、選択を外から変えてもスクロールしない。** キーボードで動かしたときだけ
/// 追従する。そのため「警告をクリックして該当エントリへ飛ぶ」「追加した行を選ぶ」といった
/// 操作が、選択だけ変わって画面は動かない、という結果になる。数十件ある一覧では
/// どこへ飛んだのか分からず、操作そのものが無かったように見える。
///
/// <see cref="ListBox"/> と <see cref="DataGrid"/> の両方に付く。両者は
/// <c>ScrollIntoView</c> を共通の基底に持たないので、ここで振り分ける。
/// </summary>
public static class ScrollToSelection
{
    /// <summary>有効にするかどうか。</summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(ScrollToSelection),
            new PropertyMetadata(false, OnEnabledChanged));

    /// <summary>
    /// 添付プロパティの値を取得する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <returns>有効なら true。</returns>
    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(EnabledProperty);
    }

    /// <summary>
    /// 添付プロパティの値を設定する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <param name="value">有効にするなら true。</param>
    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(EnabledProperty, value);
    }

    /// <summary>
    /// 有効・無効の切り替えで購読を張り替える。
    /// </summary>
    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Selector selector)
        {
            return;
        }

        selector.SelectionChanged -= OnSelectionChanged;

        if (e.NewValue is true)
        {
            selector.SelectionChanged += OnSelectionChanged;
        }
    }

    /// <summary>
    /// 選択が変わったら、その行を見える位置へ送る。
    ///
    /// **その場では送らない。** 選択の変更が一覧の作り直しと同じ処理の中で起きると、
    /// 行のコンテナがまだ作られておらず <c>ScrollIntoView</c> が空振りする
    /// （仮想化した一覧では、画面外の行にコンテナが無い）。描画の後まで待つ。
    /// </summary>
    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not Selector selector || selector.SelectedItem is not object item)
        {
            return;
        }

        _ = selector.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () =>
            {
                // 待っているあいだに選択が変わっていることがある。今の選択だけを送る。
                if (!ReferenceEquals(selector.SelectedItem, item))
                {
                    return;
                }

                switch (selector)
                {
                    case ListBox list:
                        list.ScrollIntoView(item);
                        break;

                    case DataGrid grid:
                        grid.ScrollIntoView(item);
                        break;

                    default:
                        break;
                }
            });
    }
}
