using System.Collections.Frozen;
using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 団体名の解決結果。
/// </summary>
public enum EnsembleResolution
{
    /// <summary>辞書に無い。</summary>
    Unknown,

    /// <summary>正規形が決まった。</summary>
    Resolved,

    /// <summary>
    /// 時代分割の対象だが録音年が不明なため決められない（<c>HOLD_ERA_UNKNOWN</c>）。
    /// docs/TAGGING_POLICY.md 7.5。<c>date</c> が埋まった時点で自動的に再判定できる。
    /// </summary>
    HoldEraUnknown,
}

/// <summary>
/// 辞書を正規化キーで引けるようにした索引。
/// </summary>
public sealed class DictionaryIndex
{
    /// <summary>
    /// 作品を引くキーで、作曲家と手がかりを繋ぐ区切り。
    /// タグの値に現れない制御文字を選ぶ。区切りが値に含まれると別のキーが同一視される。
    /// </summary>
    private const string WORK_KEY_SEPARATOR = "\u0001";

    /// <summary>元の辞書。</summary>
    private readonly TagDictionary _dictionary;

    /// <summary>正規化キー → 作曲家の正規形。</summary>
    private readonly FrozenDictionary<string, string> _composerByKey;

    /// <summary>正規化キー → 人物。</summary>
    private readonly FrozenDictionary<string, PersonEntry> _personByKey;

    /// <summary>正規化キー → 団体。</summary>
    private readonly FrozenDictionary<string, EnsembleEntry> _ensembleByKey;

    /// <summary>作曲家の姓（正規化キー）。R-203 / R-204 の判定に使う。</summary>
    private readonly FrozenSet<string> _composerSurnameKeys;

    /// <summary>保護対象の <c>albumartist</c>（正規化キー）。</summary>
    private readonly FrozenSet<string> _protectedKeys;

    /// <summary>
    /// 「作曲家の正規形 + 手がかりの正規化キー」→ 作品。
    ///
    /// **エイリアス単独では引かない**（docs/SPEC.md 7.4.3 手順3）。<c>Symphony No.5</c> は
    /// 作曲家が違えば別の作品であり、作曲家で絞らないと R-501 が検出している衝突を再生産する。
    /// </summary>
    private readonly FrozenDictionary<string, WorkEntry> _workByKey;

    /// <summary>フォルダ（正規化した相対パス）→ 個別例外。</summary>
    private readonly FrozenDictionary<string, IReadOnlyList<AlbumOverrideEntry>> _overridesByFolder;

    /// <summary>typo 検出用にコンパイル済みの正規表現。</summary>
    private readonly IReadOnlyList<(Regex Pattern, TypoEntry Entry)> _typos;

    /// <summary>
    /// 辞書から索引を作る。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    public DictionaryIndex(TagDictionary dictionary)
    {
        _dictionary = dictionary;

        Dictionary<string, string> composerByKey = [];
        HashSet<string> surnameKeys = [];

        foreach (ComposerEntry composer in dictionary.Composers ?? [])
        {
            foreach (string name in Names(composer.Canonical, composer.Aliases, composer.AliasesJa))
            {
                composerByKey.TryAdd(NormalizationKey.Create(name), composer.Canonical);
            }

            // 姓だけの記述（`Brahms`）を検出できるようにする。フルネームの最後の語を姓とみなす。
            string[] parts = composer.Canonical.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                surnameKeys.Add(NormalizationKey.Create(parts[^1]));
            }

            // 1 語のエイリアス（`Shostakovich`、`Siberius`）も姓として扱う。
            foreach (string alias in Safe(composer.Aliases).Where(alias => !alias.Contains(' ', StringComparison.Ordinal)))
            {
                surnameKeys.Add(NormalizationKey.Create(alias));
            }
        }

        Dictionary<string, PersonEntry> personByKey = [];

        foreach (PersonEntry person in dictionary.Persons ?? [])
        {
            foreach (string name in Names(person.Canonical, person.Aliases, person.AliasesJa))
            {
                personByKey.TryAdd(NormalizationKey.Create(name), person);
            }
        }

