using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection.Rules;

/// <summary>
/// R-501: 同一アルバム名に複数の作曲家／演奏者が混在。
///
/// <c>Symphony No.5</c> のような汎用的な名前に、作曲家も演奏者も異なる複数の録音が
/// 同居している。**自動修正しない。**
///
/// 書式は <c>{作曲家}: {作品名} - {date}/{artist}</c> で確定している
/// （docs/TAGGING_POLICY.md 3.5）。それでも修正案を出せないのは、<c>{作品名}</c> の唯一の供給元である
/// 正規化辞書の作品エントリが未整備だからである（docs/SPEC.md 13章 D6）。<c>{作品名}</c> は作品そのものの
/// 名前であり、<c>title</c>（楽章名）からも <c>album</c>（汎用名）からも一意には取れない。
/// </summary>
public sealed class AlbumNameCollisionRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-501";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "同一アルバム名に複数の作曲家／演奏者が混在";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        var byAlbum = context.Tracks
            .Where(track => track.GetValues(TagField.Album).Count == 1)
            .GroupBy(track => track.Album!, StringComparer.Ordinal);

        foreach (var album in byAlbum)
        {
            // 表記揺れを人数に数えない。`Pyotr Il'yich Tchaikovsky` と `Pyotr Ilyich Tchaikovsky` は
            // R-201 が直す同一人物であり、ここで 2 人と数えると混在の程度を誤って伝える。
            string[] composers =
            [
                .. album.Select(track => track.Composer)
                    .Where(composer => !string.IsNullOrEmpty(composer))
                    .Select(composer => context.Dictionary.TryResolveComposer(composer, out string canonical)
                        ? canonical
                        : composer!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];

            if (composers.Length < 2)
            {
                continue;
            }

            foreach (TrackTags track in album)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Album,
                    track.GetValues(TagField.Album),
                    [],
                    Id,
                    $"同名のアルバムに {composers.Length} 人の作曲家が混在（{string.Join(" / ", composers)}）。"
                    + " 「{作曲家}: {作品名} - {date}/{artist}」形式（TAGGING_POLICY 3.5）への移行が要る",
                    Severity.Warning);
            }
        }
    }
}

/// <summary>
/// R-502: アルバム名が日本語。
///
/// 日本語略称（<c>ベト7</c>、<c>マーラー2</c>）と正式な日本語名（<c>歌劇「ローエングリン」</c>）の
/// 混在が未解消である。**どちらも検出する。**
///
/// 書式は <c>{作曲家}: {作品名} - {date}/{artist}</c> で確定しており（docs/TAGGING_POLICY.md 3.5）、
/// 日本語略称は作品エントリの別名として登録すれば書式への移行過程で解消する。
/// **アルバム名だけを個別に日本語→英語へ直す作業は行わない**（6.1）。修正案を出せないのは
/// <c>{作品名}</c> の供給元である作品エントリが未整備だからである（docs/SPEC.md 13章 D6）。
/// 略称と判別できたものは根拠に作曲家の正規形を出す。
/// </summary>
public sealed class JapaneseAlbumNameRule : IInspectionRule
{
    /// <summary>日本語の文字。</summary>
    private static readonly Regex JAPANESE = new(
        @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]",
        RegexOptions.Compiled);

    /// <summary>末尾の番号。<c>ベト7</c> の <c>7</c>。</summary>
    private static readonly Regex TRAILING_NUMBER = new(@"^(?<name>.+?)\s*(?<number>\d+)$", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Id => "R-502";

    /// <inheritdoc />
    public Severity Severity => Severity.Info;

    /// <inheritdoc />
    public string Description => "アルバム名が日本語";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Album);

            if (values.Count != 1 || !JAPANESE.IsMatch(values[0]))
            {
                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Album,
                values,
                [],
                Id,
                BuildRationale(values[0], context),
                Severity.Info);
        }
    }

    /// <summary>
    /// 根拠を組み立てる。略称と判別できたら作曲家の正規形を添える。
    /// </summary>
    private static string BuildRationale(string album, InspectionContext context)
    {
        Match match = TRAILING_NUMBER.Match(album);

        if (match.Success
            && context.Dictionary.TryResolveComposer(match.Groups["name"].Value, out string composer))
        {
            return $"日本語略称。「{composer}」の第 {match.Groups["number"].Value} 番と思われる。"
                + " 「{作曲家}: {作品名} - {date}/{artist}」形式（TAGGING_POLICY 3.5）への移行が要る";
        }

        return "アルバム名が日本語。「{作曲家}: {作品名} - {date}/{artist}」形式（TAGGING_POLICY 3.5）"
            + "への移行が要るが、作品名の供給元（作品エントリ）が未整備のため修正案は出せない";
    }
}

/// <summary>
/// R-503: 楽章番号の書式がフォルダ内で不統一（docs/TAGGING_POLICY.md 6.2）。
///
/// <c>1.</c> / <c>5-1.</c> / <c>1 Allegro</c> / <c>I.</c> / 番号なし が混在している。
/// **統一方針が未決定なので自動修正しない。** どの書式に寄せるかを決められない。
/// </summary>
public sealed class MovementNumberStyleRule : IInspectionRule
{
    /// <summary>書式の判定。上から順に試す。</summary>
    private static readonly (Regex Pattern, string Label)[] STYLES =
    [
        (new Regex(@"^\d+-\d+\.", RegexOptions.Compiled), "5-1. 形式"),
        (new Regex(@"^\d+\.", RegexOptions.Compiled), "1. 形式"),
        (new Regex(@"^\d+\s+\S", RegexOptions.Compiled), "1 Allegro 形式"),
        (new Regex(@"^\(\s*[IVXLC]+[\.\s]", RegexOptions.Compiled), "(I. 形式"),
        (new Regex(@"^[IVXLC]+[\.\s]", RegexOptions.Compiled), "I. 形式"),
        (new Regex(@"^第[０-９0-9一二三四五六七八九十]+楽章", RegexOptions.Compiled), "第N楽章 形式"),
    ];

    /// <summary>番号が無い場合の表示。</summary>
    private const string NO_NUMBER = "番号なし";

    /// <inheritdoc />
    public string Id => "R-503";

    /// <inheritdoc />
    public Severity Severity => Severity.Info;

    /// <inheritdoc />
    public string Description => "楽章番号の書式がフォルダ内で不統一";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        var byFolder = context.Tracks
            .GroupBy(track => InspectionContext.GetFolder(track.RelativePath), StringComparer.OrdinalIgnoreCase);

        foreach (var folder in byFolder)
        {
            TrackTags[] tracks = [.. folder.Where(track => track.GetValues(TagField.Title).Count == 1)];

            if (tracks.Length < 2)
            {
                continue;
            }

            string[] styles = [.. tracks.Select(track => GetStyle(track.Title!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

            if (styles.Length < 2)
            {
                continue;
            }

            foreach (TrackTags track in tracks)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Title,
                    track.GetValues(TagField.Title),
                    [],
                    Id,
                    $"このフォルダに {string.Join(" / ", styles)} が混在（この曲は {GetStyle(track.Title!)}）。"
                    + " 統一方針は未決定（TAGGING_POLICY 6.2）",
                    Severity.Info);
            }
        }
    }

    /// <summary>
    /// 曲名の先頭から楽章番号の書式を判定する。
    /// </summary>
    /// <param name="title">曲名。</param>
    /// <returns>書式の表示名。</returns>
    public static string GetStyle(string title)
    {
        foreach ((Regex pattern, string label) in STYLES)
        {
            if (pattern.IsMatch(title))
            {
                return label;
            }
        }

        return NO_NUMBER;
    }
}
