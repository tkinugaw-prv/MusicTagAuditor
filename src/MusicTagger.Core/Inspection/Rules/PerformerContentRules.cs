using System.Text.RegularExpressions;
using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Inspection.Rules;

/// <summary>R-203: <c>artist</c> に作曲家名が入っている。</summary>
public sealed class ComposerInArtistRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-203";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "artist に作曲家名が入っている";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Artist);

            if (values.Count != 1 || !context.Dictionary.ContainsComposerName(values[0], out string? composer))
            {
                continue;
            }

            ConductorHit? hit = ConductorFinder.Find(track, context);

            if (hit is null)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Artist,
                    values,
                    [],
                    Id,
                    $"作曲家「{composer}」が artist に入っているが、指揮者を特定できない。CD 実物の確認が要る",
                    Severity.Info);

                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Artist,
                values,
                [hit.Canonical],
                Id,
                $"作曲家「{composer}」が artist に入っている。{hit.Source}",
                Severity.Error);
        }
    }
}

/// <summary>R-204: <c>albumartist</c> に作曲家名が入っている。</summary>
public sealed class ComposerInAlbumArtistRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-204";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "albumartist に作曲家名が入っている";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            if (context.IsProtected(track, TagField.AlbumArtist))
            {
                continue;
            }

            IReadOnlyList<string> values = track.GetValues(TagField.AlbumArtist);

            if (values.Count != 1 || !context.Dictionary.ContainsComposerName(values[0], out string? composer))
            {
                continue;
            }

            string? ensemble = FindEnsemble(track, context);

            if (ensemble is null)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.AlbumArtist,
                    values,
                    [],
                    Id,
                    $"作曲家「{composer}」が albumartist に入っているが、楽団名が不明。CD 実物の確認が要る",
                    Severity.Info);

                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.AlbumArtist,
                values,
                [ensemble],
                Id,
                $"作曲家「{composer}」が albumartist に入っている。同一フォルダの他ファイルから楽団を特定",
                Severity.Error);
        }
    }

    /// <summary>
    /// 同一フォルダの他ファイルから楽団名を探す。
    /// </summary>
    private static string? FindEnsemble(TrackTags track, InspectionContext context)
    {
        foreach (TrackTags sibling in context.GetSiblings(track))
        {
            if (ReferenceEquals(sibling, track))
            {
                continue;
            }

            foreach (string value in sibling.GetValues(TagField.AlbumArtist))
            {
                if (context.Dictionary.TryResolveEnsemble(value, out EnsembleEntry ensemble)
                    && (ensemble.Eras?.Count ?? 0) == 0
                    && ensemble.Canonical is not null)
                {
                    return ensemble.Canonical;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// R-205: 値に <c>;</c> が含まれる。
///
/// AIMP は保存時に <c>;</c> を複数値へ分割する（docs/TAGGING_POLICY.md 3.4）。
/// ただし修正方法は文脈による。曲名の <c>;</c> は正当な句読点であり（6.8）、
/// 配役情報の <c>;</c> は保護対象である（2.3）。**自動修正はしない。**
/// </summary>
public sealed class SemicolonValueRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-205";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "値に ; が含まれる（AIMP が分割する）";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            foreach (TagField field in Enum.GetValues<TagField>())
            {
                if (context.IsProtected(track, field))
                {
                    continue;
                }

                IReadOnlyList<string> values = track.GetValues(field);

                if (!values.Any(value => value.Contains(';', StringComparison.Ordinal)))
                {
                    continue;
                }

                string rationale = field == TagField.Title
                    ? "曲名中の ; は正当な句読点の可能性が高い。AIMP で保存すると分割されるため方針は未決定"
                    : "AIMP はこの値を保存時に複数値へ分割する。フィールドを分けるか判断が要る";

                yield return new TagChange(
                    track.RelativePath,
                    field,
                    values,
                    [],
                    Id,
                    rationale,
                    Severity.Info);
            }
        }
    }
}

/// <summary>R-206: 同一値の重複連結（<c>Anton Bruckner; Anton Bruckner</c>）。</summary>
public sealed class DuplicateConcatenationRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-206";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "同一値の重複連結";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            foreach (TagField field in Enum.GetValues<TagField>())
            {
                if (context.IsProtected(track, field))
                {
                    continue;
                }

                IReadOnlyList<string> values = track.GetValues(field);

                foreach (string value in values)
                {
                    string[] parts =
                    [
                        .. value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                    ];

                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    string[] unique = [.. parts.Distinct(StringComparer.OrdinalIgnoreCase)];

                    if (unique.Length == parts.Length)
                    {
                        continue;
                    }

                    if (unique.Length == 1)
                    {
                        yield return new TagChange(
                            track.RelativePath,
                            field,
                            values,
                            [unique[0]],
                            Id,
                            $"同じ値が {parts.Length} 回繰り返されている",
                            Severity.Error);
                    }
                    else
                    {
                        yield return new TagChange(
                            track.RelativePath,
                            field,
                            values,
                            [],
                            Id,
                            "重複があるが、残す値の判断が要る",
                            Severity.Info);
                    }
                }
            }
        }
    }
}

