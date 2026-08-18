using System.Text;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Export;

/// <summary>
/// ファイル一覧の現在のタグを CSV に書き出す（docs/SPEC.md 5.2）。
///
/// **画面に出ているとおりの値を書く。** 値は <see cref="ManualEditSet"/> を通して取るため、
/// まだ適用していない手編集も反映される。表と CSV で中身が違うと、どちらが本当か確かめられない
/// （検査結果 CSV の範囲を画面に揃えているのと同じ理由）。
/// </summary>
public static class TrackCsvExporter
{
    /// <summary>編集や複数値の印。検査結果 CSV の「適用予定」と同じ作法にそろえる。</summary>
    private const string MARK = "○";

    /// <summary>タグの前に置く、ファイルそのものを表す列。</summary>
    private static readonly string[] FILE_HEADERS =
    [
        "パス",
        "フォルダ",
        "ファイル名",
        "形式",
        "編集",
        "複数値",
    ];

    /// <summary>
    /// ファイル一覧を CSV 文字列にする。
    /// </summary>
    /// <param name="tracks">書き出す行。渡された順にそのまま並べる。</param>
    /// <param name="edits">保留中の手編集。</param>
    /// <returns>CSV の中身。</returns>
    public static string Build(IEnumerable<TrackTags> tracks, ManualEditSet edits)
    {
        StringBuilder builder = new();

        // タグ列は EDITABLE_FIELDS の順で出す。画面の列順とは作曲家まわりが前後するが、
        // **並びの真実を 2 つ持たない。** ここに独自の順序を書くと、編集できる
        // フィールドが増えたときに CSV だけ取り残される。
        builder.AppendLine(CsvFormat.BuildLine(
            [.. FILE_HEADERS, .. ManualEditConst.EDITABLE_FIELDS.Select(ManualEditConst.Label)]));

        foreach (TrackTags track in tracks)
        {
            string[] cells =
            [
                track.RelativePath,

                // 画面では空欄を (root) と読み替えるが、それは表示だけの置き換え（docs/SPEC.md 5.2）。
                // CSV は並べ替え・絞り込みに使う元の値をそのまま出す。
                Path.GetDirectoryName(track.RelativePath) ?? string.Empty,
                Path.GetFileName(track.RelativePath),
                track.Format.ToString(),
                edits.IsEdited(track.RelativePath) ? MARK : string.Empty,

                // 複数値は連結されて 1 つの値に見えてしまう（docs/TAGGING_POLICY.md 3.4）。
                // 画面では行の色とツールチップで区別しているので、CSV でも落とさない。
                Enum.GetValues<TagField>().Any(track.HasMultipleValues) ? MARK : string.Empty,

                .. ManualEditConst.EDITABLE_FIELDS.Select(field => edits.GetDisplayValue(track, field) ?? string.Empty),
            ];

            builder.AppendLine(CsvFormat.BuildLine(cells));
        }

        return builder.ToString();
    }

    /// <summary>
    /// ファイル一覧を CSV ファイルに書き出す。
    /// </summary>
    /// <param name="path">書き出し先。</param>
    /// <param name="tracks">書き出す行。</param>
    /// <param name="edits">保留中の手編集。</param>
    public static void WriteFile(string path, IEnumerable<TrackTags> tracks, ManualEditSet edits)
    {
        CsvFormat.WriteFile(path, Build(tracks, edits));
    }
}
