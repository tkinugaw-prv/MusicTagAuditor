using System.Collections.Frozen;

namespace MusicTagAuditor.Core.Models;

/// <summary>
/// <see cref="AudioFormat"/> の表示に使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class AudioFormatConst
{
    /// <summary>
    /// 形式ごとの拡張子。
    ///
    /// 利用者に形式を示すときは enum 名（<c>Id3</c> 等）ではなくこちらを使う。
    /// タグの格納形式の名前は実装の語彙であり、利用者が手元で見分けられるのは拡張子である。
    /// </summary>
    private static readonly FrozenDictionary<AudioFormat, string[]> EXTENSIONS_BY_FORMAT =
        new Dictionary<AudioFormat, string[]>
        {
            [AudioFormat.M4a] = [".m4a"],
            [AudioFormat.Flac] = [".flac"],
            [AudioFormat.Id3] = [".mp3", ".aif", ".aiff"],
        }.ToFrozenDictionary();

    /// <summary>
    /// 形式に対応する拡張子を返す。
    /// </summary>
    /// <param name="format">タグの格納形式。</param>
    /// <returns>拡張子。未知の形式なら空。</returns>
    public static IReadOnlyList<string> Extensions(AudioFormat format)
    {
        return EXTENSIONS_BY_FORMAT.TryGetValue(format, out string[]? extensions) ? extensions : [];
    }

    /// <summary>
    /// 形式を利用者向けの表示名にする。
    /// </summary>
    /// <param name="format">タグの格納形式。</param>
    /// <returns>拡張子を並べた文字列（例: <c>.mp3 / .aif / .aiff</c>）。</returns>
    public static string Label(AudioFormat format)
    {
        IReadOnlyList<string> extensions = Extensions(format);

        return extensions.Count == 0 ? format.ToString() : string.Join(" / ", extensions);
    }
}