/// <summary>
/// R-207: 人名に生没年・「姓, 名」順・全大文字が含まれる。
///
/// 辞書で正規形に解決できる値は R-201 / R-202 / R-204 が扱う。ここは**解決できなかった残り**を報告する。
/// 頭字語（<c>USSR</c> 等）と団体名の読点は誤検出になるため除外する
/// （docs/library-baseline-2026-08-03.md）。
/// </summary>
public sealed class PersonNameFormatRule : IInspectionRule
{
    /// <summary>生没年の表記。</summary>
    private static readonly Regex LIFE_SPAN = new(@"\(?\s*1[0-9]{3}\s*[-–]\s*[12][0-9]{3}\s*\)?", RegexOptions.Compiled);

    /// <summary>「姓, 名」順。読点が 1 つだけの場合に限る。</summary>
    private static readonly Regex SURNAME_FIRST = new(@"^[^,]+,\s*[^,]+$", RegexOptions.Compiled);

    /// <summary>全大文字の語。頭字語と区別するため 5 文字以上を対象にする。</summary>
    private static readonly Regex ALL_CAPS_WORD = new(@"\b\p{Lu}{5,}\b", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Id => "R-207";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "人名に生没年・「姓, 名」順・全大文字が含まれる";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        TagField[] fields = [TagField.Artist, TagField.Conductor, TagField.AlbumArtist, TagField.Composer];

        foreach (TrackTags track in context.Tracks)
        {
            foreach (TagField field in fields)
            {
                if (context.IsProtected(track, field))
                {
                    continue;
                }

                foreach (string value in track.GetValues(field))
                {
                    // 団体名は「姓, 名」順の判定対象にしない。読点は列挙であって語順ではない。
                    bool isEnsemble = context.Dictionary.TryResolveEnsemble(value, out _);

                    string[] problems = [.. FindProblems(value, isEnsemble)];

                    if (problems.Length == 0)
                    {
                        continue;
                    }

                    // 辞書で解決できるものは他のルールが正規形を提案する。
                    if (context.Dictionary.TryResolveComposer(value, out _)
                        || context.Dictionary.TryResolvePerson(value, out _)
                        || isEnsemble)
                    {
                        continue;
                    }

                    yield return new TagChange(
                        track.RelativePath,
                        field,
                        track.GetValues(field),
                        [],
                        Id,
                        $"{string.Join(" / ", problems)}。辞書に無いため正規形を決められない",
                        Severity.Warning);
                }
            }
        }
    }

    /// <summary>
    /// 書式上の問題を列挙する。
    /// </summary>
    private static IEnumerable<string> FindProblems(string value, bool isEnsemble)
    {
        if (LIFE_SPAN.IsMatch(value))
        {
            yield return "生没年を含む";
        }

        if (!isEnsemble && SURNAME_FIRST.IsMatch(value))
        {
            yield return "「姓, 名」順";
        }

        if (ALL_CAPS_WORD.IsMatch(value))
        {
            yield return "全大文字";
        }
    }
}

/// <summary>
/// R-208: 人名・団体名が日本語表記（docs/TAGGING_POLICY.md 3.1）。
/// 保護対象（2.3）は除外する。除外しないと配役情報が全件引っかかる。
/// </summary>
public sealed class JapanesePerformerNameRule : IInspectionRule
{
    /// <summary>日本語の文字。</summary>
    private static readonly Regex JAPANESE = new(
        @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]",
        RegexOptions.Compiled);

    /// <inheritdoc />
    public string Id => "R-208";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "日本語表記の人名・団体名";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        TagField[] fields = [TagField.Artist, TagField.Conductor, TagField.AlbumArtist, TagField.Composer];

        foreach (TrackTags track in context.Tracks)
        {
            foreach (TagField field in fields)
            {
                if (context.IsProtected(track, field))
                {
                    continue;
                }

                IReadOnlyList<string> values = track.GetValues(field);

                if (values.Count != 1 || !JAPANESE.IsMatch(values[0]))
                {
                    continue;
                }

                string value = values[0];

                if (TryResolveLatin(context, field, value, out string? canonical))
                {
                    yield return new TagChange(
                        track.RelativePath,
                        field,
                        values,
                        [canonical!],
                        Id,
                        $"ラテン文字の正規形は「{canonical}」",
                        Severity.Warning);

                    continue;
                }

                yield return new TagChange(
                    track.RelativePath,
                    field,
                    values,
                    [],
                    Id,
                    "辞書に対応するラテン文字表記が無い",
                    Severity.Info);
            }
        }
    }

    /// <summary>
    /// 日本語表記からラテン文字の正規形を引く。
    /// </summary>
    private static bool TryResolveLatin(InspectionContext context, TagField field, string value, out string? canonical)
    {
        canonical = null;

        if (field == TagField.Composer && context.Dictionary.TryResolveComposer(value, out string composer))
        {
            canonical = composer;
            return true;
        }

        if (context.Dictionary.TryResolvePerson(value, out PersonEntry person))
        {
            canonical = person.Canonical;
            return true;
        }

        if (context.Dictionary.TryResolveEnsemble(value, out EnsembleEntry ensemble) && ensemble.Canonical is not null)
        {
            canonical = ensemble.Canonical;
            return true;
        }

        return false;
    }
}
