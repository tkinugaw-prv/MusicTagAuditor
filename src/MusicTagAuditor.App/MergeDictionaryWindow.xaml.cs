using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App;

/// <summary>
/// 同梱の既定辞書から差分を取り込むダイアログ。
///
/// **自動では取り込まない。** 段階 5 で辞書からエントリを削除できるようになったため、
/// 黙って差分を当てると利用者が意図的に消したものを復活させてしまう。
/// </summary>
public partial class MergeDictionaryWindow : Window
{
    /// <summary>
    /// ダイアログを初期化する。
    /// </summary>
    /// <param name="items">取り込める差分。</param>
    public MergeDictionaryWindow(IReadOnlyList<DictionaryMergeItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        InitializeComponent();

        Items = [.. items];

        Header = string.Create(
            CultureInfo.CurrentCulture,
            $"既定辞書に {items.Count:N0} 件の更新があります。");

        DataContext = this;
    }

    /// <summary>取り込める差分。</summary>
    public ObservableCollection<DictionaryMergeItem> Items { get; }

    /// <summary>見出しに出す説明。</summary>
    public string Header { get; }

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
    /// すべて取り込む対象にする。
    /// </summary>
    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        SetAll(true);
    }

    /// <summary>
    /// すべて対象から外す。
    /// </summary>
    private void OnSelectNone(object sender, RoutedEventArgs e)
    {
        SetAll(false);
    }

    /// <summary>
    /// チェック状態をまとめて切り替える。
    /// </summary>
    private void SetAll(bool value)
    {
        DictionaryMergeItem[] snapshot = [.. Items];

        foreach (DictionaryMergeItem item in snapshot)
        {
            item.IsSelected = value;
        }

        // DictionaryMergeItem は変更を通知しないため、一覧を作り直して反映する。
        Items.Clear();

        foreach (DictionaryMergeItem item in snapshot)
        {
            Items.Add(item);
        }
    }

    /// <summary>
    /// 取り込む対象があることを確かめてから閉じる。
    /// </summary>
    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!Items.Any(item => item.IsSelected))
        {
            MessageBox.Show(
                "取り込む項目が 1 件も選ばれていません。",
                "取り込めません",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

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