        Dictionary<string, EnsembleEntry> ensembleByKey = [];

        foreach (EnsembleEntry ensemble in dictionary.Ensembles ?? [])
        {
            IEnumerable<string> names = Safe(ensemble.Eras)
                .Select(era => era.Canonical)
                .Concat(ensemble.Canonical is null ? [] : [ensemble.Canonical])
                .Concat(Safe(ensemble.Aliases))
                .Concat(Safe(ensemble.AliasesJa));

            foreach (string name in names)
            {
                ensembleByKey.TryAdd(NormalizationKey.Create(name), ensemble);
            }
        }

        _composerByKey = composerByKey.ToFrozenDictionary(StringComparer.Ordinal);
        _personByKey = personByKey.ToFrozenDictionary(StringComparer.Ordinal);
        _ensembleByKey = ensembleByKey.ToFrozenDictionary(StringComparer.Ordinal);
        _composerSurnameKeys = surnameKeys.Where(key => key.Length > 0).ToFrozenSet(StringComparer.Ordinal);

        _protectedKeys = Safe(dictionary.ProtectedAlbumArtists)
            .Select(NormalizationKey.Create)
            .Where(key => key.Length > 0)
            .ToFrozenSet(StringComparer.Ordinal);

        Dictionary<string, WorkEntry> workByKey = [];

        foreach (WorkEntry work in dictionary.Works ?? [])
        {
            if (string.IsNullOrWhiteSpace(work.Composer) || string.IsNullOrWhiteSpace(work.Canonical))
            {
                continue;
            }

            foreach (string name in Names(work.Canonical, work.Aliases, work.AliasesJa))
            {
                workByKey.TryAdd(WorkKey(work.Composer, name), work);
            }
        }

        _workByKey = workByKey.ToFrozenDictionary(StringComparer.Ordinal);

