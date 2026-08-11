using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// アルバム 1 枚に相当する単位。**フォルダ + <c>discnumber</c>** で束ねる
/// （docs/TAGGING_POLICY.md 3.5 補足2）。
///
/// アルバム名（R-504）はこの単位で判定する。**ファイル単位で判定してはならない。**
/// 1 アルバムが複数ディスク・複数フォルダに分かれている場合も同じアルバム名にするため（3.5 規則3）。
/// </summary>
/// <param name="Folder">ライブラリルートからのフォルダ。ルート直下は空文字。</param>
/// <param name="Disc">ディスク番号。未設定は 1 とみなす。</param>
/// <param name="Tracks">この単位に属するファイル。</param>
/// <param name="Composers">正規形に寄せた <c>composer</c> の相異なる値。</param>
/// <param name="Artists">正規形に寄せた <c>artist</c> の相異なる値。</param>
/// <param name="Dates">4 桁に直した <c>date</c> の相異なる値。</param>
/// <param name="Albums">相異なる <c>album</c>。現在値であり正規形ではない。</param>
public sealed record AlbumUnit(
    string Folder,
    int Disc,
    IReadOnlyList<TrackTags> Tracks,
    IReadOnlyList<string> Composers,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> Dates,
    IReadOnlyList<string> Albums)
{
    /// <summary>
    /// 走査結果をアルバム単位に束ねる。
    /// </summary>
    /// <param name="tracks">対象のファイル。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <returns>アルバム単位。フォルダ順、同一フォルダ内はディスク順。</returns>
    public static IReadOnlyList<AlbumUnit> Build(IReadOnlyList<TrackTags> tracks, DictionaryIndex dictionary)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(dictionary);

        return
        [
            .. tracks
                .GroupBy(track => (Folder: InspectionContext.GetFolder(track.RelativePath), Disc: GetDisc(track.DiscNumber)))
                .Select(group => new AlbumUnit(
                    group.Key.Folder,
                    group.Key.Disc,
                    [.. group],
                    Distinct(group, TagField.Composer, value => ToComposer(dictionary, value)),
                    Distinct(group, TagField.Artist, value => ToPerson(dictionary, value)),
                    [.. group.Select(InspectionContext.GetRecordingYear)
                        .Where(year => year is not null)
                        .Select(year => year!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)],
                    Distinct(group, TagField.Album, value => value)))
                .OrderBy(unit => unit.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(unit => unit.Disc),
        ];
    }

    /// <summary>
    /// <c>discnumber</c> からディスク番号を取り出す。
    ///
    /// **未設定は 1 とみなす。** 単一ディスクでも <c>1/1</c> を入れる規則
    /// （docs/TAGGING_POLICY.md 2.4）は未適用のファイルが残っており、未設定を別扱いにすると
    /// 同じアルバムが 2 単位に割れる。
    /// </summary>
    /// <param name="discNumber">「番号/総数」または「番号」。</param>
    /// <returns>ディスク番号。</returns>
    public static int GetDisc(string? discNumber)
    {
        if (string.IsNullOrWhiteSpace(discNumber))
        {
            return 1;
        }

        string head = discNumber.Split('/')[0].Trim();

        return int.TryParse(head, out int disc) && disc > 0 ? disc : 1;
    }

    /// <summary>
    /// 単位内のあるフィールドについて、正規化したうえで相異なる値を並べる。
    /// </summary>
    private static IReadOnlyList<string> Distinct(
        IEnumerable<TrackTags> tracks,
        TagField field,
        Func<string, string> normalize)
    {
        return
        [
            .. tracks
                .SelectMany(track => track.GetValues(field))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(normalize)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 作曲家を正規形に寄せる。辞書に無ければ元の値のまま。
    /// </summary>
    private static string ToComposer(DictionaryIndex dictionary, string value)
    {
        return dictionary.TryResolveComposer(value, out string canonical) ? canonical : value;
    }

    /// <summary>
    /// 人物を正規形に寄せる。辞書に無ければ元の値のまま。
    /// </summary>
    private static string ToPerson(DictionaryIndex dictionary, string value)
    {
        return dictionary.TryResolvePerson(value, out PersonEntry person) ? person.Canonical : value;
    }
}
