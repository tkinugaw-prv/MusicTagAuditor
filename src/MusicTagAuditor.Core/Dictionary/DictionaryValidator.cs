using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 辞書の検証で見つかった問題の重さ。
/// </summary>
public enum DictionaryIssueSeverity
{
    /// <summary>保存させない。放置すると辞書が意図どおり効かない。</summary>
    Error,

    /// <summary>保存はできるが確認が要る。</summary>
    Warning,
}

/// <summary>
/// 辞書の検証で見つかった問題 1 件。
/// </summary>
/// <param name="Severity">重さ。</param>
/// <param name="Category">種別（作曲家 / 人物 / 団体 / 誤記 / 保護対象）。</param>
/// <param name="Target">対象のエントリ名。</param>
/// <param name="Message">内容。</param>
public sealed record DictionaryIssue(
    DictionaryIssueSeverity Severity,
    string Category,
    string Target,
    string Message)
{
    /// <summary>一覧表示用の 1 行。</summary>
    public string Summary => $"[{(Severity == DictionaryIssueSeverity.Error ? "エラー" : "警告")}] {Category}「{Target}」— {Message}";
}

/// <summary>
/// 辞書を保存する前に検証する。
///
/// **最重要は正規化キーの衝突検出。**<see cref="DictionaryIndex"/> は <c>TryAdd</c> で索引を作るため、
/// 既存エントリと同じ正規化キーを持つ別名を足しても**例外は出ず、黙って捨てられる**。
/// 「登録したのに効かない」という気づきにくい状態になるので、保存前にここで止める。
/// </summary>
public static class DictionaryValidator
{
    /// <summary>作曲家の種別名。</summary>
    public const string CATEGORY_COMPOSER = "作曲家";

    /// <summary>人物の種別名。</summary>
    public const string CATEGORY_PERSON = "人物";

    /// <summary>団体の種別名。</summary>
    public const string CATEGORY_ENSEMBLE = "団体";

    /// <summary>誤記の種別名。</summary>
    public const string CATEGORY_TYPO = "誤記";

    /// <summary>保護対象の種別名。</summary>
    public const string CATEGORY_PROTECTED = "保護対象";

    /// <summary>
    /// 辞書全体を検証する。
    /// </summary>
    /// <param name="dictionary">検証する辞書。</param>
    /// <returns>見つかった問題。問題が無ければ空。</returns>
    public static IReadOnlyList<DictionaryIssue> Validate(TagDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        List<DictionaryIssue> issues = [];

        // 正規化キー → 最初に使ったエントリ。種別ごとに別の索引になるため、衝突も種別ごとに見る。
        Dictionary<string, string> composerKeys = [];
        Dictionary<string, string> personKeys = [];
        Dictionary<string, string> ensembleKeys = [];

        ValidateComposers(dictionary, issues, composerKeys);
        ValidatePersons(dictionary, issues, personKeys);
        ValidateEnsembles(dictionary, issues, ensembleKeys);
        ValidateTypos(dictionary, issues);
        ValidateProtected(dictionary, issues);
        ValidateCrossCategory(issues, composerKeys, personKeys, ensembleKeys);

        return issues;
    }

    /// <summary>
    /// 保存を止めるべき問題があるかを判定する。
    /// </summary>
    /// <param name="issues">検証結果。</param>
    /// <returns>エラーが 1 件でもあれば true。</returns>
    public static bool HasError(IEnumerable<DictionaryIssue> issues)
    {
        return issues.Any(issue => issue.Severity == DictionaryIssueSeverity.Error);
    }

    /// <summary>
    /// 作曲家を検証する。
    /// </summary>
    private static void ValidateComposers(
        TagDictionary dictionary,
        List<DictionaryIssue> issues,
        Dictionary<string, string> keys)
    {
        foreach (ComposerEntry composer in dictionary.Composers ?? [])
        {
            if (string.IsNullOrWhiteSpace(composer.Canonical))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_COMPOSER, "(空欄)", "正規形が空です。"));

