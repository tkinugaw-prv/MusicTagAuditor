using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.TagIo;

/// <summary>
/// タグ入出力で使う定数。フォーマット別の格納先は docs/TAGGING_POLICY.md 4.1 に対応する。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class TagIoConst
{
    /// <summary>
    /// M4A の指揮者 atom。MP4 に指揮者の標準 atom は存在しないが、AIMP がこれを採用しているため用いる
    /// （docs/adr/0001-tag-io-library.md で実機確認済み）。先頭バイトは 0xA9。
    /// </summary>
    public const string ATOM_CONDUCTOR = "©con";

    /// <summary>
    /// TagLib# の <c>Tag.Conductor</c> が書いてしまう atom。AIMP はこれを読まない。
    /// 検出したら誤った書き込みの痕跡として扱う。
    /// </summary>
    public const string ATOM_CONDUCTOR_WRONG = "cond";

    /// <summary>MP4 のフリーフォーム atom の型名。</summary>
    public const string ATOM_FREEFORM = "----";

    /// <summary>M4A の論理フィールドと atom の対応。</summary>
    public static readonly IReadOnlyDictionary<TagField, string> MP4_ATOM_BY_FIELD =
        new Dictionary<TagField, string>
        {
            [TagField.Title] = "©nam",
            [TagField.Artist] = "©ART",
            [TagField.AlbumArtist] = "aART",
            [TagField.Composer] = "©wrt",
            [TagField.Conductor] = ATOM_CONDUCTOR,
            [TagField.Album] = "©alb",
            [TagField.Genre] = "©gen",
            [TagField.Date] = "©day",
            [TagField.TrackNumber] = "trkn",
            [TagField.DiscNumber] = "disk",
            [TagField.Comment] = "©cmt",
        };

    /// <summary>FLAC（Vorbis comment）の論理フィールドとフィールド名の対応。</summary>
    public static readonly IReadOnlyDictionary<TagField, string> VORBIS_FIELD_BY_FIELD =
        new Dictionary<TagField, string>
        {
            [TagField.Title] = "TITLE",
            [TagField.Artist] = "ARTIST",
            [TagField.AlbumArtist] = "ALBUMARTIST",
            [TagField.Composer] = "COMPOSER",
            [TagField.Conductor] = "CONDUCTOR",
            [TagField.Album] = "ALBUM",
            [TagField.Genre] = "GENRE",
            [TagField.Date] = "DATE",
            [TagField.TrackNumber] = "TRACKNUMBER",
            [TagField.DiscNumber] = "DISCNUMBER",
            [TagField.Comment] = "COMMENT",
        };

    /// <summary>
    /// ID3v2（MP3 / AIFF）の論理フィールドとフレーム ID の対応。
    ///
    /// **<c>Comment</c> をあえて載せていない。** ID3v2 の <c>COMM</c> は、iTunes が
    /// <c>iTunNORM</c> / <c>iTunSMPB</c> / <c>iTunes_CDDB_IDs</c> を description 付きで
    /// 格納する場所でもある。この辞書に載せると <see cref="TagReader"/> は description の
    /// 違いを見ずに全フレームを平坦化した値を拾い、<see cref="TagWriter"/> は値が空のときの
    /// <c>RemoveFrames</c> で音量正規化情報ごと消してしまう（docs/SPEC.md 4.1 V3 が
    /// 名指しで警戒している事故で、<c>RawTags</c> は記録用なので復旧もできない）。
    ///
    /// 実測（2026-08-15）では対象ライブラリの AIFF 11 件すべてが description 付きの
    /// <c>COMM</c> を 3 個ずつ持ち、利用者のコメント（description が空のもの）は
    /// 1 件も無かった。空 description だけを読み書きする特別扱いで回避はできるが、
    /// 得られるのは 15 ファイルで comment を編集できることだけなので採らない。
    /// 判断の記録は docs/TAGGING_POLICY.md 4.4。
    /// </summary>
    /// <remarks>
    /// 扱えない組み合わせの宣言は <c>TagFieldConst.IsSupported</c> にある。
    /// 両者の食い違いは TagIo.Tests の <c>TagIoConstMatchesSupportedFields</c> が検出する。
    /// </remarks>
    public static readonly IReadOnlyDictionary<TagField, string> ID3_FRAME_BY_FIELD =
        new Dictionary<TagField, string>
        {
            [TagField.Title] = "TIT2",
            [TagField.Artist] = "TPE1",
            [TagField.AlbumArtist] = "TPE2",
            [TagField.Composer] = "TCOM",
            [TagField.Conductor] = "TPE3",
            [TagField.Album] = "TALB",
            [TagField.Genre] = "TCON",
            [TagField.Date] = "TDRC",
            [TagField.TrackNumber] = "TRCK",
            [TagField.DiscNumber] = "TPOS",
        };

    /// <summary>拡張子とタグ格納形式の対応。将来の拡張子追加はここに足す。</summary>
    public static readonly IReadOnlyDictionary<string, AudioFormat> FORMAT_BY_EXTENSION =
        new Dictionary<string, AudioFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".m4a"] = AudioFormat.M4a,
            [".flac"] = AudioFormat.Flac,
            [".mp3"] = AudioFormat.Id3,
            [".aif"] = AudioFormat.Id3,
            [".aiff"] = AudioFormat.Id3,
        };

    /// <summary>
    /// テキストとして表現できない生タグ値に付ける接頭辞。
    /// これに続く文字列は 16 進表記のバイト列である。
    /// </summary>
    public const string BINARY_VALUE_PREFIX = "0x";
}
