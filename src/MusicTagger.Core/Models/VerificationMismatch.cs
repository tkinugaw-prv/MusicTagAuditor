namespace MusicTagger.Core.Models;

/// <summary>
/// 書き込んだが意図した値になっていなかった項目。
///
/// **書き込みの成功と、意図した値が入っていることは別である**
/// （docs/TAGGING_POLICY.md 7.3 / docs/SPEC.md 9章の工程6）。
/// 復元と適用の両方で使う。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="Expected">書こうとした値。</param>
/// <param name="Actual">読み戻した値。</param>
public sealed record VerificationMismatch(
    string RelativePath,
    TagField Field,
    IReadOnlyList<string> Expected,
    IReadOnlyList<string> Actual)
{
    /// <summary>表示用の説明。</summary>
    public string Summary =>
        $"{RelativePath} [{Field}] 期待「{string.Join(TrackTags.VALUE_JOIN_SEPARATOR, Expected)}」"
        + $" 実際「{string.Join(TrackTags.VALUE_JOIN_SEPARATOR, Actual)}」";
}
