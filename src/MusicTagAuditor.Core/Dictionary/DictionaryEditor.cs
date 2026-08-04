using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 辞書のエントリ種別。検査結果から辞書に足すとき、どの表に入れるかを指す。
/// </summary>
public enum DictionaryCategory
{
    /// <summary>作曲家。</summary>
    Composer,

    /// <summary>指揮者・ソリスト。</summary>
    Person,

    /// <summary>演奏団体。</summary>
    Ensemble,
}

/// <summary>
/// 辞書を編集する。
///
/// <see cref="TagDictionary"/> は不変なので、すべての操作は**新しい辞書を返す**。
/// 元の辞書を書き換えないため、検証に落ちたら単に破棄すればよい。
/// </summary>
public static class DictionaryEditor
{
    /// <summary>
    /// 値が日本語表記かを判定する。別名を <c>aliases</c> と <c>aliasesJa</c> のどちらに入れるかの判断に使う。
    /// </summary>
    /// <param name="value">判定する値。</param>
    /// <returns>仮名または漢字を含めば true。</returns>
    public static bool IsJapanese(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // ひらがな・カタカナ（U+3040〜U+30FF）、CJK 統合漢字（U+4E00〜U+9FFF）、
        // 半角カタカナ（U+FF66〜U+FF9F）の範囲で判定する。
        return value.Any(c =>
            c is >= '぀' and <= 'ヿ'
            or >= '一' and <= '鿿'
            or >= 'ｦ' and <= 'ﾟ');
    }

    /// <summary>
    /// タグのフィールドから、辞書のどの種別に入れるべきかを推定する。
    ///
    /// <c>artist</c> / <c>conductor</c> は人物、<c>albumartist</c> は団体、<c>composer</c> は作曲家。
    /// docs/TAGGING_POLICY.md 2.1 のフィールド定義そのままである。
    /// </summary>
    /// <param name="field">検出されたフィールド。</param>
    /// <returns>推定した種別。</returns>
    public static DictionaryCategory SuggestCategory(TagField field)
    {
        return field switch
        {
            TagField.Composer => DictionaryCategory.Composer,
            TagField.AlbumArtist => DictionaryCategory.Ensemble,
            _ => DictionaryCategory.Person,
        };
    }

    /// <summary>
    /// 指定した種別の正規形を列挙する。既存エントリに別名を足すときの選択肢になる。
    /// </summary>
    /// <param name="dictionary">対象の辞書。</param>
    /// <param name="category">種別。</param>
    /// <returns>正規形の一覧（昇順）。</returns>
    public static IReadOnlyList<string> ListCanonicals(TagDictionary dictionary, DictionaryCategory category)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        IEnumerable<string> names = category switch
        {
            DictionaryCategory.Composer => (dictionary.Composers ?? []).Select(entry => entry.Canonical),
            DictionaryCategory.Person => (dictionary.Persons ?? []).Select(entry => entry.Canonical),
            _ => (dictionary.Ensembles ?? []).SelectMany(GetEnsembleNames),
        };

        return [.. names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// 既存エントリに別名を足す。日本語表記は <c>aliasesJa</c> 側に入れる。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <param name="category">種別。</param>
    /// <param name="canonical">足す先の正規形。</param>
    /// <param name="alias">足す別名。</param>
    /// <returns>別名を足した新しい辞書。</returns>
    /// <exception cref="InvalidOperationException">正規形に該当するエントリが無い場合。</exception>
    public static TagDictionary AddAlias(
        TagDictionary dictionary,
        DictionaryCategory category,
        string canonical,
        string alias)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        return category switch
        {
            DictionaryCategory.Composer => AddComposerAlias(dictionary, canonical, alias),
            DictionaryCategory.Person => AddPersonAlias(dictionary, canonical, alias),
            _ => AddEnsembleAlias(dictionary, canonical, alias),
        };
    }

