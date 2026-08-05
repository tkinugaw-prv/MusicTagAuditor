using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// 辞書の候補を絞り込みながら出す入力欄。
///
/// <see cref="TextBox"/> を継承しているので、テーマの入力欄テンプレート・
/// <see cref="Placeholder"/>・セル編集用スタイルがそのまま効く。
///
/// **候補は入力を助けるだけで、入力を縛らない。** 辞書に無い名前も自由に入れられる
/// （原則の例外や、これから辞書に足す値を先に入れることがある）。確定して入るのは
/// 常に正規形で、別名や日本語表記は探すための手掛かりにすぎない。
/// </summary>
public sealed class SuggestBox : TextBox
{
    /// <summary>候補一覧の高さの上限。画面を覆わない程度に留める。</summary>
    private const double POPUP_MAX_HEIGHT = 260;

    /// <summary>候補の母集合。</summary>
    public static readonly DependencyProperty SuggestionsProperty =
        DependencyProperty.Register(
            nameof(Suggestions),
            typeof(IReadOnlyList<SuggestionEntry>),
            typeof(SuggestBox),
            new PropertyMetadata(null, OnSuggestionsChanged));

    /// <summary>表示された時点で入力を受け取るか。</summary>
    public static readonly DependencyProperty FocusOnLoadProperty =
        DependencyProperty.Register(
            nameof(FocusOnLoad),
            typeof(bool),
            typeof(SuggestBox),
            new PropertyMetadata(false));

    /// <summary>候補一覧を出すポップアップ。</summary>
    private readonly Popup _popup;

    /// <summary>候補一覧。</summary>
    private readonly ListBox _list;

    /// <summary>候補の確定でテキストを書き換えている最中か。書き換えを入力と取り違えないための印。</summary>
    private bool _isAccepting;

    /// <summary>
    /// 入力欄を組み立てる。
    /// </summary>
    public SuggestBox()
    {
        _list = new ListBox
        {
            DisplayMemberPath = nameof(DictionarySuggestion.DisplayText),
            MaxHeight = POPUP_MAX_HEIGHT,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };

        _list.PreviewMouseLeftButtonDown += OnSuggestionMouseDown;

        _popup = new Popup
        {
            AllowsTransparency = true,
            Focusable = false,
            Placement = PlacementMode.Bottom,
            PlacementTarget = this,
            PopupAnimation = PopupAnimation.Fade,

            // 明示的に閉じる。false にすると候補をクリックした瞬間に閉じてしまい、選べない。
            StaysOpen = true,
            Child = new Border
            {
                Margin = new Thickness(0, 2, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Child = _list,
            },
        };

        AddLogicalChild(_popup);

        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>候補の母集合。空なら候補を出さない。</summary>
    public IReadOnlyList<SuggestionEntry>? Suggestions
    {
        get => (IReadOnlyList<SuggestionEntry>?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>
    /// 表示された時点で入力を受け取るか。
    ///
    /// <c>DataGridTemplateColumn</c> の編集要素は自動でフォーカスを受け取らないため、
    /// セル編集で使う場合に立てる。常時表示の入力欄で立てると、起動直後にそこへ
    /// フォーカスを奪われる。
    /// </summary>
    public bool FocusOnLoad
    {
        get => (bool)GetValue(FocusOnLoadProperty);
        set => SetValue(FocusOnLoadProperty, value);
    }

    /// <summary>
    /// 入力に応じて候補を出し直す。
    /// </summary>
    /// <param name="e">イベント引数。</param>
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);

        // 候補の確定で書き換えた直後に開き直すと、選んだそばから候補が出続ける。
        if (_isAccepting)
        {
            return;
        }

        // バインドによる書き換え（行の切り替えなど）で開かない。
        if (!IsKeyboardFocusWithin)
        {
            Close();
            return;
        }

        Refresh();
    }

    /// <summary>
    /// 候補一覧の操作キーを先取りする。
    ///
    /// **候補が開いているときだけ横取りする。** 閉じているときの Enter / Esc を食べると、
    /// DataGrid のセル編集の確定・取り消しが効かなくなる。
    /// </summary>
    /// <param name="e">イベント引数。</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        switch (e.Key)
        {
            case Key.Down when !_popup.IsOpen:
                Refresh();
                e.Handled = _popup.IsOpen;
                break;

            case Key.Down when _popup.IsOpen:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up when _popup.IsOpen:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter when _popup.IsOpen:
                Accept();
                e.Handled = true;
                break;

            // Tab は確定したうえで通す。次のセルへ移る動きはそのまま残す。
            case Key.Tab when _popup.IsOpen:
                Accept();
                break;

            case Key.Escape when _popup.IsOpen:
                Close();
                e.Handled = true;
                break;

            default:
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// 入力欄から離れたら候補を閉じる。
    /// </summary>
    /// <param name="e">イベント引数。</param>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Close();
    }

    /// <summary>
    /// 候補の母集合が変わったら、出したままの候補を閉じる。
    /// 一括入力で対象フィールドを切り替えたときに、前のフィールドの候補が残らないようにする。
    /// </summary>
    private static void OnSuggestionsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        (sender as SuggestBox)?.Close();
    }

    /// <summary>
    /// 表示時の見た目を整え、必要なら入力を受け取る。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_popup.Child is Border border)
        {
            border.Background = TryFindResource("PanelBarBrush") as Brush ?? border.Background;
            border.BorderBrush = TryFindResource("BorderStrongBrush") as Brush ?? border.BorderBrush;
        }

        if (FocusOnLoad)
        {
            _ = Focus();
            SelectAll();
        }
    }

