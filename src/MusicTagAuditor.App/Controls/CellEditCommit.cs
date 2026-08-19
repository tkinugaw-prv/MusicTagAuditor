using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// セル編集の入力を、その場でビューモデルへ書き戻す係。
///
/// **DataGrid はセルを 1 つ確定しただけでは値を書き戻さない。** 行の編集トランザクションが
/// 閉じるまで持ち越すため、同じ行の中で別のセルへ移っただけでは入力がどこにも現れない。
/// 手編集は保留の一覧で差分を確認してから適用する作りなので（docs/SPEC.md 1章）、
/// 一覧に出てこない入力は「消えた」のと同じに見える。
///
/// <c>DataGridTemplateColumn</c>（候補付きの 4 列）はさらに悪く、行を確定しても書き戻らない。
/// <c>CommitCellEdit</c> が見るのは編集要素の <c>BindingGroup</c> だけで、ここでは設定していない。
/// 頼りは <c>UpdateSourceTrigger=LostFocus</c> だけになるが、一覧の別の行・セルをクリックして
/// 抜けると、編集テンプレートがツリーから外れるほうがフォーカスの移動より先になり、
/// <c>LostFocus</c> が上がらないまま入力が消える。
///
/// **呼ぶのは <c>CellEditEnding</c> から。** 編集要素がまだ生きている最後の機会で、
/// ここを逃すと書き戻す相手そのものが無くなる。
/// </summary>
public static class CellEditCommit
{
    /// <summary>
    /// 編集要素が持っている入力を、バインド先へ書き戻す。
    ///
    /// 対象は <see cref="TextBox"/> と、その派生である <see cref="SuggestBox"/> だけ。
    /// チェックボックスの列は <c>UpdateSourceTrigger=PropertyChanged</c> で即座に書き戻るため、
    /// ここへ来ても何もしない。
    /// </summary>
    /// <param name="editingElement">編集に使われていた要素。null なら何もしない。</param>
    public static void Flush(FrameworkElement? editingElement)
    {
        FindTextBox(editingElement)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>
    /// 編集要素そのもの、または配下から入力欄を探す。
    ///
    /// <c>DataGridTextColumn</c> は編集要素が <see cref="TextBox"/> そのもので、
    /// <c>DataGridTemplateColumn</c> はテンプレートを載せた入れ物が来る（入力欄はその下）。
    /// </summary>
    /// <param name="element">探索の起点。</param>
    /// <returns>最初に見つかった入力欄。無ければ null。</returns>
    private static TextBox? FindTextBox(DependencyObject? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element is TextBox box)
        {
            return box;
        }

        int count = VisualTreeHelper.GetChildrenCount(element);

        for (int index = 0; index < count; index++)
        {
            if (FindTextBox(VisualTreeHelper.GetChild(element, index)) is TextBox found)
            {
                return found;
            }
        }

        return null;
    }
}
