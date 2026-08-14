using System.Text.RegularExpressions;
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

    /// <summary>作品の種別名。</summary>
    public const string CATEGORY_WORK = "作品";

    /// <summary>個別例外の種別名。</summary>
    public const string CATEGORY_OVERRIDE = "個別例外";

    /// <summary>録音年の形（docs/TAGGING_POLICY.md 2.4）。R-104 が直すのと同じ 4 桁。</summary>
    private static readonly Regex FOUR_DIGIT_YEAR = new(@"^\d{4}$", RegexOptions.Compiled);

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
        ValidateWorks(dictionary, issues);
        ValidateAlbumOverrides(dictionary, issues);
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

            // **時代分割の正規形どうしは畳んでから照合する。** 改名したあと元の名前に戻った団体は、
            // 同じ正規形が 2 つの区分に現れる（既定辞書の uk-philharmonia は 1964 年までと 1977 年以降が
            // どちらも Philharmonia Orchestra）。これは重複した別名ではなく、消す先も無い。
            // 畳まないと「直しようのない警告」が居座り、辞書を掃除しきっても 0 件にならない。
            IEnumerable<string> names = eras.Select(era => era.Canonical)
                .DistinctBy(NormalizationKey.Create, StringComparer.Ordinal)
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
    /// 作品エントリを検証する（docs/SPEC.md 7.4）。
    ///
    /// **自然キーは <c>composer</c> + <c>canonical</c>。** 同じ組を 2 つ作ると索引は先勝ちになり、
    /// 後から書いたほうは黙って捨てられる。エイリアスの衝突も同じ作曲家の中でだけ起きる。
    /// </summary>
    private static void ValidateWorks(TagDictionary dictionary, List<DictionaryIssue> issues)
    {
        // 作曲家の正規形。作品の composer がここに無いと、検査時にキーが一致せず引けない。
        HashSet<string> composers = new(
            (dictionary.Composers ?? []).Select(composer => composer.Canonical),
            StringComparer.Ordinal);

        HashSet<string> naturalKeys = new(StringComparer.Ordinal);

        // 「作曲家 + 別名の正規化キー」→ 最初に使った作品。
        Dictionary<string, string> aliasOwners = [];

        foreach (WorkEntry work in dictionary.Works ?? [])
        {
            string label = DescribeWork(work);

            if (string.IsNullOrWhiteSpace(work.Canonical))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_WORK, label, "作品名が空です。"));
            }

            if (string.IsNullOrWhiteSpace(work.Composer))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_WORK, label, "作曲家が空です。作曲家を鍵に引くため必須です。"));

                continue;
            }

            if (!composers.Contains(work.Composer))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    CATEGORY_WORK,
                    label,
                    $"作曲家「{work.Composer}」が作曲家の正規形と一致しません。この作品は引けません。"));
            }

            if (string.IsNullOrWhiteSpace(work.Canonical))
            {
                continue;
            }

            if (!naturalKeys.Add($"{work.Composer}\u0001{work.Canonical}"))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    CATEGORY_WORK,
                    label,
                    $"「{work.Composer}」の「{work.Canonical}」が重複しています。"
                    + " 1 作品 1 エントリにまとめてください（版で分けないこと）。"));
            }

            foreach (string name in AllNames(work.Canonical, work.Aliases, work.AliasesJa))
            {
                string key = NormalizationKey.Create(name);

                if (key.Length == 0)
                {
                    issues.Add(new DictionaryIssue(
                        DictionaryIssueSeverity.Warning, CATEGORY_WORK, label, "空の別名があります。無視されます。"));

                    continue;
                }

                string scoped = $"{work.Composer}\u0001{key}";

                if (aliasOwners.TryGetValue(scoped, out string? existing))
                {
                    issues.Add(new DictionaryIssue(
                        existing == work.Canonical ? DictionaryIssueSeverity.Warning : DictionaryIssueSeverity.Error,
                        CATEGORY_WORK,
                        label,
                        existing == work.Canonical
                            ? $"「{name}」は同じエントリ内で重複しています。"
                            : $"「{name}」は同じ作曲家の「{existing}」が既に使っています。"
                                + " 後から書いたほうは索引に載らず、登録しても効きません。"));

                    continue;
                }

                aliasOwners[scoped] = work.Canonical;
            }
        }
    }

    /// <summary>
    /// 作品を問題の対象として名指しする文字列を作る。
    ///
    /// **作曲家を必ず添える。** 作品の自然キーは <c>composer</c> + <c>canonical</c> であり、
    /// 「Symphony No. 7」だけでは誰の第 7 番なのか読めない。番号で呼ぶ作品は作曲家をまたいで
    /// 何件も並ぶため、一覧のどの行を直せばよいのかが分からなくなる。
    ///
    /// 形は辞書タブの一覧の見出し（<c>作曲家: 作品名</c>）にそろえる。警告の文言をそのまま
    /// 絞り込み欄に入れれば当該の行に辿り着ける。
    ///
    /// **公開しているのは、作品を名指しする形をこの 1 箇所に閉じ込めるため。**
    /// <see cref="RedundantAliasCleaner"/> も同じ形で対象を示す必要がある。別々に組み立てると、
    /// 検証と掃除で違う名前が出て、同じ作品の話だと読み取れなくなる。
    /// </summary>
    /// <param name="work">対象の作品。</param>
    /// <returns>一覧の見出しと同じ形の名前。</returns>
    public static string DescribeWork(WorkEntry work)
    {
        ArgumentNullException.ThrowIfNull(work);

        string canonical = string.IsNullOrWhiteSpace(work.Canonical) ? "(空欄)" : work.Canonical;

        return string.IsNullOrWhiteSpace(work.Composer) ? canonical : $"{work.Composer}: {canonical}";
    }

    /// <summary>
    /// 個別例外を検証する（docs/SPEC.md 7.4.5）。
    /// </summary>
    private static void ValidateAlbumOverrides(TagDictionary dictionary, List<DictionaryIssue> issues)
    {
        foreach (AlbumOverrideEntry entry in dictionary.AlbumOverrides ?? [])
        {
            string label = string.IsNullOrWhiteSpace(entry.Folder) ? "(空欄)" : entry.Folder;

            if (string.IsNullOrWhiteSpace(entry.Folder))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error, CATEGORY_OVERRIDE, label, "フォルダが空です。"));

                continue;
            }

            if (!entry.Exclude
                && string.IsNullOrWhiteSpace(entry.WorkName)
                && string.IsNullOrWhiteSpace(entry.Composer)
                && string.IsNullOrWhiteSpace(entry.Date))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning,
                    CATEGORY_OVERRIDE,
                    label,
                    "対象外にも作品名の指定にもなっていません。この例外は何もしません。"));
            }

            // 4 桁でない年はアルバム名にそのまま入ってしまう（3.5 の {date} は録音年 4 桁）。
            // R-104 が ISO 形式のタグを直すのと同じ形に、辞書の側でも揃える。
            if (!string.IsNullOrWhiteSpace(entry.Date) && !FOUR_DIGIT_YEAR.IsMatch(entry.Date))
            {
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Error,
                    CATEGORY_OVERRIDE,
                    label,
                    $"年（date）「{entry.Date}」が 4 桁ではありません。"));
            }

            if (string.IsNullOrWhiteSpace(entry.Note))
            {
                // 理由の書いていない例外は、後から消してよいか判断できなくなる。
                issues.Add(new DictionaryIssue(
                    DictionaryIssueSeverity.Warning, CATEGORY_OVERRIDE, label, "理由（note）が空です。"));
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
