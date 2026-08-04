using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection.Rules;

/// <summary>
/// R-301: 楽語・人名の typo（辞書の正規表現に一致）。
///
/// **照合は正規表現で行う**（docs/TAGGING_POLICY.md 5.4）。完全一致で辞書を引くと
/// <c>Finale- Allgro molto</c> と <c>Finale: Allgro molto</c> の区切り文字違いを取りこぼす。
/// **団体名は対象にしない。**<c>Münchener Bach-Chor</c> の -ener は正しい表記である。
/// </summary>
public sealed class TypoRule : IInspectionRule
{
    /// <summary>誤記を探すフィールド。人名・団体名フィールドは含めない。</summary>
    private static readonly TagField[] TARGET_FIELDS = [TagField.Title, TagField.Album];

    /// <inheritdoc />
    public string Id => "R-301";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "楽語・人名の typo";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            foreach (TagField field in TARGET_FIELDS)
            {
                IReadOnlyList<string> values = track.GetValues(field);

                if (values.Count != 1)
                {
                    continue;
                }

                IReadOnlyList<TypoEntry> typos = context.Dictionary.FindTypos(values[0]);

                if (typos.Count == 0)
                {
                    continue;
                }

                string fixedValue = context.Dictionary.ApplyTypoFixes(values[0]);

                yield return new TagChange(
                    track.RelativePath,
                    field,
                    values,
                    [fixedValue],
                    Id,
                    $"辞書の誤記に一致（{string.Join(" / ", typos.Select(typo => $"{typo.Pattern} → {typo.Replacement}"))}）",
                    Severity.Error);
            }
        }
    }
}

/// <summary>R-302: <c>title</c> に拡張子が含まれる。</summary>
public sealed class TitleContainsExtensionRule : IInspectionRule
{
    /// <summary>末尾の拡張子。対象拡張子は docs/SPEC.md 11章。</summary>
    private static readonly Regex EXTENSION = new(
        @"\.(m4a|flac|mp3|aif|aiff|wav|ogg|opus)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public string Id => "R-302";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "title に拡張子が含まれる";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Title);

            if (values.Count != 1 || !EXTENSION.IsMatch(values[0]))
            {
                continue;
            }

            string trimmed = EXTENSION.Replace(values[0], string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Title,
                values,
                [trimmed],
                Id,
                "曲名の末尾に拡張子が残っている",
                Severity.Error);
        }
    }
}

/// <summary>
/// R-303: <c>title</c> がプレースホルダ（<c>Track04</c>、<c>ショス15 - 01</c> 等）。
///
/// ファイル名から補完できる場合は修正値を出すが、**既定ではチェックしない**。
/// ファイル名には Windows で使えない文字の代替が混じっており、日本語のものもある。
/// 1 件ずつ人間が見て決める（docs/TAGGING_POLICY.md 7.4）。
/// </summary>
public sealed class PlaceholderTitleRule : IInspectionRule
{
    /// <inheritdoc />
    public string Id => "R-303";

    /// <inheritdoc />
    public Severity Severity => Severity.Error;

    /// <inheritdoc />
    public string Description => "title がプレースホルダ";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Title);

            if (values.Count != 1 || !PlaceholderTitle.IsPlaceholder(values[0]))
            {
                continue;
            }

            string? suggestion = PlaceholderTitle.SuggestFromFileName(track.RelativePath);

            if (suggestion is null)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Title,
                    values,
                    [],
                    Id,
                    "ファイル名もプレースホルダのため補完できない。CD 実物の確認が要る",
                    Severity.Info);

                continue;
            }

            // 修正値は出すが Severity.Info なので既定ではチェックされない。
            yield return new TagChange(
                track.RelativePath,
                TagField.Title,
                values,
                [suggestion],
                Id,
                $"ファイル名「{Path.GetFileName(track.RelativePath)}」から補完（先頭のトラック番号のみ除去）",
                Severity.Info);
        }
    }
}

/// <summary>
/// R-304: 曲名中のウムラウト欠落（docs/TAGGING_POLICY.md 6.3）。
///
/// **既定で無効。** 有効にすると誤検出が増える（docs/SPEC.md 6.2）。
/// **自動修正はしない。** CD 原盤が意図的に ASCII 表記である可能性があり、
/// 人名・団体名（3.3）とは事情が違う。正しい綴りの候補は根拠に出すだけにする。
/// </summary>
public sealed class DiacriticMissingRule : IInspectionRule
{
    /// <summary>ルール ID。既定で無効なので、有効化する側から参照できるよう公開する。</summary>
    public const string RULE_ID = "R-304";

    /// <inheritdoc />
    public string Id => RULE_ID;

    /// <inheritdoc />
    public Severity Severity => Severity.Info;

    /// <inheritdoc />
    public string Description => "曲名中の発音区別符号の欠落";

    /// <inheritdoc />
    public bool IsEnabledByDefault => false;

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Title);

            if (values.Count != 1)
            {
                continue;
            }

            IReadOnlyList<DiacriticCandidate> found = DiacriticCandidates.Find(values[0]);

            if (found.Count == 0)
            {
                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Title,
                values,
                [],
                Id,
                $"「{DiacriticCandidates.Suggest(values[0], found)}」の可能性がある"
                + $"（{string.Join(" / ", found.Select(candidate => $"{candidate.Ascii}→{candidate.Correct}"))}）。"
                + " 原盤が意図的に ASCII 表記の場合もあるため自動修正しない",
                Severity.Info);
        }
    }
}