    /// <summary>
    /// 作曲家に別名を足す。
    /// </summary>
    private static TagDictionary AddComposerAlias(TagDictionary dictionary, string canonical, string alias)
    {
        ComposerEntry[] composers = [.. dictionary.Composers ?? []];
        int index = Array.FindIndex(composers, entry => entry.Canonical == canonical);

        if (index < 0)
        {
            throw new InvalidOperationException($"作曲家「{canonical}」が辞書にありません。");
        }

        ComposerEntry target = composers[index];

        composers[index] = IsJapanese(alias)
            ? target with { AliasesJa = Append(target.AliasesJa, alias) }
            : target with { Aliases = Append(target.Aliases, alias) };

        return dictionary with { Composers = composers };
    }

    /// <summary>
    /// 人物に別名を足す。
    /// </summary>
    private static TagDictionary AddPersonAlias(TagDictionary dictionary, string canonical, string alias)
    {
        PersonEntry[] persons = [.. dictionary.Persons ?? []];
        int index = Array.FindIndex(persons, entry => entry.Canonical == canonical);

        if (index < 0)
        {
            throw new InvalidOperationException($"人物「{canonical}」が辞書にありません。");
        }

        PersonEntry target = persons[index];

        persons[index] = IsJapanese(alias)
            ? target with { AliasesJa = Append(target.AliasesJa, alias) }
            : target with { Aliases = Append(target.Aliases, alias) };

        return dictionary with { Persons = persons };
    }

    /// <summary>
    /// 団体に別名を足す。時代分割エントリの場合、正規形はどの時代のものでも指定できる。
    /// </summary>
    private static TagDictionary AddEnsembleAlias(TagDictionary dictionary, string canonical, string alias)
    {
        EnsembleEntry[] ensembles = [.. dictionary.Ensembles ?? []];
        int index = Array.FindIndex(ensembles, entry => GetEnsembleNames(entry).Contains(canonical, StringComparer.Ordinal));

        if (index < 0)
        {
            throw new InvalidOperationException($"団体「{canonical}」が辞書にありません。");
        }

        EnsembleEntry target = ensembles[index];

        ensembles[index] = IsJapanese(alias)
            ? target with { AliasesJa = Append(target.AliasesJa, alias) }
            : target with { Aliases = Append(target.Aliases, alias) };

        return dictionary with { Ensembles = ensembles };
    }

    /// <summary>
    /// 作曲家を新規に足す。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <param name="canonical">正規形。</param>
    /// <param name="aliases">別名。日本語表記は自動で <c>aliasesJa</c> に振り分ける。</param>
    /// <returns>新しい辞書。</returns>
    public static TagDictionary AddComposer(TagDictionary dictionary, string canonical, IEnumerable<string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        (string[] latin, string[] japanese) = SplitByScript(aliases, canonical);

        ComposerEntry entry = new()
        {
            Canonical = canonical,
            Aliases = latin,
            AliasesJa = japanese,
        };

        return dictionary with { Composers = [.. dictionary.Composers ?? [], entry] };
    }

    /// <summary>
    /// 人物を新規に足す。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <param name="canonical">正規形。</param>
    /// <param name="roles">担う役割。</param>
    /// <param name="aliases">別名。</param>
    /// <returns>新しい辞書。</returns>
    public static TagDictionary AddPerson(
        TagDictionary dictionary,
        string canonical,
        IEnumerable<PersonRole> roles,
        IEnumerable<string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        (string[] latin, string[] japanese) = SplitByScript(aliases, canonical);

        PersonEntry entry = new()
        {
            Canonical = canonical,
            Roles = [.. roles.Select(role => role.ToString()).Distinct(StringComparer.Ordinal)],
            Aliases = latin,
            AliasesJa = japanese,
        };

        return dictionary with { Persons = [.. dictionary.Persons ?? [], entry] };
    }

    /// <summary>
    /// 団体を新規に足す。時代分割は行わない単純なエントリとして作る。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <param name="entityId">実体 ID。**同一性はこれで判断する**（docs/TAGGING_POLICY.md 5.3.1）。</param>
    /// <param name="canonical">正規形。</param>
    /// <param name="aliases">別名。</param>
    /// <returns>新しい辞書。</returns>
    public static TagDictionary AddEnsemble(
        TagDictionary dictionary,
        string entityId,
        string canonical,
        IEnumerable<string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        (string[] latin, string[] japanese) = SplitByScript(aliases, canonical);

        EnsembleEntry entry = new()
        {
            EntityId = entityId,
            Canonical = canonical,
            Aliases = latin,
            AliasesJa = japanese,
        };

        return dictionary with { Ensembles = [.. dictionary.Ensembles ?? [], entry] };
    }