        _overridesByFolder = Safe(dictionary.AlbumOverrides)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Folder))
            .GroupBy(entry => NormalizeFolder(entry.Folder), StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(
                group => group.Key,
                group => (IReadOnlyList<AlbumOverrideEntry>)[.. group],
                StringComparer.OrdinalIgnoreCase);

        _typos =
        [
            .. Safe(dictionary.Typos)
                .Where(typo => IsValidPattern(typo.Pattern))
                .Select(typo => (new Regex(typo.Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), typo)),
        ];
    }

    /// <summary>元の辞書。</summary>
    public TagDictionary Dictionary => _dictionary;

    /// <summary>
    /// 値が保護対象の <c>albumartist</c> かを判定する（docs/TAGGING_POLICY.md 2.3）。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <returns>保護対象なら true。</returns>
    public bool IsProtectedAlbumArtist(string? value)
    {
        return _protectedKeys.Contains(NormalizationKey.Create(value));
    }

    /// <summary>
    /// 作曲家の正規形を引く。
    /// </summary>
    /// <param name="value">タグの値。</param>
    /// <param name="canonical">見つかった正規形。</param>
    /// <returns>辞書にあれば true。</returns>
    public bool TryResolveComposer(string? value, out string canonical)
    {
        return _composerByKey.TryGetValue(NormalizationKey.Create(value), out canonical!);
    }

    /// <summary>
    /// 人物を引く。
    /// </summary>
    /// <param name="value">タグの値。</param>
    /// <param name="person">見つかった人物。</param>
    /// <returns>辞書にあれば true。</returns>
    public bool TryResolvePerson(string? value, out PersonEntry person)
    {
        return _personByKey.TryGetValue(NormalizationKey.Create(value), out person!);
    }

    /// <summary>
    /// 団体を引く。
    /// </summary>
    /// <param name="value">タグの値。</param>
    /// <param name="ensemble">見つかった団体。</param>
    /// <returns>辞書にあれば true。</returns>
    public bool TryResolveEnsemble(string? value, out EnsembleEntry ensemble)
    {
        return _ensembleByKey.TryGetValue(NormalizationKey.Create(value), out ensemble!);
    }

    /// <summary>
    /// 団体名を録音年で解決する。
    ///
    /// 収録時点の名称を採用するため、時代分割エントリでは録音年が要る（docs/TAGGING_POLICY.md 3.1.2 規則3）。
    /// 年が分からない場合は保留する。誤った値で埋めるより保留のほうが後から対処できる。
    /// </summary>
    /// <param name="ensemble">対象の団体。</param>
    /// <param name="recordingYear">録音年。不明なら null。</param>
    /// <param name="canonical">決まった正規形。保留・不明の場合は null。</param>
    /// <returns>解決結果。</returns>
    public static EnsembleResolution ResolveCanonical(EnsembleEntry ensemble, int? recordingYear, out string? canonical)
    {
        canonical = null;

        if (Safe(ensemble.Eras).Count == 0 || ensemble.NoEraSplit)
        {
            canonical = ensemble.Canonical;
            return canonical is null ? EnsembleResolution.Unknown : EnsembleResolution.Resolved;
        }

        if (recordingYear is null)
        {
            return EnsembleResolution.HoldEraUnknown;
        }

        foreach (EnsembleEra era in Safe(ensemble.Eras))
        {
            bool afterFrom = era.From is null || recordingYear >= era.From;
            bool beforeUntil = era.Until is null || recordingYear < era.Until;

            if (afterFrom && beforeUntil)
            {
                canonical = era.Canonical;
                return EnsembleResolution.Resolved;
            }
        }

        return EnsembleResolution.HoldEraUnknown;
    }

    /// <summary>
    /// 値に作曲家名が含まれているかを判定する（R-203 / R-204）。
    ///
    /// **団体名や人物名として辞書に載っている値は対象外にする。**
    /// これをしないと `Smetana Quartet` や `Münchener Bach-Chor` を誤検出する
    /// （docs/library-baseline-2026-08-03.md）。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <param name="composerCanonical">見つかった作曲家の正規形。</param>
    /// <returns>作曲家名が含まれていれば true。</returns>
    public bool ContainsComposerName(string? value, out string? composerCanonical)
    {
        composerCanonical = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // 団体名・人物名として既知なら、作曲家の姓を含んでいても作曲家ではない。
        if (TryResolveEnsemble(value, out _) || TryResolvePerson(value, out _))
        {
            return false;
        }

        // 値そのものが作曲家名の場合。
        if (TryResolveComposer(value, out string wholeMatch))
        {
            composerCanonical = wholeMatch;
            return true;
        }

        // 語ごとに姓と照合する。部分一致だと `Bach` が `Bach-Chor` に当たってしまう。
        foreach (string token in SplitIntoTokens(value))
        {
            string key = NormalizationKey.Create(token);

            if (key.Length > 0 && _composerSurnameKeys.Contains(key) && _composerByKey.TryGetValue(key, out string? canonical))
            {
                composerCanonical = canonical;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 作曲家と手がかりから作品を引く（docs/SPEC.md 7.4.3）。
    ///
    /// **作曲家で絞ってから引く。** <c>Symphony No.5</c> は作曲家が違えば別の作品である。
    /// </summary>
    /// <param name="composerCanonical">作曲家の正規形。</param>
    /// <param name="hint"><c>album</c> の値やフォルダ名。</param>
    /// <param name="work">見つかった作品。</param>
    /// <returns>辞書にあれば true。</returns>
    public bool TryResolveWork(string? composerCanonical, string? hint, out WorkEntry work)
    {
        work = null!;

        if (string.IsNullOrWhiteSpace(composerCanonical) || string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        return _workByKey.TryGetValue(WorkKey(composerCanonical, hint), out work!);
    }

    /// <summary>
    /// フォルダに対する個別例外を引く（docs/SPEC.md 7.4.5）。
    ///
    /// <c>disc</c> を持たない項目はそのフォルダの全ディスクに効く。**ディスク指定のほうを優先する。**
    /// 「フォルダ全体を対象外にしつつ 1 枚だけ名前を決める」という書き方を許すため。
    /// </summary>
    /// <param name="folder">ライブラリルートからの相対フォルダ。</param>
    /// <param name="disc">ディスク番号。</param>
    /// <param name="entry">見つかった例外。</param>
    /// <returns>該当があれば true。</returns>
    public bool TryResolveAlbumOverride(string? folder, int disc, out AlbumOverrideEntry entry)
    {
        entry = null!;

        if (!_overridesByFolder.TryGetValue(NormalizeFolder(folder), out IReadOnlyList<AlbumOverrideEntry>? candidates))
        {
            return false;
        }

        entry = candidates.FirstOrDefault(candidate => candidate.Disc == disc)
            ?? candidates.FirstOrDefault(candidate => candidate.Disc is null)!;

        return entry is not null;
    }

    /// <summary>
    /// 作品を引くためのキーを作る。作曲家の正規形と手がかりの正規化キーを組にする。
    /// </summary>
    private static string WorkKey(string composerCanonical, string name)
    {
        // 区切りはタグの値に現れない制御文字にする。区切りが値に含まれると別のキーが同一視される。
        return composerCanonical + WORK_KEY_SEPARATOR + NormalizationKey.Create(name);
    }

    /// <summary>
    /// フォルダのパスを比較用にそろえる。区切りと前後の空白だけを吸収する。
    ///
    /// **正規化キーは使わない。** 記号を落とすとフォルダの区別が付かなくなる。
    /// </summary>
    private static string NormalizeFolder(string? folder)
    {
        return (folder ?? string.Empty)
            .Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// 値に含まれる typo を検出する。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <returns>一致した typo。</returns>
    public IReadOnlyList<TypoEntry> FindTypos(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return [.. _typos.Where(typo => typo.Pattern.IsMatch(value)).Select(typo => typo.Entry)];
    }

    /// <summary>
    /// 値に含まれる typo をすべて置換する。
    /// </summary>
    /// <param name="value">元の値。</param>
    /// <returns>置換後の値。</returns>
    public string ApplyTypoFixes(string value)
    {
        string result = value;

        foreach ((Regex pattern, TypoEntry entry) in _typos)
        {
            result = pattern.Replace(result, entry.Replacement);
        }

        return result;
    }

    /// <summary>
    /// 文字列を語に分割する。区切りは英数字以外。
    /// </summary>
    private static IEnumerable<string> SplitIntoTokens(string value)
    {
        return value.Split(
            [' ', '\t', ',', ';', ':', '/', '-', '(', ')', '[', ']', '&', '.', '　', '、', '・'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// 正規形とエイリアスをまとめて返す。
    /// </summary>
    private static IEnumerable<string> Names(string canonical, IReadOnlyList<string>? aliases, IReadOnlyList<string>? aliasesJa)
    {
        return new[] { canonical }.Concat(Safe(aliases)).Concat(Safe(aliasesJa));
    }

    /// <summary>
    /// null を空列として扱う。辞書は手で編集するため、項目が欠けていても落ちないようにする。
    /// </summary>
    private static IReadOnlyList<T> Safe<T>(IReadOnlyList<T>? values)
    {
        return values ?? [];
    }

    /// <summary>
    /// 人物の役割を安全に判定する。
    /// </summary>
    /// <param name="person">対象の人物。</param>
    /// <param name="role">確認する役割。</param>
    /// <returns>その役割を持つなら true。</returns>
    public static bool HasRole(PersonEntry person, PersonRole role)
    {
        return Safe(person.Roles).Contains(role.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 正規表現として妥当かを確認する。辞書は手で編集するため、壊れた式で落ちないようにする。
    /// </summary>
    /// <param name="pattern">確認するパターン。</param>
    /// <returns>妥当なら true。</returns>
    public static bool IsValidPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            _ = Regex.Match(string.Empty, pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
