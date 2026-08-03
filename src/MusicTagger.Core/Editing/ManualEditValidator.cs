using System.Text.RegularExpressions;
using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Editing;

/// <summary>
/// 手編集で気づいてほしい点 1 件。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="Message">内容。</param>
public sealed record ManualEditWarning(string RelativePath, TagField Field, string Message)
{
    /// <summary>一覧表示用の 1 行。</summary>
    public string Summary => $"{RelativePath} [{ManualEditConst.Label(Field)}] {Message}";
}

/// <summary>
/// 手編集の入力を検査する。
///
/// **止めない。** ここで出すのはすべて「気づいてほしい点」であり、最終的な判断は人間が持つ。
/// 手で入れた値をツールが拒むと、原則の例外（配役情報や個別例外）を扱えなくなる。
/// ただし黙って通すと AIMP でタグが壊れる種類の入力があるので、それは必ず知らせる。
/// </summary>
public static class ManualEditValidator
{
    /// <summary>4 桁の年だけを認める正規表現（docs/TAGGING_POLICY.md 2.4）。</summary>
    private static readonly Regex YEAR_ONLY = new(@"^\d{4}$", RegexOptions.Compiled);

    /// <summary>「番号/総数」の書式（docs/TAGGING_POLICY.md 2.4）。</summary>
    private static readonly Regex NUMBER_PAIR = new(@"^\d+/\d+$", RegexOptions.Compiled);

    /// <summary>ジャンルの固定値。</summary>
    private const string GENRE_VALUE = "Classic";

    /// <summary>人名・団体名を入れるフィールド。</summary>
    private static readonly TagField[] NAME_FIELDS =
        [TagField.Artist, TagField.AlbumArtist, TagField.Composer, TagField.Conductor];

    /// <summary>
    /// 手編集の差分を検査する。
    /// </summary>
    /// <param name="changes">手編集の差分。</param>
    /// <param name="tracks">編集前のタグ。複数値かどうかの判定に使う。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <returns>気づいてほしい点。無ければ空。</returns>
    public static IReadOnlyList<ManualEditWarning> Validate(
        IEnumerable<TagChange> changes,
        IEnumerable<TrackTags> tracks,
        DictionaryIndex dictionary)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(dictionary);

        Dictionary<string, TrackTags> byPath = tracks.ToDictionary(
            track => track.RelativePath,
            StringComparer.OrdinalIgnoreCase);

        List<ManualEditWarning> warnings = [];

        foreach (TagChange change in changes)
        {
            byPath.TryGetValue(change.RelativePath, out TrackTags? track);

            CheckProtectedAlbumArtist(warnings, change, track, dictionary);
            CheckMultipleValues(warnings, change, track);

            if (change.ClearsValue)
            {
                continue;
            }

            string value = change.AfterValues.Count > 0 ? change.AfterValues[0] : string.Empty;

            CheckSemicolon(warnings, change, value);
            CheckFieldFormat(warnings, change, value);
            CheckNameValue(warnings, change, value, dictionary);
        }

