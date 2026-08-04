namespace MusicTagAuditor.Core.Models;

/// <summary>
/// タグの格納形式。拡張子ではなくタグの構造で分類する。
/// 対応表は docs/TAGGING_POLICY.md 4.1 を参照。
/// </summary>
public enum AudioFormat
{
    /// <summary>MP4 atom（.m4a）。</summary>
    M4a,

    /// <summary>Vorbis comment（.flac）。</summary>
    Flac,

    /// <summary>ID3v2（.mp3 / .aif / .aiff）。</summary>
    Id3,
}
