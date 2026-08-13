using System.Collections;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 数字の並びを数として見る比較（自然順）。
///
/// 素の文字列比較だと <c>Symphony No. 10</c> が <c>Symphony No. 4</c> より前に来る。
/// **作品名は番号で呼ぶものが大半**なので、辞書の一覧を目で追う用途では実質使えない。
/// エクスプローラーのファイル名の並びと同じ考え方。
///
/// 文字の部分は現在のカルチャで比べる。序数比較にするとアクセント付きの名前
/// （<c>Günter</c>）が末尾に飛び、重複を見つけるという目的に合わない。
/// </summary>
public static class NaturalOrder
{
    /// <summary>
    /// 2 つの文字列を自然順で比べる。
    ///
    /// 数字と数字以外の区切りごとに前から見ていき、両方が数字なら数として、
    /// そうでなければ文字列として比べる。
    ///
    /// **数字として扱うのは ASCII の数字だけ。** 全角数字や他の字体の数字まで
    /// 拾うと、`５` と `5` のどちらが大きいかを桁の比較で決めることになり、
    /// 見た目から結果を予想できなくなる。それらは文字として比べる。
    /// </summary>
    /// <param name="left">比較する文字列。</param>
    /// <param name="right">比較する文字列。</param>
    /// <returns>左が小さければ負、等しければ 0、大きければ正。</returns>
    public static int Compare(string? left, string? right)
    {
        string a = left ?? string.Empty;
        string b = right ?? string.Empty;

        int i = 0;
        int j = 0;

        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int digitsA = i;
                int digitsB = j;

                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                int byNumber = CompareNumbers(a.AsSpan(digitsA, i - digitsA), b.AsSpan(digitsB, j - digitsB));

                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            // 次に数字が現れるまでをひとまとまりの文字列として比べる。
            // 片方だけが数字で始まっていれば、そちら側は空文字列になって先に並ぶ。
            int endA = i;
            int endB = j;

            while (endA < a.Length && !char.IsAsciiDigit(a[endA]))
            {
                endA++;
            }

            while (endB < b.Length && !char.IsAsciiDigit(b[endB]))
            {
                endB++;
            }

            int byText = string.Compare(a[i..endA], b[j..endB], StringComparison.CurrentCulture);

            if (byText != 0)
            {
                return byText;
            }

            i = endA;
            j = endB;
        }

        // ここまで同じなら、残りが短いほうが先。
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>
    /// 数字の並びを数として比べる。
    ///
    /// 数値に変換しない。**桁数に上限を設けたくない**ため（トラック番号のつもりでも、
    /// 作品名や別名には年号や作品番号が並ぶ）。先頭の 0 を落として桁数で比べ、
    /// 同じ桁数なら 1 文字ずつ見れば大小は決まる。
    /// </summary>
    private static int CompareNumbers(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        ReadOnlySpan<char> a = left.TrimStart('0');
        ReadOnlySpan<char> b = right.TrimStart('0');

        if (a.Length != b.Length)
        {
            return a.Length - b.Length;
        }

        int byDigits = a.SequenceCompareTo(b);

        if (byDigits != 0)
        {
            return byDigits;
        }

        // 数としては同じ（"04" と "4"）。0 詰めの多いほうを後ろに置いて並びを安定させる。
        return left.Length - right.Length;
    }
}

/// <summary>
/// 辞書タブの一覧を自然順に並べる比較器。
///
/// <see cref="System.Windows.Data.ListCollectionView.CustomSort"/> に渡す。
/// <c>SortDescriptions</c> ではプロパティの既定の比較しか使えず、番号を数として見られない。
/// 状態を持たないので <see cref="Instance"/> を使い回してよい。
/// </summary>
public sealed class NaturalOrderRowComparer : IComparer
{
    /// <summary>使い回す実体。</summary>
    public static NaturalOrderRowComparer Instance { get; } = new();

    /// <summary>
    /// 2 つの行を並び順のキーで比べる。
    /// </summary>
    /// <param name="x">比較する行。</param>
    /// <param name="y">比較する行。</param>
    /// <returns>左が小さければ負、等しければ 0、大きければ正。</returns>
    public int Compare(object? x, object? y)
    {
        return NaturalOrder.Compare((x as IDictionaryRow)?.SortKey, (y as IDictionaryRow)?.SortKey);
    }
}
