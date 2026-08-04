using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection.Rules;

/// <summary>R-201: <c>composer</c> が辞書の正規形と不一致。</summary>
public sealed class ComposerNotCanonicalRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-201";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "composer が辞書の正規形と不一致";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Composer);

            if (values.Count == 0)
            {
                continue;
            }

            if (values.Count == 1 && context.Dictionary.TryResolveComposer(values[0], out string canonical))
            {
                if (canonical == values[0])
                {
                    continue;
                }

                yield return new TagChange(
                    track.RelativePath,
                    TagField.Composer,
                    values,
                    [canonical],
                    Id,
                    $"辞書の正規形は「{canonical}」",
                    Severity.Error);

                continue;
            }

            // 辞書に無い値は修正値を決められない。空欄のままのほうが後から対処できる。
            yield return new TagChange(
                track.RelativePath,
                TagField.Composer,
                values,
                [],
                Id,
                "辞書に無い値。正規形を辞書に登録してから再スキャンする",
                Severity.Info);
        }
    }
}

/// <summary>
/// R-202: <c>artist</c> / <c>conductor</c> / <c>albumartist</c> が辞書の正規形と不一致。
///
/// 時代分割の対象になる団体は R-209 が扱うため、ここでは触らない。
/// 作曲家名が入っている値は R-203 / R-204 が扱う。
/// </summary>
public sealed class PerformerNotCanonicalRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-202";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "artist / conductor / albumartist が辞書の正規形と不一致";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        TagField[] fields = [TagField.Artist, TagField.Conductor, TagField.AlbumArtist];

        foreach (TrackTags track in context.Tracks)
        {
            foreach (TagField field in fields)
            {
                if (context.IsProtected(track, field))
                {
                    continue;
                }

                IReadOnlyList<string> values = track.GetValues(field);

                if (values.Count != 1)
                {
                    continue;
                }

                string value = values[0];

                // 作曲家名が入っている場合は R-203 / R-204 の領分。
                if (context.Dictionary.ContainsComposerName(value, out _))
                {
                    continue;
                }

                if (context.Dictionary.TryResolvePerson(value, out PersonEntry person))
                {
                    if (person.Canonical != value)
                    {
                        yield return new TagChange(
                            track.RelativePath,
                            field,
                            values,
                            [person.Canonical],
                            Id,
                            $"辞書の正規形は「{person.Canonical}」",
                            Severity.Error);
                    }

                    continue;
                }

                if (context.Dictionary.TryResolveEnsemble(value, out EnsembleEntry ensemble))
                {
                    // 時代分割の対象は R-209 に任せる。
                    if ((ensemble.Eras?.Count ?? 0) > 0 && !ensemble.NoEraSplit)
                    {
                        continue;
                    }

                    if (ensemble.Canonical is not null && ensemble.Canonical != value)
                    {
                        yield return new TagChange(
                            track.RelativePath,
                            field,
                            values,
                            [ensemble.Canonical],
                            Id,
                            $"辞書の正規形は「{ensemble.Canonical}」",
                            Severity.Error);
                    }

                    continue;
                }

                yield return new TagChange(
                    track.RelativePath,
                    field,
                    values,
                    [],
                    Id,
                    "辞書に無い値。正規形を辞書に登録してから再スキャンする",
                    Severity.Info);
            }
        }
    }
}

/// <summary>
/// R-209: <c>albumartist</c> が収録時点の団体名と不一致（docs/TAGGING_POLICY.md 5.3.1）。
///
/// **同一性は実体 ID で判断する。名前の類似で束ねない。**
/// <c>date</c> が空欄で時代分割の対象なら、書き換えずに保留する（7.5 の <c>HOLD_ERA_UNKNOWN</c>）。
/// </summary>
public sealed class EnsembleEraRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-209";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "albumartist が収録時点の団体名と不一致";

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

            if (values.Count != 1 || !context.Dictionary.TryResolveEnsemble(values[0], out EnsembleEntry ensemble))
            {
                continue;
            }

            if ((ensemble.Eras?.Count ?? 0) == 0 || ensemble.NoEraSplit)
            {
                continue;
            }

            int? year = InspectionContext.GetRecordingYear(track);

            EnsembleResolution resolution = DictionaryIndex.ResolveCanonical(ensemble, year, out string? canonical);

            if (resolution == EnsembleResolution.HoldEraUnknown)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.AlbumArtist,
                    values,
                    [],
                    Id,
                    $"実体「{ensemble.EntityId}」は録音年で名称が変わる。date が空欄のため保留",
                    Severity.Error,
                    HoldReason.EraUnknown);

                continue;
            }

            if (canonical is null || canonical == values[0])
            {
                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.AlbumArtist,
                values,
                [canonical],
                Id,
                $"{year} 年の録音。実体「{ensemble.EntityId}」の収録時点の名称は「{canonical}」",
                Severity.Error);
        }
    }
}
