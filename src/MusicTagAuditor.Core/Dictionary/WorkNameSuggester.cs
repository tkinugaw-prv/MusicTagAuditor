using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 作品名（<c>canonical</c>）の候補 1 件。
/// </summary>
/// <param name="Value">候補の文字列。</param>
/// <param name="Source">どこから作った候補か。**画面に必ず出す。**</param>
public sealed record WorkNameCandidate(string Value, string Source);

/// <summary>
/// アルバム単位の手がかりから作品名の候補を作る（docs/SPEC.md 7.3.2）。
///
/// **候補であって既定値ではない。** 入力欄は空のまま出し、押したときだけ入る。現在の <c>album</c> の
/// 値は誤っていることがあり（docs/TAGGING_POLICY.md 3.5 補足2）、機械が正規形として採用してはならない。
/// 出所を必ず添えるのは、<c>シューベルト 9</c> のフォルダに <c>Schubert Symphony No.8</c> という
/// <c>album</c> が付いた単位が実在するため。出所が分かれば、この食い違いは候補の並びから見える。
///
/// 候補は 3 つの作り方を混ぜる。
/// 1. 手がかりから**作曲家名を外して書式を整えた**もの（<c>Nielsen Symphony No.4</c> → <c>Symphony No. 4</c>）
/// 2. **同じ作曲家に既にある作品名**。別名だけが足りていない場合はこれを選ぶのが正しい
/// 3. **辞書にある作品名の書式に番号を差し替えた**もの（<c>Symphony No. 8</c> + 手がかりの 4 → <c>Symphony No. 4</c>）
///
/// 3 が効くのは、作品名の語彙が作曲家をまたいで繰り返すため（<c>Symphony No. N</c> が大半を占める）。
/// **人が書いた正規形だけを素材にする**ので、3.5 規則8 の書式から外れた候補が混ざらない。
/// </summary>
public static class WorkNameSuggester
{
    /// <summary>同じ作曲家に既にある作品名から作った候補の出所。</summary>
    public const string SOURCE_SAME_COMPOSER = "この作曲家の作品";

    /// <summary>辞書の作品名の書式から作った候補の出所。</summary>
    public const string SOURCE_TEMPLATE = "辞書の書式";

    /// <summary>候補の上限。多すぎると選ぶより読む手間が勝つ。</summary>
    private const int MAX_CANDIDATES = 8;

    /// <summary>書式から作る候補の上限。ジャンル違いの候補が並んでも選ぶことはない。</summary>
    private const int MAX_TEMPLATE_CANDIDATES = 3;

    /// <summary>
    /// 作品の番号。<c>Symphony No. 4</c> の <c>4</c>。
    ///
    /// **3 桁までしか拾わない。** フォルダ名には録音年が入っていることが多く
    /// （<c>チャイコフスキー 6 - ムラヴィンスキー 1982</c>）、年を番号として扱うと
    /// <c>Symphony No. 1982</c> という候補ができる。作品番号は 3 桁で足りる（ハイドンの 104 番）。
    /// </summary>
    private static readonly Regex NUMBER = new(@"(?<!\d)\d{1,3}(?!\d)", RegexOptions.Compiled);

    /// <summary><c>No.4</c> のように詰まった番号。3.5 規則8 の書式は <c>No. 4</c>。</summary>
    private static readonly Regex TIGHT_NUMBER = new(@"(?<=\p{L})\.\s*(?=\d)", RegexOptions.Compiled);

