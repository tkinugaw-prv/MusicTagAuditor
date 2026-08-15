using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// 見出しをドラッグして列幅を変えられるようにする添付ビヘイビア。
///
/// **WPF 既定のリサイズは、表の総幅を表示幅より広げない。** 掴んだ列を広げた分は
/// 右側の列から奪うだけで、奪えなければドラッグしても何も起きない。列数が多く
/// すべての列が <c>MinWidth</c> に張り付いている表（ファイル一覧は 14 列あり、
/// 通常の画面幅では常にこの状態になる）では、既定のままだと列幅を 1px も
/// 動かせない。値が見切れていても読む手段が無くなる。
///
/// ここでは掴んだ瞬間に全列の幅を実測値の px へ固定し、以後は掴んだ列だけを
/// 動かす。**総幅が表示幅を超えてよい。** あふれた分は横スクロールで送る。
/// 既定の処理を止める手段が無い（つまみに直接張られていて、こちらへ届く頃には
/// 実行済み）ため、掴んだ時点の幅を控えておき、毎回それを上書きして打ち消す。
///
/// 幅を px に固定すると「ウィンドウを広げた分が star 指定の列に回る」動きは
/// 失われる。元へ戻せるよう、最初のドラッグの直前に元の指定を控えておき、
/// <see cref="ResetWidthsCommand"/> で復元する。
/// </summary>
public static class ColumnResize
{
    /// <summary>WPF 既定の見出しテンプレートが持つ、左側のつまみの名前。</summary>
    private const string LEFT_GRIPPER_NAME = "PART_LeftHeaderGripper";

    /// <summary>WPF 既定の見出しテンプレートが持つ、右側のつまみの名前。</summary>
    private const string RIGHT_GRIPPER_NAME = "PART_RightHeaderGripper";

