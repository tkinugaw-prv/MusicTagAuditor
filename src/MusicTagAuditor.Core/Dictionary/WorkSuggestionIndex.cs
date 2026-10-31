namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 曲名の入力欄に出す作品名の候補を、作曲家ごとに引く索引（docs/SPEC.md 5.2）。
///
/// **作曲家を問わず出さない。** <c>Symphony No. 5</c> は作曲家が違えば別の作品なので、1 つに
/// まとめると片方の別名で探せなくなり、他の作曲家の作品で候補の上限も埋まる。
/// 辞書の作品エントリが作曲家をキーの一部にしているのと同じ理由である。
///
/// <see cref="DictionaryCategory"/> には足さない。あの種別は「値をどの表へ入れるか」を表しており、
/// 作品だけは作曲家が決まらないと候補を作れない。
/// </summary>
public sealed class WorkSuggestionIndex
{
    /// <summary>候補の無い索引。辞書を読み込む前の初期値に使う。</summary>
    public static readonly WorkSuggestionIndex EMPTY = new(new TagDictionary(), new DictionaryIndex(new TagDictionary()));

    /// <summary>作曲家の正規形 → その作曲家の作品の候補。</summary>
    private readonly Dictionary<string, IReadOnlyList<SuggestionEntry>> _byComposer;

    /// <summary>辞書引きの索引。タグの作曲家を正規形へ寄せるために持つ。</summary>
    private readonly DictionaryIndex _index;

    /// <summary>
    /// 辞書から索引を作る。
    /// </summary>
    /// <param name="dictionary">対象の辞書。</param>
    /// <param name="index"><paramref name="dictionary"/> の索引。</param>
    public WorkSuggestionIndex(TagDictionary dictionary, DictionaryIndex index)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(index);

        _index = index;

        _byComposer = (dictionary.Works ?? [])
            .GroupBy(work => index.TryResolveComposer(work.Composer, out string canonical) ? canonical : work.Composer, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                IReadOnlyList<SuggestionEntry> (group) =>
                [
                    .. group
                        .Select(work => DictionarySuggester.Build(work.Canonical, work.Aliases, work.AliasesJa))
                        .Where(entry => entry.Canonical.Length > 0)
                        .DistinctBy(entry => entry.Canonical, StringComparer.Ordinal)
                        .OrderBy(entry => entry.Canonical, StringComparer.Ordinal),
                ],
                StringComparer.Ordinal);
    }

    /// <summary>
    /// 対象の行すべてに共通する作曲家の作品を返す。
    ///
    /// **作曲家が空・辞書に無い・複数の行が 1 つでもあれば候補を出さない。** 空の行を無視して
    /// 残りの作曲家で出すと、作曲家の分からない行へ別の行の作曲家の作品名を入れてしまう。
    /// </summary>
    /// <param name="rows">行ごとの <c>composer</c> の値。複数値は分けて渡す。</param>
    /// <returns>候補。作曲家が 1 人に定まらなければ空。</returns>
    public IReadOnlyList<SuggestionEntry> For(IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        string? composer = null;

        foreach (IReadOnlyList<string> values in rows)
        {
            string[] resolved =
            [
                .. values
                    .Select(value => _index.TryResolveComposer(value, out string canonical) ? canonical : null)
                    .Distinct(StringComparer.Ordinal)
                    .Cast<string>(),
            ];

            if (resolved.Length != 1 || resolved[0] is null || (composer is not null && composer != resolved[0]))
            {
                return [];
            }

            composer = resolved[0];
        }

        return composer is not null && _byComposer.TryGetValue(composer, out IReadOnlyList<SuggestionEntry>? entries)
            ? entries
            : [];
    }
}
