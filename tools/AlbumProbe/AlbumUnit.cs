using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace AlbumProbe;

/// <summary>
/// アルバム 1 枚に相当する単位。**フォルダ + <c>discnumber</c>** で束ねる
/// （docs/TAGGING_POLICY.md 3.5 補足2）。
///
/// この切り方は 1 アルバムを割ることがある（歌劇をフォルダで幕別に分けている場合など）。
/// アルバム名の生成では割れていても同じ名前が出るだけなので害はないが、
/// 単位内で <c>composer</c> や <c>date</c> が一意かを数えるときは影響する。
/// </summary>
/// <param name="Folder">ライブラリルートからのフォルダ。ルート直下は <see cref="Const.ROOT_FOLDER_LABEL"/>。</param>
/// <param name="Disc">ディスク番号。未設定は 1 とみなす。</param>
/// <param name="Tracks">この単位に属するファイル。</param>
/// <param name="Composers">正規形に寄せた <c>composer</c> の相異なる値。</param>
/// <param name="Artists">正規形に寄せた <c>artist</c> の相異なる値。</param>
/// <param name="AlbumArtists">実体 ID に寄せた <c>albumartist</c> の相異なる値。</param>
/// <param name="Dates">相異なる <c>date</c>。</param>
/// <param name="Albums">相異なる <c>album</c>。現在値であり、正規形ではない。</param>
public sealed record AlbumUnit(
    string Folder,
    int Disc,
    IReadOnlyList<TrackTags> Tracks,
    IReadOnlyList<string> Composers,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> AlbumArtists,
    IReadOnlyList<string> Dates,
    IReadOnlyList<string> Albums)
{
    /// <summary>
    /// 走査結果をアルバム単位に束ねる。
    /// </summary>
    /// <param name="tracks">走査で読み取ったタグ。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <returns>アルバム単位。フォルダ順、同一フォルダ内はディスク順。</returns>
    public static IReadOnlyList<AlbumUnit> Build(IReadOnlyList<TrackTags> tracks, DictionaryIndex dictionary)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(dictionary);

        return
        [
            .. tracks
                .GroupBy(track => (Folder: GetFolder(track.RelativePath), Disc: GetDisc(track.DiscNumber)))
                .Select(group => new AlbumUnit(
                    group.Key.Folder,
                    group.Key.Disc,
                    [.. group],
                    Distinct(group, TagField.Composer, value => ToComposer(dictionary, value)),
                    Distinct(group, TagField.Artist, value => ToPerson(dictionary, value)),
                    Distinct(group, TagField.AlbumArtist, value => ToEnsemble(dictionary, value)),
                    Distinct(group, TagField.Date, value => value),
                    Distinct(group, TagField.Album, value => value)))
                .OrderBy(unit => unit.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(unit => unit.Disc),
        ];
    }

    /// <summary>
    /// 値が 1 つに定まっていればそれを、定まっていなければ表示用の文字列を返す。
    /// </summary>
    /// <param name="values">対象の値。</param>
    /// <returns>表示用の 1 文字列。</returns>
    public static string Single(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.Count switch
        {
            0 => Const.NO_VALUE,
            1 => values[0],
            _ => string.Join("+", values),
        };
    }

    /// <summary>
    /// 相対パスのフォルダ部分を返す。
    /// </summary>
    /// <param name="relativePath">ライブラリルートからの相対パス。</param>
    /// <returns>フォルダ。ルート直下なら <see cref="Const.ROOT_FOLDER_LABEL"/>。</returns>
    private static string GetFolder(string relativePath)
    {
        string? folder = Path.GetDirectoryName(relativePath);

        return string.IsNullOrEmpty(folder) ? Const.ROOT_FOLDER_LABEL : folder;
    }

    /// <summary>
    /// <c>discnumber</c> からディスク番号を取り出す。
    ///
    /// **未設定は 1 とみなす。** 単一ディスクでも <c>1/1</c> を入れる規則
    /// （docs/TAGGING_POLICY.md 2.4）は未適用のファイルが残っており、
    /// 未設定を別扱いにすると同じアルバムが 2 単位に割れる。
    /// </summary>
    /// <param name="discNumber">「番号/総数」または「番号」。</param>
    /// <returns>ディスク番号。</returns>
    private static int GetDisc(string? discNumber)
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
    /// <param name="tracks">単位に属するファイル。</param>
    /// <param name="field">対象フィールド。</param>
    /// <param name="normalize">正規化。</param>
    /// <returns>相異なる値。順序は安定させる。</returns>
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
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="value">タグの値。</param>
    /// <returns>正規形。</returns>
    private static string ToComposer(DictionaryIndex dictionary, string value)
    {
        return dictionary.TryResolveComposer(value, out string canonical) ? canonical : value;
    }

    /// <summary>
    /// 人物を正規形に寄せる。辞書に無ければ元の値のまま。
    /// </summary>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="value">タグの値。</param>
    /// <returns>正規形。</returns>
    private static string ToPerson(DictionaryIndex dictionary, string value)
    {
        return dictionary.TryResolvePerson(value, out PersonEntry person) ? person.Canonical : value;
    }

    /// <summary>
    /// 演奏団体を実体 ID に寄せる。
    ///
    /// **名前で束ねない。** <c>Leningrad Philharmonic Orchestra</c> と
    /// <c>Saint Petersburg Philharmonic Orchestra</c> は名前が似ていないが同一実体である
    /// （docs/TAGGING_POLICY.md 5.3.1）。
    /// </summary>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="value">タグの値。</param>
    /// <returns>実体 ID。辞書に無ければ元の値のまま。</returns>
    private static string ToEnsemble(DictionaryIndex dictionary, string value)
    {
        return dictionary.TryResolveEnsemble(value, out EnsembleEntry ensemble)
            ? Const.ENTITY_ID_PREFIX + ensemble.EntityId
            : value;
    }
}