                continue;
            }

            AddKeys(issues, keys, CATEGORY_COMPOSER, composer.Canonical, AllNames(composer.Canonical, composer.Aliases, composer.AliasesJa));
        }
    }

    /// <summary>
    /// 人物を検証する。
    /// </summary>
    private static void ValidatePersons(
        TagDictionary dictionary,
        List<DictionaryIssue> issues,
        Dictionary<string, string> keys)
    {
        string[] knownRoles = [.. Enum.GetNames<PersonRole>()];

        foreach (PersonEntry person in dictionary.Persons ?? [])
        {
            if (string.IsNullOrWhiteSpace(person.Canonical))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_PERSON, "(空欄)", "正規形が空です。"));

                continue;
            }

            IReadOnlyList<string> roles = person.Roles ?? [];

            if (roles.Count == 0)
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning,
                    CATEGORY_PERSON,
                    person.Canonical,
                    "役割が空です。指揮者の特定に使われません。"));
            }

            foreach (string role in roles.Where(role => !knownRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    CATEGORY_PERSON,
                    person.Canonical,
                    $"役割「{role}」は未知です。指定できるのは {string.Join(" / ", knownRoles)} です。"));
            }

            AddKeys(issues, keys, CATEGORY_PERSON, person.Canonical, AllNames(person.Canonical, person.Aliases, person.AliasesJa));
        }
    }

    /// <summary>
    /// 団体を検証する。時代分割は誤ると片寄せ事故になるため、期間の重なりと隙間も見る。
    /// </summary>
    private static void ValidateEnsembles(
        TagDictionary dictionary,
        List<DictionaryIssue> issues,
        Dictionary<string, string> keys)
    {
        HashSet<string> entityIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (EnsembleEntry ensemble in dictionary.Ensembles ?? [])
        {
            IReadOnlyList<EnsembleEra> eras = ensemble.Eras ?? [];
            string label = ensemble.Canonical ?? eras.FirstOrDefault()?.Canonical ?? ensemble.EntityId;

            if (string.IsNullOrWhiteSpace(ensemble.EntityId))
            {
                // 同一性は実体 ID で判断する（docs/TAGGING_POLICY.md 5.3.1）。空だと判断の根拠が無い。
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_ENSEMBLE, label, "実体 ID が空です。"));
            }
            else if (!entityIds.Add(ensemble.EntityId))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    CATEGORY_ENSEMBLE,
                    label,
                    $"実体 ID「{ensemble.EntityId}」が重複しています。別の団体には別の ID を付けてください。"));
            }

            ValidateEnsembleCanonical(issues, ensemble, eras, label);
            ValidateEras(issues, eras, label);

            IEnumerable<string> names = eras.Select(era => era.Canonical)
                .Concat(ensemble.Canonical is null ? [] : [ensemble.Canonical])
                .Concat(ensemble.Aliases ?? [])
                .Concat(ensemble.AliasesJa ?? []);

            AddKeys(issues, keys, CATEGORY_ENSEMBLE, label, names);
        }
    }

    /// <summary>
    /// 団体の正規形と時代分割の組み合わせを検証する。
    /// </summary>
    private static void ValidateEnsembleCanonical(
        List<DictionaryIssue> issues,
        EnsembleEntry ensemble,
        IReadOnlyList<EnsembleEra> eras,
        string label)
    {
        bool hasCanonical = !string.IsNullOrWhiteSpace(ensemble.Canonical);

        if (!hasCanonical && eras.Count == 0)
        {
            issues.Add(new DictionaryIssue(
                DictionaryIssueSeverity.Error, CATEGORY_ENSEMBLE, label, "正規形も時代分割もありません。"));

            return;
        }

        if (ensemble.NoEraSplit && !hasCanonical)
        {
            issues.Add(new DictionaryIssue(
                DictionaryIssueSeverity.Error,
                CATEGORY_ENSEMBLE,
                label,
                "時代分割しない設定ですが正規形がありません。"));
        }

        if (hasCanonical && eras.Count > 0 && !ensemble.NoEraSplit)
        {
            issues.Add(new DictionaryIssue(
                DictionaryIssueSeverity.Warning,
                CATEGORY_ENSEMBLE,
                label,
                "正規形と時代分割の両方があります。時代分割が優先され、正規形は照合用の別名としてのみ使われます。"));
        }

        foreach (EnsembleEra era in eras.Where(era => string.IsNullOrWhiteSpace(era.Canonical)))
        {
            issues.Add(new DictionaryIssue(
                DictionaryIssueSeverity.Error,
                CATEGORY_ENSEMBLE,
                label,
                $"{FormatPeriod(era)} の正規形が空です。"));
        }
    }

    /// <summary>
    /// 時代分割の期間を検証する。重なりと隙間はいずれも警告に留める。
    ///
    /// 隙間は「その年の録音は保留に落ちる」という意味であり、誤りとは限らない。
    /// 重なりは先に一致した区分が採用されるため、意図と違う値になりうる。
    /// </summary>
    private static void ValidateEras(List<DictionaryIssue> issues, IReadOnlyList<EnsembleEra> eras, string label)
    {
        if (eras.Count < 2)
        {
            return;
        }

        EnsembleEra[] ordered = [.. eras.OrderBy(era => era.From ?? int.MinValue).ThenBy(era => era.Until ?? int.MaxValue)];

        for (int i = 0; i < ordered.Length - 1; i++)
        {
            EnsembleEra current = ordered[i];
            EnsembleEra next = ordered[i + 1];

            int currentEnd = current.Until ?? int.MaxValue;
            int nextStart = next.From ?? int.MinValue;

            if (nextStart < currentEnd)
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning,
                    CATEGORY_ENSEMBLE,
                    label,
                    $"{FormatPeriod(current)} と {FormatPeriod(next)} の期間が重なっています。"
                    + " 先に一致した区分が採用されます。"));

                continue;
            }

            if (nextStart > currentEnd)
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning,
                    CATEGORY_ENSEMBLE,
                    label,
                    $"{FormatPeriod(current)} と {FormatPeriod(next)} の間に隙間があります。"
                    + $" {currentEnd}〜{nextStart - 1} 年の録音は保留になります。"));
            }
        }
    }

    /// <summary>
    /// 誤記の正規表現を検証する。
    /// </summary>
    private static void ValidateTypos(TagDictionary dictionary, List<DictionaryIssue> issues)
    {
        foreach (TypoEntry typo in dictionary.Typos ?? [])
        {
            if (string.IsNullOrWhiteSpace(typo.Pattern))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_TYPO, "(空欄)", "パターンが空です。"));

                continue;
            }

            if (!DictionaryIndex.IsValidPattern(typo.Pattern))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    CATEGORY_TYPO,
                    typo.Pattern,
                    "正規表現として解釈できません。"));

                continue;
            }

            if (string.IsNullOrEmpty(typo.Replacement))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning,
                    CATEGORY_TYPO,
                    typo.Pattern,
                    "置換後が空です。一致した箇所は削除されます。"));
            }
        }
    }

    /// <summary>
    /// 保護対象を検証する。
    /// </summary>
    private static void ValidateProtected(TagDictionary dictionary, List<DictionaryIssue> issues)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (string value in dictionary.ProtectedAlbumArtists ?? [])
        {
            string key = NormalizationKey.Create(value);

            if (key.Length == 0)
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_PROTECTED, "(空欄)", "値が空です。"));

                continue;
            }

            if (!keys.Add(key))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning, CATEGORY_PROTECTED, value, "同じ値が重複しています。"));
            }
        }
    }

    /// <summary>
    /// 種別をまたいだ衝突を検証する。
    ///
    /// <see cref="DictionaryIndex.ContainsComposerName"/> は団体・人物を先に照合するため、
    /// 同じ名前が両方にあると作曲家として扱われなくなる。誤りとは限らないので警告に留める。
    /// </summary>
    private static void ValidateCrossCategory(
        List<DictionaryIssue> issues,
        Dictionary<string, string> composerKeys,
        Dictionary<string, string> personKeys,
        Dictionary<string, string> ensembleKeys)
    {
        ReportOverlap(issues, composerKeys, personKeys, CATEGORY_COMPOSER, CATEGORY_PERSON);
        ReportOverlap(issues, composerKeys, ensembleKeys, CATEGORY_COMPOSER, CATEGORY_ENSEMBLE);
        ReportOverlap(issues, personKeys, ensembleKeys, CATEGORY_PERSON, CATEGORY_ENSEMBLE);
    }

    /// <summary>
    /// 2 つの索引で同じ正規化キーを使っているものを報告する。
    /// </summary>
    private static void ReportOverlap(
        List<DictionaryIssue> issues,
        Dictionary<string, string> left,
        Dictionary<string, string> right,
        string leftCategory,
        string rightCategory)
    {
        foreach ((string key, string leftOwner) in left)
        {
            if (!right.TryGetValue(key, out string? rightOwner))
            {
                continue;
            }

            issues.Add(new DictionaryIssue(
                DictionaryIssueSeverity.Warning,
                leftCategory,
                leftOwner,
                $"{rightCategory}「{rightOwner}」と同じ名前です。照合では {rightCategory} が優先されます。"));
        }
    }

    /// <summary>
    /// 名前を正規化キーに直して索引に足す。既に使われていればエラーとして報告する。
    /// </summary>
    private static void AddKeys(
        List<DictionaryIssue> issues,
        Dictionary<string, string> keys,
        string category,
        string owner,
        IEnumerable<string> names)
    {
        foreach (string name in names)
        {
            string key = NormalizationKey.Create(name);

            if (key.Length == 0)
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning, category, owner, "空の別名があります。無視されます。"));

                continue;
            }

            if (keys.TryGetValue(key, out string? existing))
            {
                if (existing == owner)
                {
                    issues.Add(new DictionaryIssue(
                        DictionaryIssueSeverity.Warning,
                        category,
                        owner,
                        $"「{name}」は同じエントリ内で重複しています。"));

                    continue;
                }

                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    category,
                    owner,
                    $"「{name}」は「{existing}」が既に使っています。"
                    + " 後から書いたほうは索引に載らず、登録しても効きません。"));

                continue;
            }

            keys[key] = owner;
        }
    }

    /// <summary>
    /// 正規形と別名をまとめる。
    /// </summary>
    private static IEnumerable<string> AllNames(string canonical, IReadOnlyList<string>? aliases, IReadOnlyList<string>? aliasesJa)
    {
        return new[] { canonical }.Concat(aliases ?? []).Concat(aliasesJa ?? []);
    }

    /// <summary>
    /// 時代区分の期間を表示用の文字列にする。
    /// </summary>
    private static string FormatPeriod(EnsembleEra era)
    {
        string from = era.From is null ? string.Empty : $"{era.From}";
        string until = era.Until is null ? string.Empty : $"{era.Until}";

        return $"「{era.Canonical}」（{from}〜{until}）";
    }
}