        return warnings;
    }

    /// <summary>
    /// 配役情報を含む <c>albumartist</c> を書き換えようとしていないかを確認する
    /// （docs/TAGGING_POLICY.md 2.3）。
    ///
    /// 保護対象は楽団名に縮めると情報が失われるため、検査ルールは触らない。
    /// 手編集はその保護を越えられるので、越えていることを知らせる。
    /// </summary>
    private static void CheckProtectedAlbumArtist(
        List<ManualEditWarning> warnings,
        TagChange change,
        TrackTags? track,
        DictionaryIndex dictionary)
    {
        if (change.Field != TagField.AlbumArtist || track is null)
        {
            return;
        }

        if (!track.GetValues(TagField.AlbumArtist).Any(dictionary.IsProtectedAlbumArtist))
        {
            return;
        }

        warnings.Add(new ManualEditWarning(
            change.RelativePath,
            change.Field,
            "配役情報として保護されている値を書き換えます。楽団名に縮めると歌手・合唱団の情報が失われます。"));
    }

    /// <summary>
    /// 複数値として格納されているフィールドを 1 値に潰していないかを確認する。
    ///
    /// M4A では複数値を書き分けられない（docs/adr/0001-tag-io-library.md）。
    /// 潰したこと自体は読み戻し照合では検出できないので、書く前に知らせる。
    /// </summary>
    private static void CheckMultipleValues(List<ManualEditWarning> warnings, TagChange change, TrackTags? track)
    {
        if (track is null || !track.HasMultipleValues(change.Field))
        {
            return;
        }

        warnings.Add(new ManualEditWarning(
            change.RelativePath,
            change.Field,
            $"複数値（{track.GetValues(change.Field).Count} 値）として格納されています。手編集すると 1 値にまとまります。"));
    }

    /// <summary>
    /// 値に <c>;</c> が含まれていないかを確認する（docs/TAGGING_POLICY.md 3.4）。
    /// AIMP は保存時にこれを複数値の区切りとして解釈し、1 つの値を分割する。
    /// </summary>
    private static void CheckSemicolon(List<ManualEditWarning> warnings, TagChange change, string value)
    {
        if (!value.Contains(';', StringComparison.Ordinal))
        {
            return;
        }

        warnings.Add(new ManualEditWarning(
            change.RelativePath,
            change.Field,
            "値に ; が含まれます。AIMP は保存時にこれを区切りとして複数値に分割します。"));
    }

    /// <summary>
    /// フィールドごとの書式を確認する（docs/TAGGING_POLICY.md 2.4）。
    /// </summary>
    private static void CheckFieldFormat(List<ManualEditWarning> warnings, TagChange change, string value)
    {
        switch (change.Field)
        {
            case TagField.Genre when value != GENRE_VALUE:
                warnings.Add(new ManualEditWarning(
                    change.RelativePath,
                    change.Field,
                    $"genre は全ファイル「{GENRE_VALUE}」に固定します。"));
                break;

            case TagField.Date when !YEAR_ONLY.IsMatch(value):
                warnings.Add(new ManualEditWarning(
                    change.RelativePath,
                    change.Field,
                    "date は録音年を 4 桁で入れます。ISO 形式は使いません。"));
                break;

            case TagField.TrackNumber or TagField.DiscNumber when !NUMBER_PAIR.IsMatch(value):
                warnings.Add(new ManualEditWarning(
                    change.RelativePath,
                    change.Field,
                    "「番号/総数」の書式で入れます。単一ディスクでも 1/1 とします。"));
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 人名・団体名の値を確認する。
    ///
    /// 辞書に無い名前は誤記の可能性がある。段階 5 の辞書追加導線へ回してもらうために知らせる。
    /// </summary>
    private static void CheckNameValue(
        List<ManualEditWarning> warnings,
        TagChange change,
        string value,
        DictionaryIndex dictionary)
    {
        if (!NAME_FIELDS.Contains(change.Field))
        {
            return;
        }

        if (DictionaryEditor.IsJapanese(value))
        {
            warnings.Add(new ManualEditWarning(
                change.RelativePath,
                change.Field,
                "人名・団体名はラテン文字で表記します（TAGGING_POLICY 3.1）。"));
        }

        if (change.Field != TagField.Composer && dictionary.ContainsComposerName(value, out string? composer))
        {
            warnings.Add(new ManualEditWarning(
                change.RelativePath,
                change.Field,
                $"作曲家「{composer}」を入れようとしています。{ManualEditConst.Label(change.Field)} に作曲家名は入れません（TAGGING_POLICY 2.1）。"));

            return;
        }

        if (!DictionaryEditor.IsAlreadyKnown(dictionary, value, out _))
        {
            warnings.Add(new ManualEditWarning(
                change.RelativePath,
                change.Field,
                "辞書に無い名前です。誤記でなければ、辞書に登録しておくと以降の検査で正規形として扱われます。"));
        }
    }
}
