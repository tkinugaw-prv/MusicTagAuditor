using System.Diagnostics;
using MusicTagAuditor.Core.Inspection.Rules;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection;

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
    /// <param name="rules">
    /// 実行するルール。null または空なら既定の一式を使う。
    ///
    /// **空を「ルール無し」として受け入れてはならない。** DI コンテナは
    /// <c>IEnumerable&lt;T&gt;</c> を要求されると、T が未登録でも既定値ではなく空のコレクションを
    /// 注入する。これを素通しすると、検査が常に 0 件を返すのに例外も出ない状態になる。
    /// ルールが 1 つも無いエンジンに正当な用途は無いので、既定へ寄せる。
    /// </param>
    public InspectionEngine(IEnumerable<IInspectionRule>? rules = null)
    {
        IInspectionRule[] provided = [.. rules ?? []];

        _rules = [.. (provided.Length > 0 ? provided : CreateDefaultRules())
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)];
    }

    /// <summary>実行するルールの数。構成が意図どおりかの確認に使う。</summary>
    public int RuleCount => _rules.Count;

    /// <summary>
    /// docs/SPEC.md 6.1 のルール一式を作る。段階 7 で全ルールが揃った。
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
            new TypoRule(),
            new TitleContainsExtensionRule(),
            new PlaceholderTitleRule(),
            new DiacriticMissingRule(),
            new ComposerMissingRule(),
            new ConductorMissingRule(),
            new MojibakeRule(),
            new AlbumNameCollisionRule(),
            new JapaneseAlbumNameRule(),
            new MovementNumberStyleRule(),
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
            // 既定で無効なルールは、利用者が明示的に選んだときだけ動かす（docs/SPEC.md 6.2）。
            if (!rule.IsEnabledByDefault && !context.Options.IsEnabled(rule.Id))
            {
                continue;
            }

            TagChange[] changes = [.. rule.Inspect(context)
                .OrderBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.Field)];

            results.Add(new RuleResult(rule.Id, rule.Severity, rule.Description, changes));
        }

        stopwatch.Stop();

        return new InspectionResult(results, stopwatch.Elapsed);
    }
}
