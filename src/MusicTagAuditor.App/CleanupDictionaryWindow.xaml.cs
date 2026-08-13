using System.Globalization;
using System.Windows;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App;

/// <summary>
/// 索引に載らない別名を取り除く前に、何が消えるかを見せるダイアログ。
///
/// **1 件ずつ選ばせない。**「既定辞書から取り込む」（<see cref="MergeDictionaryWindow"/>）が
/// 行ごとにチェックを持つのは、利用者が意図的に削除したエントリを復活させうるからである。
/// こちらは事情が違い、消す別名はいずれも**索引が既に捨てているもの**で、残す理由が無い。
/// 意味の無い選択肢を並べると、逆に「外すべき行があるのでは」と読ませてしまう。
/// </summary>
public partial class CleanupDictionaryWindow : Window
{
    /// <summary>
    /// ダイアログを初期化する。
    /// </summary>
    /// <param name="items">取り除く別名。</param>
    public CleanupDictionaryWindow(IReadOnlyList<RemovedAlias> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        InitializeComponent();

        Items = items;

        Header = string.Create(
            CultureInfo.CurrentCulture,
            $"索引に載っていない別名が {items.Count:N0} 件あります。");

        DataContext = this;
    }

    /// <summary>取り除く別名。</summary>
    public IReadOnlyList<RemovedAlias> Items { get; }

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
    /// 掃除を実行して閉じる。
    /// </summary>
    private void OnAccept(object sender, RoutedEventArgs e)
    {
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
