using System.Globalization;
using System.Text;

namespace MusicTagger.Core.Normalization;

/// <summary>
/// 辞書引きに使う正規化キーを作る。
///
/// 表記の揺れのうち「意味を変えないもの」をここで吸収する。これにより
/// 「カール・ベーム」と「カールベーム」、「Bohm」と「Böhm」を辞書に別々に登録せずに済む。
/// 隣接する music-library-search-app のインデックス生成と同じ考え方。
/// </summary>
public static class NormalizationKey
{
    /// <summary>ひらがなとカタカナのコードポイントの差。</summary>
    private const int HIRAGANA_TO_KATAKANA_OFFSET = 0x30A1 - 0x3041;

    /// <summary>ひらがなの範囲の開始。</summary>
    private const char HIRAGANA_START = 'ぁ';

    /// <summary>ひらがなの範囲の終了。</summary>
    private const char HIRAGANA_END = 'ゖ';

    /// <summary>
    /// 正規化キーを作る。
    ///
    /// 手順は NFKC → 小文字化 → ひらがな→カタカナ → ダイアクリティカルマーク除去 → 記号と空白の除去。
    /// </summary>
    /// <param name="value">元の文字列。</param>
    /// <returns>正規化キー。null や空白のみの場合は空文字。</returns>
    public static string Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // NFKC で全角英数などを畳んでから小文字化する。
        string normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

        StringBuilder builder = new(normalized.Length);

        // ダイアクリティカルマークを分離するため NFD にしてから、結合文字を落とす。
        foreach (char c in normalized.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= HIRAGANA_START and <= HIRAGANA_END)
            {
                builder.Append((char)(c + HIRAGANA_TO_KATAKANA_OFFSET));
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }

            // 記号・空白は落とす。中黒やピリオドの有無を辞書に登録せずに済ませるため。
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// 2 つの値が正規化キーとして等しいかを判定する。
    /// </summary>
    /// <param name="left">比較する値。</param>
    /// <param name="right">比較する値。</param>
    /// <returns>等しければ true。</returns>
    public static bool AreEquivalent(string? left, string? right)
    {
        string leftKey = Create(left);

        return leftKey.Length > 0 && leftKey == Create(right);
    }
}
