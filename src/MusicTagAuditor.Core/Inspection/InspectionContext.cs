using System.Globalization;
using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// 検査の設定。
/// </summary>
public sealed class InspectionOptions
{
    /// <summary>
    /// 既定で無効なルールのうち、利用者が明示的に有効にしたもの。
    ///
    /// 誤検出が増えるルールは既定で無効にし、選んだときだけ動かす（docs/SPEC.md 6.2）。
    /// 現在の対象は R-304（曲名中の発音区別符号の欠落）。
    /// </summary>
    public IReadOnlySet<string> EnabledOptionalRuleIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 既定で無効なルールが有効にされているかを返す。
    /// </summary>
    /// <param name="ruleId">ルール ID。</param>
    /// <returns>有効なら true。</returns>
    public bool IsEnabled(string ruleId)
    {
        return EnabledOptionalRuleIds.Contains(ruleId);
    }
}

/// <summary>
/// 検査ルールに渡す文脈。
///
/// **ライブラリ全体のスナップショットを持つ。** 指揮者の特定・作曲家の推定・アルバム内の混在検出は
/// いずれもファイル横断の判断が要るため、単一ファイル単位の入力にはしない。
/// </summary>
public sealed class InspectionContext
{
    /// <summary>4 桁の年を取り出す正規表現。</summary>
    private static readonly Regex YEAR_PATTERN = new(@"(1[0-9]{3}|20[0-9]{2})", RegexOptions.Compiled);

    /// <summary>フォルダごとのファイル。</summary>
    private readonly Dictionary<string, IReadOnlyList<TrackTags>> _tracksByFolder;

    /// <summary>
    /// 文脈を組み立てる。
    /// </summary>
    /// <param name="scan">スキャン結果。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="options">検査の設定。</param>
    public InspectionContext(ScanResult scan, DictionaryIndex dictionary, InspectionOptions? options = null)
    {
        LibraryRoot = scan.LibraryRoot;
        Tracks = scan.Tracks;
        Dictionary = dictionary;
        Options = options ?? new InspectionOptions();

        _tracksByFolder = scan.Tracks
            .GroupBy(track => GetFolder(track.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TrackTags>)[.. group], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>ライブラリのルート。</summary>
    public string LibraryRoot { get; }

    /// <summary>全ファイルのタグ。</summary>
    public IReadOnlyList<TrackTags> Tracks { get; }

    /// <summary>正規化辞書の索引。</summary>
    public DictionaryIndex Dictionary { get; }

    /// <summary>検査の設定。</summary>
    public InspectionOptions Options { get; }

    /// <summary>
    /// 指定ファイルと同じフォルダにあるファイルを返す。
    /// </summary>
    /// <param name="track">基準になるファイル。</param>
    /// <returns>同じフォルダのファイル（自身を含む）。</returns>
    public IReadOnlyList<TrackTags> GetSiblings(TrackTags track)
    {
        return _tracksByFolder.TryGetValue(GetFolder(track.RelativePath), out IReadOnlyList<TrackTags>? siblings)
            ? siblings
            : [];
    }

    /// <summary>
    /// 相対パスからフォルダ部分を取り出す。
    /// </summary>
    /// <param name="relativePath">相対パス。</param>
    /// <returns>フォルダ部分。ルート直下なら空文字。</returns>
    public static string GetFolder(string relativePath)
    {
        return Path.GetDirectoryName(relativePath) ?? string.Empty;
    }

    /// <summary>
    /// 録音年を取り出す。時代分割の判定に要る（docs/TAGGING_POLICY.md 3.1.2 規則3）。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <returns>録音年。<c>date</c> から読み取れない場合は null。</returns>
    public static int? GetRecordingYear(TrackTags track)
    {
        string? date = track.Date;

        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        Match match = YEAR_PATTERN.Match(date);

        return match.Success && int.TryParse(match.Value, CultureInfo.InvariantCulture, out int year)
            ? year
            : null;
    }

    /// <summary>
    /// このファイルを検査対象から外すべきかを判定する。
    ///
    /// **2.3 の保護対象は全ルールの検査前に除外する。** 除外しないと R-207 / R-208 が
    /// 誤検出だらけになる（docs/library-baseline-2026-08-03.md）。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <param name="field">対象フィールド。</param>
    /// <returns>除外すべきなら true。</returns>
    public bool IsProtected(TrackTags track, TagField field)
    {
        if (field != TagField.AlbumArtist)
        {
            return false;
        }

        return track.GetValues(TagField.AlbumArtist).Any(Dictionary.IsProtectedAlbumArtist);
    }
}
