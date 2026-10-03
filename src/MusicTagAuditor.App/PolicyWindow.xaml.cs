using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MusicTagAuditor.App.Interop;
using MusicTagAuditor.App.Policy;

namespace MusicTagAuditor.App;

/// <summary>
/// タグ付け原則（docs/TAGGING_POLICY.md）を読むウィンドウ（docs/SPEC.md 5.5）。
///
/// **1 つしか開かない。** 何度ボタンを押しても同じウィンドウを前に出す。
/// 原則は 1 つしか無いので、複数開いても読み比べる相手が無く、閉じ忘れが溜まるだけになる。
/// </summary>
public partial class PolicyWindow : Window, INotifyPropertyChanged
{
    /// <summary>開いている原則ウィンドウ。閉じたら null に戻す。</summary>
    private static PolicyWindow? _shared;

    /// <summary>一致の塗りの不透明度。下の文字が読める濃さにする。</summary>
    private const double MATCH_FILL_OPACITY = 0.45;

    /// <summary>組み立てた原則文書。</summary>
    private readonly PolicyRendering _rendering;

    /// <summary>現在の検索の一致範囲（開いている部分だけ）。</summary>
    private IReadOnlyList<TextRange> _matches = [];

    /// <summary>一致を塗る層。表示後に作る。</summary>
    private PolicyMatchAdorner? _matchAdorner;

    /// <summary>選択中の一致の位置。未選択なら -1。</summary>
    private int _matchIndex = -1;

    /// <summary>検索結果の表示。</summary>
    private string _searchStatus = string.Empty;

    /// <summary>折りたたみ中の章にだけある一致の案内。</summary>
    private string _foldedHitNotice = string.Empty;

    /// <summary>
    /// ウィンドウを初期化する。
    /// </summary>
    /// <param name="document">原則文書。</param>
    public PolicyWindow(PolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        InitializeComponent();

        _rendering = new PolicyFlowDocumentBuilder(Application.Current?.Resources).Build(document);
        Outline = PolicyOutlineItem.CreateOutline(document);

        foreach (PolicyChapterFold fold in _rendering.Folds)
        {
            // 開閉で文書の中身が変わると、検索の一致範囲が指す位置も変わる。
            fold.ExpandedChanged += (_, _) => RunSearch();
        }

        Viewer.Document = _rendering.Document;
        DataContext = this;
        Loaded += OnLoaded;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>目次。</summary>
    public IReadOnlyList<PolicyOutlineItem> Outline { get; }

    /// <summary>検索結果の表示（例: <c>3 / 12 件</c>）。</summary>
    public string SearchStatus
    {
        get => _searchStatus;
        private set => SetField(ref _searchStatus, value);
    }

    /// <summary>折りたたみ中の章にだけある一致の案内。</summary>
    public string FoldedHitNotice
    {
        get => _foldedHitNotice;
        private set
        {
            if (SetField(ref _foldedHitNotice, value))
            {
                OnPropertyChanged(nameof(HasFoldedHitNotice));
            }
        }
    }

    /// <summary>案内を出すか。</summary>
    public bool HasFoldedHitNotice => FoldedHitNotice.Length > 0;

    /// <summary>
    /// 原則ウィンドウを開く。開いていれば前に出す。
    /// </summary>
    /// <param name="mainWindow">メインウィンドウ。閉じたら原則ウィンドウも閉じる。</param>
    /// <param name="sectionNumber">開く節の番号（例: <c>3.5</c>）。null なら位置を変えない。</param>
    /// <returns>原則ウィンドウ。</returns>
    public static PolicyWindow ShowShared(Window mainWindow, string? sectionNumber = null)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        if (_shared is null)
        {
            // Owner は付けない。付けると常にメイン画面の手前に出て、並べて読む邪魔になる。
            // 代わりにメイン画面が閉じたら閉じる。残すとアプリが終了しなくなる。
            PolicyWindow window = new(PolicyDocument.LoadEmbedded());
            EventHandler closeWithMain = (_, _) => window.Close();
            mainWindow.Closed += closeWithMain;
            window.Closed += (_, _) =>
            {
                mainWindow.Closed -= closeWithMain;
                _shared = null;
            };

            _shared = window;
            window.Show();
        }
        else
        {
            if (_shared.WindowState == WindowState.Minimized)
            {
                _shared.WindowState = WindowState.Normal;
            }

            _shared.Activate();
        }

        if (sectionNumber is not null)
        {
            _shared.OpenSection(sectionNumber);
        }

        return _shared;
    }