    /// <summary>日本語の文字。作品名には使わない（3.5 規則8）。</summary>
    private static readonly Regex JAPANESE = new(
        @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]",
        RegexOptions.Compiled);

    /// <summary>前後に付いていても意味を持たない記号。</summary>
    private static readonly char[] TRIM_CHARS = [' ', '\t', ':', ';', ',', '.', '-', '_', '/', '"', '\'', '(', ')', '[', ']'];

    /// <summary>
    /// 作品名の候補を作る。
    /// </summary>
    /// <param name="dictionary">現在の辞書。既にある作品名を素材にする。</param>
    /// <param name="index">現在の索引。作曲家名の判定に使う。</param>
    /// <param name="composer">この単位の作曲家の正規形。</param>
    /// <param name="hints">手がかり（<c>album</c> の値・フォルダ名）と、その出所。</param>
    /// <returns>候補。重複は落とす。出所付き。</returns>
    public static IReadOnlyList<WorkNameCandidate> Suggest(
        TagDictionary dictionary,
        DictionaryIndex index,
        string composer,
        IEnumerable<WorkNameCandidate> hints)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(hints);

        WorkNameCandidate[] materials = [.. hints];

        List<WorkNameCandidate> candidates = [];

        // 1. 手がかりから作曲家名を外す。
        //
        // **日本語を含む手がかりからは作らない。** 作品名は英語か原語で書く（3.5 規則8）ので、
        // 日本語の手がかりからは作れない。残るラテン文字は演奏者であることが多く
        // （`ブルックナー 8 - Wand` の `Wand`）、そのまま出すと作品名に見える候補になってしまう。
        // 番号（`ブルックナー 8` の 8）は下の書式で使うので、ここで捨てても失わない。
        foreach (WorkNameCandidate hint in materials.Where(hint => !JAPANESE.IsMatch(hint.Value)))
        {
            string cleaned = StripComposer(hint.Value, index);

            if (IsUsable(cleaned, index))
            {
                candidates.Add(new WorkNameCandidate(cleaned, hint.Source));
            }
        }

        string[] numbers = [.. materials.SelectMany(hint => NUMBER.Matches(hint.Value).Select(match => match.Value)).Distinct(StringComparer.Ordinal)];

        candidates.AddRange(FromSameComposer(dictionary, composer, numbers));
        candidates.AddRange(FromTemplates(dictionary, composer, numbers, materials));

        return
        [
            .. candidates
                .DistinctBy(candidate => NormalizationKey.Create(candidate.Value), StringComparer.Ordinal)
                .Take(MAX_CANDIDATES),
        ];
    }

    /// <summary>
    /// 値に含まれる番号を取り出す。
    ///
    /// <c>album</c> とフォルダ名が別の番号を指していないかの確認に使う（docs/SPEC.md 7.4.3 手順5）。
    /// 実ライブラリには <c>シューベルト 9</c> のフォルダに <c>Schubert Symphony No.8</c> という
    /// <c>album</c> が付いた単位がある。**候補を並べるだけでは、この食い違いは見落とされる。**
    /// </summary>
    /// <param name="value">対象の値。</param>
    /// <returns>含まれる番号。順は現れた順。</returns>
    public static IReadOnlyList<string> ExtractNumbers(string? value)
    {
        return
        [
            .. NUMBER.Matches(value ?? string.Empty)
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 同じ作曲家に既にある作品名を候補にする。
    ///
    /// **既にある作品を選ぶのは重複登録ではない。** 手がかりが引けなかったのは別名が足りないからで、
    /// そのときは新しい行を作らず既存のエントリに別名が足される（<see cref="DictionaryEditor.AddWork"/>）。
    /// </summary>
    private static IEnumerable<WorkNameCandidate> FromSameComposer(
        TagDictionary dictionary,
        string composer,
        IReadOnlyList<string> numbers)
    {
        return (dictionary.Works ?? [])
            .Where(work => string.Equals(work.Composer, composer, StringComparison.Ordinal))
            .Select(work => work.Canonical)
            .Where(canonical => !string.IsNullOrWhiteSpace(canonical))
            // **手がかりに番号があるなら、その番号の作品だけを出す。** 第 6 番を探しているときに
            // 第 10 番・第 11 番…と並べても選ぶことはなく、候補欄が埋まって他が見えなくなる。
            .Where(canonical => numbers.Count == 0 || HasNumber(canonical, numbers))
            .OrderBy(canonical => canonical, StringComparer.Ordinal)
            .Select(canonical => new WorkNameCandidate(canonical, SOURCE_SAME_COMPOSER));
    }

    /// <summary>
    /// 辞書にある作品名の書式に、手がかりの番号を差し替えた候補を作る。
    ///
    /// 素材は**作曲家をまたいだ全作品**にする。第 4 番を誰も登録していなくても
    /// <c>Symphony No. 8</c> があれば <c>Symphony No. 4</c> は作れる。
    /// </summary>
    private static IEnumerable<WorkNameCandidate> FromTemplates(
        TagDictionary dictionary,
        string composer,
        IReadOnlyList<string> numbers,
        IReadOnlyList<WorkNameCandidate> hints)
    {
        if (numbers.Count == 0)
        {
            return [];
        }

        // 手がかりに出てくる語を含む書式を先に出す。`Symphony` と書いてあるのに
        // `Piano Concerto` を先頭に置くと、候補を読む手間が増えるだけになる。
        string[] hintWords =
        [
            .. hints.SelectMany(hint => Words(hint.Value))
                .Select(NormalizationKey.Create)
                .Where(word => word.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];

        var templates = (dictionary.Works ?? [])
            .Where(work => !string.IsNullOrWhiteSpace(work.Canonical) && NUMBER.IsMatch(work.Canonical))
            // 同じ作曲家の作品は FromSameComposer が出すので、書式の素材としてだけ使う。
            .Select(work => NUMBER.Replace(work.Canonical, "{0}", 1))
            .GroupBy(template => template, StringComparer.Ordinal)
            .Select(group => new
            {
                Template = group.Key,
                Count = group.Count(),
                Matches = Words(group.Key).Select(NormalizationKey.Create).Any(word => hintWords.Contains(word, StringComparer.Ordinal)),
            })
            .OrderByDescending(template => template.Matches)
            .ThenByDescending(template => template.Count)
            .ThenBy(template => template.Template, StringComparer.Ordinal);

        return templates
            .SelectMany(template => numbers.Select(number => template.Template.Replace("{0}", number, StringComparison.Ordinal)))
            .Select(value => new WorkNameCandidate(value, SOURCE_TEMPLATE))
            .Take(MAX_TEMPLATE_CANDIDATES);
    }

    /// <summary>
    /// 手がかりから作曲家名を外し、書式を整える。
    ///
    /// <c>Bruckner:Sym.No.3</c> のように区切りなしで続く書き方があるので、<c>:</c> でも分けて見る。
    /// </summary>
    /// <param name="hint">手がかり。</param>
    /// <param name="index">現在の索引。</param>
    /// <returns>整えた文字列。</returns>
    public static string StripComposer(string? hint, DictionaryIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        IEnumerable<string> kept = Words(hint ?? string.Empty)
            .SelectMany(word => word.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(part => !IsComposerName(part, index));

        string joined = string.Join(' ', kept).Trim(TRIM_CHARS);

        // `No.4` を `No. 4` にする（3.5 規則8）。詰まった書き方は album 側の癖で、正規形ではない。
        return TIGHT_NUMBER.Replace(joined, ". ");
    }

    /// <summary>
    /// 語が作曲家名かを判定する。前後の記号は落としてから見る。
    /// </summary>
    private static bool IsComposerName(string word, DictionaryIndex index)
    {
        string bare = word.Trim(TRIM_CHARS);

        return bare.Length > 0 && index.ContainsComposerName(bare, out _);
    }

    /// <summary>
    /// 候補として出してよい値かを判定する。
    ///
    /// **日本語を含む値は落とす。** 作品名はジャンル名が英語・固有の題名が原語で、
    /// 原語が非ラテン文字なら英語圏での一般的な題名を使う（3.5 規則8）。日本語の手がかりからは作れない。
    /// </summary>
    private static bool IsUsable(string value, DictionaryIndex index)
    {
        if (value.Length < 2 || JAPANESE.IsMatch(value))
        {
            return false;
        }

        // 番号だけ・記号だけの残骸は作品名にならない。
        if (!value.Any(char.IsLetter))
        {
            return false;
        }

        return !index.ContainsComposerName(value, out _);
    }

    /// <summary>
    /// 値が手がかりの番号を含むか。
    /// </summary>
    private static bool HasNumber(string value, IReadOnlyList<string> numbers)
    {
        return NUMBER.Matches(value).Any(match => numbers.Contains(match.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// 空白で語に切る。
    /// </summary>
    private static IEnumerable<string> Words(string value)
    {
        return value.Split(
            [' ', '\t', '　'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
