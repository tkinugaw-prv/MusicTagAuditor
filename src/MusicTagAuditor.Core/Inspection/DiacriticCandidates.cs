using System.Collections.Frozen;
using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// 発音区別符号が落ちた綴りと、その正しい綴りの組 1 件。
/// </summary>
/// <param name="Ascii">ASCII に落ちた綴り。</param>
/// <param name="Correct">符号を含む正しい綴り。</param>
/// <param name="Language">言語のメモ。</param>
public sealed record DiacriticCandidate(string Ascii, string Correct, string Language);

/// <summary>
/// 曲名で発音区別符号が落ちている疑いのある語（R-304）。
///
/// **自動修正はしない。** CD 原盤が意図的に ASCII 表記である可能性があり、
/// 人名・団体名（docs/TAGGING_POLICY.md 3.3）とは事情が違う。方針は 6.3 で未確定。
///
/// 辞書 (<c>dictionary.json</c>) ではなく定数として持つのは、これが利用者ごとに
/// 育てる語彙ではなく、原則の 6.3 に紐づく固定の一覧だから。ドイツ語以外の言語を
/// 足す場合もこの表に行を追加する。
/// </summary>
public static class DiacriticCandidates
{
    /// <summary>
    /// 一覧。**docs/TAGGING_POLICY.md 6.3 が実際に挙げている語だけを載せる。**
    ///
    /// それらしいドイツ語を足すと、原盤が意図的に ASCII 表記の曲を誤検出する。
    /// 語を増やすときは実データで確認してからにすること。
    /// ドイツ語以外（フランス語のアクサン、チェコ語のハーチェク等）もこの表に行を足す。
    /// </summary>
    public static readonly IReadOnlyList<DiacriticCandidate> ALL =
    [
        new("Walkure", "Walküre", "ドイツ語"),
        new("Gotterdammerung", "Götterdämmerung", "ドイツ語"),
        new("Tannhauser", "Tannhäuser", "ドイツ語"),
        new("Freischutz", "Freischütz", "ドイツ語"),
        new("Sangerkrieg", "Sängerkrieg", "ドイツ語"),
        new("Jagervergnugen", "Jägervergnügen", "ドイツ語"),
        new("Konig", "König", "ドイツ語"),
        new("Nurnberg", "Nürnberg", "ドイツ語"),
    ];

    /// <summary>ASCII 表記 → 候補。語単位で引くために使う。</summary>
    private static readonly FrozenDictionary<string, DiacriticCandidate> BY_ASCII =
        ALL.ToFrozenDictionary(candidate => candidate.Ascii, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 値に含まれる、符号が落ちた綴りを探す。
    ///
    /// **既に正しい綴りになっている語は拾わない。** 正規化キーで引くと
    /// <c>Walküre</c> と <c>Walkure</c> が同じキーになるため、ここでは素の文字列で照合する。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <returns>見つかった候補。</returns>
    public static IReadOnlyList<DiacriticCandidate> Find(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        List<DiacriticCandidate> found = [];

        foreach (string token in value.Split(
            [' ', '\t', ',', ';', ':', '/', '(', ')', '[', ']', '&', '.', '"', '\'', '?', '!'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (BY_ASCII.TryGetValue(token, out DiacriticCandidate? candidate) && !found.Contains(candidate))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>
    /// 値の中の綴りを正しい形に置き換える。**根拠の提示にだけ使う。**
    /// </summary>
    /// <param name="value">元の値。</param>
    /// <param name="candidates">置き換える候補。</param>
    /// <returns>置き換えた結果。</returns>
    public static string Suggest(string value, IEnumerable<DiacriticCandidate> candidates)
    {
        string result = value;

        foreach (DiacriticCandidate candidate in candidates)
        {
            result = result.Replace(candidate.Ascii, candidate.Correct, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// 正規化キーが同じかを確かめる。表に誤りが無いことのテスト用。
    /// </summary>
    /// <param name="candidate">確認する候補。</param>
    /// <returns>ASCII 表記と正しい綴りが同じ語を指していれば true。</returns>
    public static bool IsSameWord(DiacriticCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return NormalizationKey.AreEquivalent(candidate.Ascii, candidate.Correct);
    }
}
