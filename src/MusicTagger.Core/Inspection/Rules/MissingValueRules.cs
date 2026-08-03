using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Inspection.Rules;

/// <summary>
/// R-401: <c>composer</c> 未設定。
///
/// 実ライブラリの 26 件は <see cref="ComposerFinder"/> の 3 経路で全件が決まる。
/// </summary>
public sealed class ComposerMissingRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-401";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "composer 未設定";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            if (track.GetValues(TagField.Composer).Count > 0)
            {
                continue;
            }

            ComposerHit? hit = ComposerFinder.Find(track, context);

            if (hit is null)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Composer,
                    [],
                    [],
                    Id,
                    "作曲家を特定できない。CD 実物の確認が要る",
                    Severity.Info);

                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Composer,
                [],
                [hit.Canonical],
                Id,
                hit.Source,
                Severity.Error);
        }
    }
}

/// <summary>
/// R-402: <c>conductor</c> 未設定。
///
/// **指揮者が居ないのが正しい録音がある。** 室内楽・独奏・指揮者を置かない合奏団では
/// <c>conductor</c> は空が正しい（docs/TAGGING_POLICY.md 2.2）。実ライブラリでは
/// I Musici 12 件・Smetana Quartet 10 件・オルガン独奏 4 件がこれに当たり、検出対象にしない。
///
/// <c>artist</c> が辞書の指揮者なら、その値を <c>conductor</c> にも入れる。2.2 が
/// 「指揮者がいる録音では artist が誰であっても conductor に指揮者を必ず入れる」としているため。
/// </summary>
public sealed class ConductorMissingRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-402";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "conductor 未設定";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            if (track.GetValues(TagField.Conductor).Count > 0)
            {
                continue;
            }

            IReadOnlyList<string> artists = track.GetValues(TagField.Artist);
            string? artist = artists.Count == 1 ? artists[0] : null;

            if (artist is not null && IsConductorlessPerformance(artist, context))
            {
                continue;
            }

            if (artist is not null
                && context.Dictionary.TryResolvePerson(artist, out PersonEntry person)
                && DictionaryIndex.HasRole(person, PersonRole.Conductor))
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Conductor,
                    [],
                    [person.Canonical],
                    Id,
                    $"artist が指揮者「{person.Canonical}」。指揮者がいる録音では conductor にも必ず入れる",
                    Severity.Error);

                continue;
            }

            ConductorHit? hit = ConductorFinder.Find(track, context);

            if (hit is not null)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Conductor,
                    [],
                    [hit.Canonical],
                    Id,
                    hit.Source,
                    Severity.Error);

                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Conductor,
                [],
                [],
                Id,
                "指揮者を特定できない。CD 実物の確認が要る",
                Severity.Info);
        }
    }

    /// <summary>
    /// 指揮者が居ないのが正しい演奏かを判定する（docs/TAGGING_POLICY.md 2.2）。
    ///
    /// <c>artist</c> が「指揮者を置かない団体」または指揮者の役割を持たない人物（ソリスト）なら、
    /// <c>conductor</c> が空なのは正しい状態である。
    /// </summary>
    private static bool IsConductorlessPerformance(string artist, InspectionContext context)
    {
        if (context.Dictionary.TryResolveEnsemble(artist, out EnsembleEntry ensemble))
        {
            return ensemble.NoConductor;
        }

        if (context.Dictionary.TryResolvePerson(artist, out PersonEntry person))
        {
            return !DictionaryIndex.HasRole(person, PersonRole.Conductor);
        }

        return false;
    }
}

/// <summary>
/// R-403: 文字化け（Shift-JIS の誤解釈）。
///
/// 復元すると「アーティスト情報なし」「トラック 1」等になり、**実質的にタグ未設定**である
/// （docs/TAGGING_POLICY.md 6.6）。値としての意味を持たないので、修正案は
/// 「値を消す」にする。誤った値で埋めるより空欄のほうが後から対処できる（7.4）。
///
/// **既定ではチェックしない。** 消すか再入力するかは人間が決める。
/// </summary>
public sealed class MojibakeRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-403";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "文字化け（Shift-JIS の誤解釈）";

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

                if (values.Count != 1 || !MojibakeDetector.TryDecode(values[0], out string? decoded))
                {
                    continue;
                }

                yield return new TagChange(
                    track.RelativePath,
                    field,
                    values,
                    [],
                    Id,
                    $"Shift-JIS を誤解釈した状態。復元すると「{decoded}」で、実質的にタグ未設定。"
                    + " 消すか、CD 実物を確認して入れ直す",
                    Severity.Info,
                    HoldReason.None,
                    ClearsValue: true);
            }
        }
    }
}
