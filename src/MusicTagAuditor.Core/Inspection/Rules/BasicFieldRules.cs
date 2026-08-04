using System.Globalization;
using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection.Rules;

/// <summary>R-101: <c>genre</c> が <c>Classic</c> 以外。</summary>
public sealed class GenreNotClassicRule : IInspectionRule
{
    /// <summary>全ファイルに設定する値。<c>Classical</c> ではない（docs/TAGGING_POLICY.md 2.4）。</summary>
    public const string CANONICAL_GENRE = "Classic";

    /// <inheritdoc />
    public string Id => "R-101";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "genre が Classic 以外";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Genre);

            if (values.Count == 0 || (values.Count == 1 && values[0] == CANONICAL_GENRE))
            {
                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Genre,
                values,
                [CANONICAL_GENRE],
                Id,
                $"genre は全ファイル {CANONICAL_GENRE} に固定する",
                Severity.Warning);
        }
    }
}

/// <summary>R-102: <c>genre</c> 未設定。</summary>
public sealed class GenreMissingRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-102";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "genre 未設定";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks.Where(track => track.Genre is null))
        {
            yield return new TagChange(
                track.RelativePath,
                TagField.Genre,
                [],
                [GenreNotClassicRule.CANONICAL_GENRE],
                Id,
                $"genre は全ファイル {GenreNotClassicRule.CANONICAL_GENRE} に固定する",
                Severity.Warning);
        }
    }
}

/// <summary>R-103: <c>discnumber</c> 未設定。単一ディスクと判断できる場合のみ補う。</summary>
public sealed class DiscNumberMissingRule : IInspectionRule
{
    /// <summary>単一ディスクのときに設定する値（docs/TAGGING_POLICY.md 2.4）。</summary>
    public const string SINGLE_DISC_VALUE = "1/1";

    /// <inheritdoc />
    public string Id => "R-103";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "discnumber 未設定";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks.Where(track => track.DiscNumber is null))
        {
            // 同じフォルダに 2 枚目以降がある場合は、何番かを機械的には決められない。
            bool multiDisc = context.GetSiblings(track)
                .Select(sibling => sibling.DiscNumber)
                .Where(disc => disc is not null)
                .Any(disc => !IsFirstDisc(disc!));

            if (multiDisc)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.DiscNumber,
                    [],
                    [],
                    Id,
                    "同一フォルダに複数ディスクがあるため、何枚目かを特定できない",
                    Severity.Info);

                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.DiscNumber,
                [],
                [SINGLE_DISC_VALUE],
                Id,
                "同一フォルダに他のディスクが無いため単一ディスクと判断",
                Severity.Warning);
        }
    }

    /// <summary>
    /// ディスク番号が 1 枚目を指すかを判定する。
    /// </summary>
    private static bool IsFirstDisc(string value)
    {
        string number = value.Split('/', StringSplitOptions.TrimEntries)[0];

        return number == "1";
    }
}

/// <summary>R-104: <c>date</c> が 4 桁でない。</summary>
public sealed class DateFormatRule : IInspectionRule
{
    /// <summary>4 桁の年。</summary>
    private static readonly Regex FOUR_DIGIT_YEAR = new(@"^\d{4}$", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Id => "R-104";

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "date が4桁でない";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            string? date = track.Date;

            if (date is null || FOUR_DIGIT_YEAR.IsMatch(date))
            {
                continue;
            }

            int? year = InspectionContext.GetRecordingYear(track);

            if (year is null)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Date,
                    track.GetValues(TagField.Date),
                    [],
                    Id,
                    "年を読み取れない",
                    Severity.Info);

                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Date,
                track.GetValues(TagField.Date),
                [year.Value.ToString(CultureInfo.InvariantCulture)],
                Id,
                $"「{date}」から年を抽出",
                Severity.Warning);
        }
    }
}

/// <summary>R-105: <c>date</c> 未設定。自動修正しない。</summary>
public sealed class DateMissingRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-105";

    /// <inheritdoc />
    public Severity Severity => Severity.Info;

    /// <inheritdoc />
    public string Description => "date 未設定";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks.Where(track => track.Date is null))
        {
            yield return new TagChange(
                track.RelativePath,
                TagField.Date,
                [],
                [],
                Id,
                "録音年は CD 実物の確認が要る。date は団体名の時代判定にも使うため重要",
                Severity.Info);
        }
    }
}
