namespace MusicTagAuditor.Core.Models;

/// <summary>
/// 検査・編集の対象となる論理タグフィールド。
/// フォーマット別の格納先は docs/TAGGING_POLICY.md 4.1 を参照。
/// </summary>
public enum TagField
{
    /// <summary>曲名（楽章名）。</summary>
    Title,

    /// <summary>その録音の主役。判定順序は docs/TAGGING_POLICY.md 2.2。</summary>
    Artist,

    /// <summary>演奏団体。</summary>
    AlbumArtist,

    /// <summary>作曲家。</summary>
    Composer,

    /// <summary>指揮者。M4A では規格外の <c>©con</c> に格納する。</summary>
    Conductor,

    /// <summary>アルバム名。</summary>
    Album,

    /// <summary>ジャンル。全ファイル <c>Classic</c> に固定する。</summary>
    Genre,

    /// <summary>録音年。4桁で保持する。</summary>
    Date,

    /// <summary>トラック番号。</summary>
    TrackNumber,

    /// <summary>ディスク番号。</summary>
    DiscNumber,
}
