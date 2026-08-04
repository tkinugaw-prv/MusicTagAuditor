using System.Text.RegularExpressions;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// 曲名がプレースホルダかどうかを判定し、ファイル名から補完する（R-303）。
///
/// 実ライブラリでは 155 件が該当し、うち 137 件はファイル名に本来の曲名が入っている
/// （docs/library-baseline-2026-08-03.md）。残り 18 件はファイル名もプレースホルダ。
/// </summary>
public static class PlaceholderTitle
{
    /// <summary><c>Track04</c> 形式。</summary>
    private static readonly Regex TRACK_FORM = new(@"^track\s*\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>数字のみ、または <c>1-1</c> のような数字の組。</summary>
    private static readonly Regex NUMBER_FORM = new(@"^\d+([-_]\d+)?$", RegexOptions.Compiled);

    /// <summary><c>ショス15 - 01</c> のような「語 - 数字」形式。</summary>
    private static readonly Regex WORD_NUMBER_FORM = new(@"^\S+\s*-\s*\d+$", RegexOptions.Compiled);

    /// <summary>ファイル名の先頭に付くトラック番号。<c>01 </c> や <c>2-08 </c>。</summary>
    private static readonly Regex LEADING_TRACK_NUMBER = new(@"^\d+(-\d+)?[\s._-]+", RegexOptions.Compiled);

    /// <summary>
    /// 値がプレースホルダかを判定する。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <returns>プレースホルダなら true。</returns>
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();

        return TRACK_FORM.IsMatch(trimmed)
            || NUMBER_FORM.IsMatch(trimmed)
            || WORD_NUMBER_FORM.IsMatch(trimmed);
    }

    /// <summary>
    /// ファイル名から曲名を補完する。
    ///
    /// 先頭のトラック番号は落とす。**それ以外は加工しない。**
    /// ファイル名には Windows で使えない文字の代替が入っている（<c>Op.141_ I. Allegretto</c> の
    /// <c>_</c> は本来 <c>:</c> と思われる）が、元が何だったかは決められないので戻さない。
    /// </summary>
    /// <param name="relativePath">対象ファイルの相対パス。</param>
    /// <returns>補完できる曲名。できなければ null。</returns>
    public static string? SuggestFromFileName(string relativePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(relativePath);

        if (fileName.Length == 0)
        {
            return null;
        }

        string candidate = LEADING_TRACK_NUMBER.Replace(fileName, string.Empty).Trim();

        // 番号を落とした結果が空、またはこれもプレースホルダなら補完にならない。
        if (candidate.Length == 0 || IsPlaceholder(candidate))
        {
            return null;
        }

        return candidate;
    }
}
