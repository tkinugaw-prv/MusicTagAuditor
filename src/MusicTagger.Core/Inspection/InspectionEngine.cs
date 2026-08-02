using System.Diagnostics;
using MusicTagger.Core.Inspection.Rules;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Inspection;

/// <summary>
/// 検査ルールをまとめて実行する。
/// </summary>
public sealed class InspectionEngine
{
    /// <summary>実行するルール。</summary>
    private readonly IReadOnlyList<IInspectionRule> _rules;

    /// <summary>
    /// エンジンを初期化する。
    /// </summary>
    /// <param name="rules">実行するルール。省略時は既定の一式。</param>
    public InspectionEngine(IEnumerable<IInspectionRule>? rules = null)
    {
        _rules = [.. (rules ?? CreateDefaultRules()).OrderBy(rule => rule.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// 段階 3 の時点で有効なルール一式を作る。
    /// R-3xx / R-4xx / R-5xx は段階 7 で追加する（docs/SPEC.md 12章）。
    /// </summary>
    /// <returns>ルールの一覧。</returns>
    public static IReadOnlyList<IInspectionRule> CreateDefaultRules()
    {
        return
        [
            new GenreNotClassicRule(),
            new GenreMissingRule(),
            new DiscNumberMissingRule(),
            new DateFormatRule(),
            new DateMissingRule(),
            new ComposerNotCanonicalRule(),
            new PerformerNotCanonicalRule(),
            new ComposerInArtistRule(),
            new ComposerInAlbumArtistRule(),
            new SemicolonValueRule(),
            new DuplicateConcatenationRule(),
            new PersonNameFormatRule(),
            new JapanesePerformerNameRule(),
            new EnsembleEraRule(),
        ];
    }

    /// <summary>
    /// すべてのルールを実行する。
    /// </summary>
    /// <param name="context">ライブラリ全体の文脈。</param>
    /// <returns>検査結果。</returns>
    public InspectionResult Inspect(InspectionContext context)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        List<RuleResult> results = [];

        foreach (IInspectionRule rule in _rules)
        {
            TagChange[] changes = [.. rule.Inspect(context)
                .OrderBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Field)];

            results.Add(new RuleResult(rule.Id, rule.Severity, rule.Description, changes));
        }

        stopwatch.Stop();

        return new InspectionResult(results, stopwatch.Elapsed);
    }
}