    /// <summary>
    /// 節を開いて先頭を表示する。
    /// </summary>
    /// <param name="sectionNumber">節番号（例: <c>3.5</c>）。</param>
    /// <returns>節が見つかったか。</returns>
    public bool OpenSection(string sectionNumber)
    {
        ArgumentNullException.ThrowIfNull(sectionNumber);

        PolicyOutlineItem? item = Outline.FirstOrDefault(
            candidate => string.Equals(candidate.Heading.Number, sectionNumber, StringComparison.Ordinal));

        if (item is null)
        {
            return false;
        }

        // 目次の選択を変えると OnOutlineSelectionChanged が移動を行う。
        // 同じ項目が選ばれていると選択変更が起きないので、そのときは直接移動する。
        if (ReferenceEquals(OutlineList.SelectedItem, item))
        {
            NavigateTo(item.Heading);
        }
        else
        {
            OutlineList.SelectedItem = item;
        }

        OutlineList.ScrollIntoView(item);

        return true;
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
    /// 見出しへ移動し、画面の先頭に出す。折りたたみ中の章なら先に開く。
    /// </summary>
    private void NavigateTo(PolicyHeading heading)
    {
        if (!_rendering.HeadingParagraphs.TryGetValue(heading, out Paragraph? paragraph))
        {
            return;
        }

        PolicyChapterFold? fold = _rendering.Folds.FirstOrDefault(
            candidate => candidate.Heading.ChapterNumber == heading.ChapterNumber);

        if (heading.Level > 2 && fold is not null)
        {
            fold.Expand();
        }

        // BringIntoView は見える位置まで最小限しか動かさないため、下から近づくと見出しが
        // 画面の最下行に来てしまう。一度末尾まで送ってから戻すと、見出しが先頭に揃う。
        Viewer.UpdateLayout();
        GetScrollViewer()?.ScrollToEnd();
        Viewer.UpdateLayout();
        paragraph.BringIntoView();
    }

    /// <summary>
    /// 一致を塗る層を本文に重ねる。テンプレートが適用されるまで本文の描画面が無いため、表示後に行う。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScrollViewer? scrollViewer = GetScrollViewer();

        if (scrollViewer?.Content is not UIElement renderScope
            || AdornerLayer.GetAdornerLayer(renderScope) is not AdornerLayer layer)
        {
            return;
        }

        Brush fill = (TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue).Clone();
        fill.Opacity = MATCH_FILL_OPACITY;
        fill.Freeze();

        _matchAdorner = new PolicyMatchAdorner(renderScope, fill);
        layer.Add(_matchAdorner);

        // 文字の位置はスクロールと折り返し幅で変わる。どちらも ScrollChanged で拾える。
        scrollViewer.ScrollChanged += (_, _) => _matchAdorner.InvalidateVisual();
    }

    /// <summary>
    /// 本文の ScrollViewer を取り出す。
    /// </summary>
    private ScrollViewer? GetScrollViewer()
    {
        return Viewer.Template?.FindName("PART_ContentHost", Viewer) as ScrollViewer;
    }

    /// <summary>
    /// 目次で選んだ見出しへ移動する。
    /// </summary>
    private void OnOutlineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is PolicyOutlineItem item)
        {
            NavigateTo(item.Heading);
        }
    }

    /// <summary>
    /// Ctrl+F で検索欄へ移る。
    /// </summary>
    private void OnFindCommand(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// 入力が変わるたびに探し直す。
    /// </summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RunSearch();
    }

    /// <summary>
    /// Enter で次へ、Shift+Enter で前へ移る。
    /// </summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        MoveMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        e.Handled = true;
    }

    /// <summary>
    /// 次の一致へ移る。
    /// </summary>
    private void OnFindNext(object sender, RoutedEventArgs e)
    {
        MoveMatch(1);
    }

    /// <summary>
    /// 前の一致へ移る。
    /// </summary>
    private void OnFindPrevious(object sender, RoutedEventArgs e)
    {
        MoveMatch(-1);
    }

    /// <summary>
    /// 一致のある折りたたみ中の章を開いて探し直す。
    /// </summary>
    private void OnExpandFoldsForSearch(object sender, RoutedEventArgs e)
    {
        foreach (PolicyChapterFold fold in _rendering.Folds.Where(fold => !fold.IsExpanded))
        {
            if (PolicyTextSearch.Count(fold.Body.Blocks, SearchBox.Text) > 0)
            {
                fold.Expand();
            }
        }

        RunSearch();
    }

    /// <summary>
    /// 検索欄の文字列で探し直す。塗りは消し、件数だけを出す。
    /// </summary>
    private void RunSearch()
    {
        string query = SearchBox.Text;

        _matches = PolicyTextSearch.FindAll(_rendering.Document.Blocks, query);
        _matchIndex = -1;
        _matchAdorner?.Show(null);

        int foldedCount = _rendering.Folds
            .Where(fold => !fold.IsExpanded)
            .Sum(fold => PolicyTextSearch.Count(fold.Body.Blocks, query));

        // 折りたたみ中の章の一致は件数に含めない。含めると「次へ」で辿れない一致が数に入る。
        FoldedHitNotice = foldedCount == 0
            ? string.Empty
            : string.Create(
                CultureInfo.CurrentCulture,
                $"折りたたみ中の章にも {foldedCount:N0} 件あります。上の件数には含めていません。");

        UpdateSearchStatus(query);
    }

    /// <summary>
    /// 一致を前後に移り、塗って画面に出す。端では反対側へ回り込む。
    /// </summary>
    /// <param name="step">進む向き（1 または -1）。</param>
    private void MoveMatch(int step)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _matchIndex = _matchIndex < 0
            ? (step > 0 ? 0 : _matches.Count - 1)
            : (_matchIndex + step + _matches.Count) % _matches.Count;

        TextRange match = _matches[_matchIndex];
        match.Start.Paragraph?.BringIntoView();
        Viewer.UpdateLayout();
        _matchAdorner?.Show(match);

        UpdateSearchStatus(SearchBox.Text);
    }

    /// <summary>
    /// 検索結果の表示を更新する。
    /// </summary>
    private void UpdateSearchStatus(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchStatus = string.Empty;
            return;
        }

        SearchStatus = _matchIndex < 0
            ? string.Create(CultureInfo.CurrentCulture, $"{_matches.Count:N0} 件")
            : string.Create(CultureInfo.CurrentCulture, $"{_matchIndex + 1:N0} / {_matches.Count:N0} 件");
    }

    /// <summary>
    /// 値を更新して変更を通知する。
    /// </summary>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    /// <summary>
    /// 変更を通知する。
    /// </summary>
    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