    /// <summary>
    /// この動作を有効にするかどうか。<see cref="DataGrid"/> に付ける。
    /// </summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(ColumnResize),
            new PropertyMetadata(false, OnEnabledChanged));

    /// <summary>ドラッグ中の状態と、元の幅指定の控え。</summary>
    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ResizeState),
            typeof(ColumnResize),
            new PropertyMetadata(null));

    /// <summary>
    /// 列幅を XAML で書いた元の指定へ戻すコマンド。
    /// 引数には <see cref="DataGrid"/> かその中の要素を渡す。
    /// </summary>
    public static ICommand ResetWidthsCommand { get; } = new ResetWidthsCommandImpl();

    /// <summary>
    /// この動作が有効かどうかを取得する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <returns>有効なら true。</returns>
    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (bool)element.GetValue(EnabledProperty);
    }

    /// <summary>
    /// この動作の有効・無効を設定する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <param name="value">有効にするなら true。</param>
    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(EnabledProperty, value);
    }

    /// <summary>
    /// つまみのドラッグを拾えるよう、DataGrid にハンドラを張る。
    /// </summary>
    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        // 既定の処理が e.Handled を立てるため、handledEventsToo を付けないと届かない。
        if (e.NewValue is true)
        {
            grid.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted), handledEventsToo: true);
            grid.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnDragDelta), handledEventsToo: true);
            grid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted), handledEventsToo: true);
        }
        else
        {
            grid.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted));
            grid.RemoveHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnDragDelta));
            grid.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted));
            grid.SetValue(StateProperty, null);
        }
    }

    /// <summary>
    /// ドラッグの開始。動かす列を決め、全列の幅を px に固定する。
    /// </summary>
    private static void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not DataGrid grid || !grid.CanUserResizeColumns)
        {
            return;
        }

        if (e.OriginalSource is not Thumb thumb)
        {
            return;
        }

        DataGridColumn? target = FindResizedColumn(grid, thumb);

        if (target is null || !target.CanUserResize)
        {
            return;
        }

        ResizeState state = GetOrCreateState(grid);

        // 元の指定（star など）は最初のドラッグの直前にしか残っていない。
        state.OriginalWidths ??= grid.Columns.ToDictionary(c => c, c => c.Width);

        state.Target = target;
        state.StartX = Mouse.GetPosition(grid).X;
        state.StartWidths = grid.Columns.ToDictionary(c => c, c => c.ActualWidth);
        state.StartWidth = target.ActualWidth;

        // star のまま残すと、こちらで px を入れても再配分で押し戻される。
        FreezeToPixels(state.StartWidths);
    }

    /// <summary>
    /// ドラッグ中。既定の処理が動かした他列を戻し、掴んだ列だけを動かす。
    /// </summary>
    private static void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        if (grid.GetValue(StateProperty) is not ResizeState { Target: not null, StartWidths: not null } state)
        {
            return;
        }

        // e.HorizontalChange はつまみ自身から見た移動量で、列が伸びるとつまみも動く。
        // 起点からの実移動量を DataGrid 座標で取り直す。
        double moved = Mouse.GetPosition(grid).X - state.StartX;
        double width = Math.Clamp(state.StartWidth + moved, state.Target.MinWidth, state.Target.MaxWidth);

        foreach ((DataGridColumn column, double startWidth) in state.StartWidths)
        {
            if (!ReferenceEquals(column, state.Target))
            {
                column.Width = new DataGridLength(startWidth);
            }
        }

        state.Target.Width = new DataGridLength(width);
    }

    /// <summary>
    /// ドラッグの終了。次のドラッグに備えて掴んでいた列を忘れる。
    /// </summary>
    private static void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is DataGrid grid && grid.GetValue(StateProperty) is ResizeState state)
        {
            state.Target = null;
            state.StartWidths = null;
        }
    }

    /// <summary>
    /// つまみから、そのドラッグで幅が変わる列を求める。
    /// 左のつまみは 1 つ手前の列を動かす（WPF 既定と同じ）。
    /// </summary>
    private static DataGridColumn? FindResizedColumn(DataGrid grid, Thumb thumb)
    {
        if (FindAncestor<DataGridColumnHeader>(thumb) is not { Column: { } column })
        {
            return null;
        }

        return thumb.Name switch
        {
            RIGHT_GRIPPER_NAME => column,
            LEFT_GRIPPER_NAME => PreviousVisibleColumn(grid, column),
            _ => null,
        };
    }

    /// <summary>
    /// 表示順で 1 つ手前にある、表示中の列を返す。
    /// </summary>
    private static DataGridColumn? PreviousVisibleColumn(DataGrid grid, DataGridColumn column)
    {
        return grid.Columns
            .Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < column.DisplayIndex)
            .OrderByDescending(c => c.DisplayIndex)
            .FirstOrDefault();
    }

    /// <summary>
    /// 幅の指定を実測値の px に置き換える。
    /// </summary>
    private static void FreezeToPixels(IReadOnlyDictionary<DataGridColumn, double> widths)
    {
        foreach ((DataGridColumn column, double width) in widths)
        {
            column.Width = new DataGridLength(width);
        }
    }

    /// <summary>
    /// DataGrid に紐づく状態を取り出す。無ければ作る。
    /// </summary>
    private static ResizeState GetOrCreateState(DataGrid grid)
    {
        if (grid.GetValue(StateProperty) is ResizeState existing)
        {
            return existing;
        }

        ResizeState created = new();
        grid.SetValue(StateProperty, created);

        return created;
    }

    /// <summary>
    /// 指定した要素から祖先をたどって、最初に見つかった <typeparamref name="T"/> を返す。
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? start)
        where T : DependencyObject
    {
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>1 つの DataGrid についてのドラッグ状態。</summary>
    private sealed class ResizeState
    {
        /// <summary>XAML で書かれた元の幅指定。最初のドラッグの直前に控える。</summary>
        public Dictionary<DataGridColumn, DataGridLength>? OriginalWidths { get; set; }

        /// <summary>いま掴んでいる列。ドラッグ中だけ入る。</summary>
        public DataGridColumn? Target { get; set; }

        /// <summary>ドラッグ開始時の各列の幅。既定の処理が動かした分を打ち消すのに使う。</summary>
        public Dictionary<DataGridColumn, double>? StartWidths { get; set; }

        /// <summary>ドラッグ開始時のマウス位置（DataGrid 座標）。</summary>
        public double StartX { get; set; }

        /// <summary>ドラッグ開始時の、掴んだ列の幅。</summary>
        public double StartWidth { get; set; }
    }

    /// <summary>
    /// 列幅を元の指定へ戻すコマンド。ドラッグで px に固定した幅を捨てる。
    /// </summary>
    private sealed class ResetWidthsCommandImpl : ICommand
    {
        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <inheritdoc />
        public bool CanExecute(object? parameter)
        {
            return FindState(parameter) is { OriginalWidths: not null };
        }

        /// <inheritdoc />
        public void Execute(object? parameter)
        {
            if (FindState(parameter) is not { OriginalWidths: { } originals } state)
            {
                return;
            }

            foreach ((DataGridColumn column, DataGridLength width) in originals)
            {
                column.Width = width;
            }

            // 戻した以上、次のドラッグでは今の指定が「元」になる。
            state.OriginalWidths = null;
        }

        /// <summary>
        /// コマンド引数（DataGrid そのもの、または見出しなど中の要素）から状態を探す。
        /// </summary>
        private static ResizeState? FindState(object? parameter)
        {
            DataGrid? grid = parameter as DataGrid ?? FindAncestor<DataGrid>(parameter as DependencyObject);

            return grid?.GetValue(StateProperty) as ResizeState;
        }
    }
}