    /// <summary>
    /// 正規形から実体 ID の候補を作る。利用者が毎回考えずに済むようにするための補助。
    /// </summary>
    /// <param name="dictionary">既存の辞書。ID の重複を避けるために使う。</param>
    /// <param name="canonical">正規形。</param>
    /// <returns>既存と重複しない実体 ID。</returns>
    public static string SuggestEntityId(TagDictionary dictionary, string canonical)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        string baseId = new(
            (canonical ?? string.Empty)
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray());

        baseId = string.Join('-', baseId.Split('-', StringSplitOptions.RemoveEmptyEntries));

        if (baseId.Length == 0)
        {
            baseId = "ensemble";
        }

        HashSet<string> used = new(
            (dictionary.Ensembles ?? []).Select(entry => entry.EntityId),
            StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(baseId))
        {
            return baseId;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseId}-{suffix}";

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 値がいずれかの索引で既に引ける状態かを調べる。
    /// 二重登録を防ぐため、追加ダイアログで先に確認する。
    /// </summary>
    /// <param name="index">現在の索引。</param>
    /// <param name="value">確認する値。</param>
    /// <param name="owner">見つかった場合の登録先。</param>
    /// <returns>既に引けるなら true。</returns>
    public static bool IsAlreadyKnown(DictionaryIndex index, string value, out string owner)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (index.TryResolveComposer(value, out string composer))
        {
            owner = $"{DictionaryValidator.CATEGORY_COMPOSER}「{composer}」";
            return true;
        }

        if (index.TryResolvePerson(value, out PersonEntry person))
        {
            owner = $"{DictionaryValidator.CATEGORY_PERSON}「{person.Canonical}」";
            return true;
        }

        if (index.TryResolveEnsemble(value, out EnsembleEntry ensemble))
        {
            owner = $"{DictionaryValidator.CATEGORY_ENSEMBLE}「{GetEnsembleNames(ensemble).FirstOrDefault() ?? ensemble.EntityId}」";
            return true;
        }

        owner = string.Empty;
        return false;
    }

    /// <summary>
    /// 団体エントリが持つ正規形をすべて返す。時代分割エントリは複数持つ。
    /// </summary>
    /// <param name="ensemble">対象の団体。</param>
    /// <returns>正規形の一覧。</returns>
    public static IReadOnlyList<string> GetEnsembleNames(EnsembleEntry ensemble)
    {
        ArgumentNullException.ThrowIfNull(ensemble);

        List<string> names = [];

        if (!string.IsNullOrWhiteSpace(ensemble.Canonical))
        {
            names.Add(ensemble.Canonical);
        }

        names.AddRange((ensemble.Eras ?? []).Select(era => era.Canonical).Where(name => !string.IsNullOrWhiteSpace(name)));

        return names;
    }

    /// <summary>
    /// 別名をラテン文字と日本語に振り分ける。正規形と同じ値、および重複は落とす。
    /// </summary>
    private static (string[] Latin, string[] Japanese) SplitByScript(IEnumerable<string>? aliases, string canonical)
    {
        string canonicalKey = NormalizationKey.Create(canonical);

        string[] cleaned =
        [
            .. (aliases ?? [])
                .Select(alias => alias.Trim())
                .Where(alias => alias.Length > 0 && NormalizationKey.Create(alias) != canonicalKey)
                .Distinct(StringComparer.Ordinal),
        ];

        return ([.. cleaned.Where(alias => !IsJapanese(alias))], [.. cleaned.Where(IsJapanese)]);
    }

    /// <summary>
    /// 別名の一覧に 1 件足す。既にあれば足さない。
    /// </summary>
    private static IReadOnlyList<string> Append(IReadOnlyList<string>? values, string value)
    {
        IReadOnlyList<string> current = values ?? [];

        return current.Contains(value, StringComparer.Ordinal) ? current : [.. current, value];
    }
}