    /// <summary>
    /// 隠れたら候補を閉じる。**閉じ忘れると、一覧をスクロールしたときに候補だけが宙に残る。**
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            Close();
        }
    }

    /// <summary>
    /// 候補のクリックで確定する。
    ///
    /// Preview で処理して握り潰す。ListBox に処理させるとフォーカスが移り、
    /// 入力欄から離れた扱いになって候補が閉じてしまう。
    /// </summary>
    private void OnSuggestionMouseDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? current = e.OriginalSource as DependencyObject;

        while (current is not null and not ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is not ListBoxItem item)
        {
            return;
        }

        _list.SelectedItem = item.DataContext;
        Accept();
        e.Handled = true;
    }

    /// <summary>
    /// 現在の入力で候補を絞り込み、出すかどうかを決める。
    /// </summary>
    private void Refresh()
    {
        IReadOnlyList<SuggestionEntry> candidates = Suggestions ?? [];

        if (candidates.Count == 0)
        {
            Close();
            return;
        }

        IReadOnlyList<DictionarySuggestion> suggestions = DictionarySuggester.Filter(candidates, Text);

        // 打ち終わった値に候補を重ねない。1 件だけ残り、それが入力そのものなら用は済んでいる。
        if (suggestions.Count == 0
            || (suggestions.Count == 1 && string.Equals(suggestions[0].Canonical, Text, StringComparison.Ordinal)))
        {
            Close();
            return;
        }

        _list.ItemsSource = suggestions;
        _list.SelectedIndex = 0;

        _popup.MinWidth = ActualWidth;
        _popup.IsOpen = true;
    }

    /// <summary>
    /// 候補の選択を上下に動かす。端で止める。回り込むと、どこを見ているか分からなくなる。
    /// </summary>
    /// <param name="offset">動かす向きと幅。</param>
    private void Move(int offset)
    {
        int count = _list.Items.Count;

        if (count == 0)
        {
            return;
        }

        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + offset, 0, count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    /// <summary>
    /// 選択中の候補を確定する。**入るのは正規形。**
    /// </summary>
    private void Accept()
    {
        if (_list.SelectedItem is not DictionarySuggestion suggestion)
        {
            Close();
            return;
        }

        _isAccepting = true;

        try
        {
            Text = suggestion.Canonical;
            CaretIndex = Text.Length;
        }
        finally
        {
            _isAccepting = false;
        }

        Close();
    }

    /// <summary>
    /// 候補を閉じる。
    /// </summary>
    private void Close()
    {
        _popup.IsOpen = false;
    }
}
