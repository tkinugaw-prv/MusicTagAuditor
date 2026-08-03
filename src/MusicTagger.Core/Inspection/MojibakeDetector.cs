using System.Text;
using System.Text.RegularExpressions;

namespace MusicTagger.Core.Inspection;

/// <summary>
/// 文字化けを検出する（R-403）。
///
/// 検出方法は docs/library-baseline-2026-08-03.md の追記で確定したもの。
/// 既知の文字列（<c>アルバム情報なし</c> 等）との照合では引っかからない。
/// 実際に格納されているのは <c>A[eBXgîñÈµ</c> のような、Shift-JIS のバイト列を
/// 別のコードページとして解釈した結果だからである。
///
/// 1. 値を Latin-1 相当のバイト列に戻す
/// 2. そのバイト列を Shift-JIS (CP932) として解釈する
/// 3. 解釈結果が妥当な日本語になれば文字化けと判定する
/// </summary>
public static class MojibakeDetector
{
    /// <summary>1 文字が 1 バイトに対応するエンコーディング。誤解釈を巻き戻すために使う。</summary>
    private static readonly Encoding LATIN1 = Encoding.Latin1;

    /// <summary>Shift-JIS。</summary>
    private static readonly Encoding SHIFT_JIS;

    /// <summary>日本語の文字が連続していること。1 文字だけの一致は偶然が多い。</summary>
    private static readonly Regex JAPANESE_RUN = new(
        @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]{2,}",
        RegexOptions.Compiled);

    /// <summary>
    /// Shift-JIS を使えるようにする。
    /// </summary>
    static MojibakeDetector()
    {
        // .NET Core 以降、CP932 は既定の提供元に含まれない。登録は何度呼んでも安全。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        SHIFT_JIS = Encoding.GetEncoding(932);
    }

    /// <summary>
    /// 値が文字化けかを判定し、元の日本語を復元する。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <param name="decoded">復元できた日本語。文字化けでなければ null。</param>
    /// <returns>文字化けなら true。</returns>
    public static bool TryDecode(string? value, out string? decoded)
    {
        decoded = null;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // 1 文字が 1 バイトに戻せない値は、そもそも誤解釈の結果ではない。
        // 正しく読めている日本語（「カラヤン」等）をここで弾く。
        foreach (char c in value)
        {
            if (c > 0xFF)
            {
                return false;
            }
        }

        // ASCII だけの値は誤解釈のしようがない。
        if (!value.Any(c => c > 0x7F))
        {
            return false;
        }

        string candidate = SHIFT_JIS.GetString(LATIN1.GetBytes(value));

        if (!JAPANESE_RUN.IsMatch(candidate))
        {
            return false;
        }

        decoded = candidate;
        return true;
    }
}
