using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 検索で選んだ一致を本文の上に重ねて塗る。参照から飛んだ規則には、本文の左に帯を引く。
///
/// **選択（TextSelection）では示さない。** 本文にフォーカスが無いと選択の反転は
/// スクロールに追従せず、画面の同じ位置に古い四角が残った（2026-10-04 実測）。
/// フォーカスを本文へ回せば描かれるが、検索欄から抜けるので入力中の IME の変換が切れる。
/// 文書の書式を書き換えて塗る案も、コード表記の背景色を戻す手間が要るので採らなかった。
/// </summary>
public sealed class PolicyMatchAdorner : Adorner
{
    /// <summary>同じ行とみなす上端の差（DIP）。</summary>
    private const double SAME_LINE_TOLERANCE = 1.0;

    /// <summary>帯の幅（DIP）。</summary>
    private const double BAR_WIDTH = 4.0;

    /// <summary>帯と本文の間隔（DIP）。リストの番号より左に出す。</summary>
    private const double BAR_GAP = 30.0;

    /// <summary>塗りの色。</summary>
    private readonly Brush _fill;

    /// <summary>帯の色。</summary>
    private readonly Brush _bar;

    /// <summary>塗る範囲。null なら何も描かない。</summary>
    private TextRange? _range;

    /// <summary>塗らずに左に帯を引くか。</summary>
    private bool _asBar;

    /// <summary>
    /// 本文の描画面に重ねる。
    /// </summary>
    /// <param name="renderScope">本文を描いている要素（FlowDocumentScrollViewer の内側）。</param>
    /// <param name="fill">塗りの色。</param>
    /// <param name="bar">帯の色。</param>
    public PolicyMatchAdorner(UIElement renderScope, Brush fill, Brush bar)
        : base(renderScope)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ArgumentNullException.ThrowIfNull(bar);

        _fill = fill;
        _bar = bar;
        IsHitTestVisible = false;
    }

    /// <summary>
    /// 塗る範囲を差し替える。
    /// </summary>
    /// <param name="range">一致範囲。null なら消す。</param>
    public void Show(TextRange? range)
    {
        _range = range;
        _asBar = false;
        InvalidateVisual();
    }

    /// <summary>
    /// 帯を引いていれば消す。検索の塗りは残す。
    /// </summary>
    public void HideBar()
    {
        if (_asBar)
        {
            Show(null);
        }
    }

    /// <summary>
    /// 範囲の左に帯を引く。規則の項目は数行に及ぶので、全体を塗ると本文が読みにくくなる。
    /// </summary>
    /// <param name="range">印を付ける範囲。</param>
    public void ShowBar(TextRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        _range = range;
        _asBar = true;
        InvalidateVisual();
    }

    /// <summary>
    /// 一致範囲を行ごとの矩形にして塗る。
    /// </summary>
    /// <param name="drawingContext">描画先。</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (_range is null)
        {
            return;
        }

        Rect[] lines = [.. GetLineRects(_range)];

        if (!_asBar)
        {
            foreach (Rect rect in lines)
            {
                drawingContext.DrawRectangle(_fill, null, rect);
            }

            return;
        }

        if (lines.Length == 0)
        {
            return;
        }

        double left = lines.Min(rect => rect.Left) - BAR_GAP;
        double top = lines.Min(rect => rect.Top);
        double bottom = lines.Max(rect => rect.Bottom);
        drawingContext.DrawRectangle(_bar, null, new Rect(Math.Max(0, left), top, BAR_WIDTH, bottom - top));
    }

    /// <summary>
    /// 範囲を行ごとの矩形に分ける。一致が行をまたいで折り返すこともあるため、1 文字ずつ測ってつなぐ。
    /// </summary>
    private static IEnumerable<Rect> GetLineRects(TextRange range)
    {
        Rect? line = null;
        TextPointer position = range.Start;

        while (position.CompareTo(range.End) < 0)
        {
            TextPointer next = position.GetNextInsertionPosition(LogicalDirection.Forward) ?? range.End;

            if (next.CompareTo(range.End) > 0)
            {
                next = range.End;
            }

            Rect start = position.GetCharacterRect(LogicalDirection.Forward);
            Rect end = next.GetCharacterRect(LogicalDirection.Backward);

            if (!start.IsEmpty && !end.IsEmpty && end.Left >= start.Left)
            {
                Rect character = new(start.Left, start.Top, end.Left - start.Left, start.Height);

                if (line is Rect current && Math.Abs(current.Top - character.Top) < SAME_LINE_TOLERANCE)
                {
                    line = Rect.Union(current, character);
                }
                else
                {
                    if (line is Rect finished)
                    {
                        yield return finished;
                    }

                    line = character;
                }
            }

            if (next.CompareTo(position) <= 0)
            {
                break;
            }

            position = next;
        }

        if (line is Rect last)
        {
            yield return last;
        }
    }
}
