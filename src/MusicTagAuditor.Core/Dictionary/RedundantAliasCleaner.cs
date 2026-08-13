using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 取り除いた冗長な別名 1 件。
/// </summary>
/// <param name="Category">種別（作曲家 / 人物 / 団体 / 作品）。</param>
/// <param name="Owner">別名を持っていたエントリ。作品は <c>作曲家: 作品名</c>。</param>
/// <param name="Name">取り除いた別名。</param>
/// <param name="KeptName">同じ正規化キーで残るほうの名前。</param>
public sealed record RemovedAlias(string Category, string Owner, string Name, string KeptName)
{
    /// <summary>一覧表示用の 1 行。</summary>
    public string Summary => $"{Category}「{Owner}」の別名「{Name}」を削除（「{KeptName}」と同じキー）";
}

/// <summary>
/// 正規化キーが同じエントリ内の別の名前と衝突する別名を取り除く。
///
/// <see cref="DictionaryIndex"/> は <c>TryAdd</c> で索引を作るため、**先に現れた名前が勝ち、
/// 後から来た同じキーの別名は黙って捨てられる。** つまりこの種の別名は書いても引けず、
/// 消しても引ける範囲は 1 文字も変わらない。<see cref="DictionaryValidator"/> が警告し続ける
/// だけの存在になる。
///
/// **残すのは常に先に現れたほう。** 索引が採用しているのがそちらであり、順序を変えると
/// <see cref="DictionarySuggester"/> が候補に併記する表記まで変わる。掃除の前後で画面の
/// 見え方を変えないため、判断の基準を索引と一致させておく。
///
/// **正規形と時代分割は決して落とさない。** これらは別名ではなく、消せば引けなくなる。
/// 同じ正規形を持つ区分（改名して元に戻った団体）は、キーを埋めるだけで削除対象にしない。
/// </summary>
public static class RedundantAliasCleaner
{
    /// <summary>
    /// 辞書全体から冗長な別名を取り除く。元の辞書は書き換えない。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <returns>掃除した辞書と、取り除いた別名。</returns>
    public static (TagDictionary Dictionary, IReadOnlyList<RemovedAlias> Removed) Clean(TagDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        List<RemovedAlias> removed = [];

        List<ComposerEntry> composers = [];

        foreach (ComposerEntry entry in dictionary.Composers ?? [])
        {
            Dictionary<string, string> owners = Seed([entry.Canonical]);

            composers.Add(entry with
            {
                Aliases = Keep(entry.Aliases, owners, removed, DictionaryValidator.CATEGORY_COMPOSER, entry.Canonical),
                AliasesJa = Keep(entry.AliasesJa, owners, removed, DictionaryValidator.CATEGORY_COMPOSER, entry.Canonical),
            });
        }

        List<PersonEntry> persons = [];

        foreach (PersonEntry entry in dictionary.Persons ?? [])
        {
            Dictionary<string, string> owners = Seed([entry.Canonical]);

            persons.Add(entry with
            {
                Aliases = Keep(entry.Aliases, owners, removed, DictionaryValidator.CATEGORY_PERSON, entry.Canonical),
                AliasesJa = Keep(entry.AliasesJa, owners, removed, DictionaryValidator.CATEGORY_PERSON, entry.Canonical),
            });
        }

        List<EnsembleEntry> ensembles = [];

        foreach (EnsembleEntry entry in dictionary.Ensembles ?? [])
        {
            // 団体は正規形が複数ありうる（時代分割）。**索引と同じ順**で先客にする。
            // 索引は時代分割を先に読むため、同じキーの区分と正規形があれば区分のほうが残る。
            Dictionary<string, string> owners = Seed(
                [.. (entry.Eras ?? []).Select(era => era.Canonical), .. entry.Canonical is null ? [] : new[] { entry.Canonical }]);

            string label = DictionaryEditor.GetEnsembleNames(entry).FirstOrDefault() ?? entry.EntityId;

            ensembles.Add(entry with
            {
                Aliases = Keep(entry.Aliases, owners, removed, DictionaryValidator.CATEGORY_ENSEMBLE, label),
                AliasesJa = Keep(entry.AliasesJa, owners, removed, DictionaryValidator.CATEGORY_ENSEMBLE, label),
            });
        }

        (IReadOnlyList<WorkEntry> works, IReadOnlyList<RemovedAlias> removedFromWorks) = CleanWorks(dictionary.Works);

        removed.AddRange(removedFromWorks);

        return (
            dictionary with
            {
                Composers = composers,
                Persons = persons,
                Ensembles = ensembles,
                Works = works,
            },
            removed);
    }

    /// <summary>
    /// 作品だけを掃除する。
    ///
    /// 作品エントリの取り込み（<c>AlbumProbe --import-works</c>）は <c>works</c> 以外に触らないため、
    /// 辞書全体を通す <see cref="Clean"/> は使えない。
    /// </summary>
    /// <param name="works">元の作品。</param>
    /// <returns>掃除した作品と、取り除いた別名。</returns>
    public static (IReadOnlyList<WorkEntry> Works, IReadOnlyList<RemovedAlias> Removed) CleanWorks(
        IReadOnlyList<WorkEntry>? works)
    {
        List<RemovedAlias> removed = [];
        List<WorkEntry> cleaned = [];

        foreach (WorkEntry entry in works ?? [])
        {
            Dictionary<string, string> owners = Seed([entry.Canonical]);
            string label = DictionaryValidator.DescribeWork(entry);

            cleaned.Add(entry with
            {
                Aliases = Keep(entry.Aliases, owners, removed, DictionaryValidator.CATEGORY_WORK, label),
                AliasesJa = Keep(entry.AliasesJa, owners, removed, DictionaryValidator.CATEGORY_WORK, label),
            });
        }

        return (cleaned, removed);
    }

    /// <summary>
    /// 消せない名前（正規形・時代分割の正規形）をキーの先客として並べる。
    /// </summary>
    private static Dictionary<string, string> Seed(IEnumerable<string> names)
    {
        Dictionary<string, string> owners = new(StringComparer.Ordinal);

        foreach (string name in names)
        {
            string key = NormalizationKey.Create(name);

            if (key.Length > 0)
            {
                _ = owners.TryAdd(key, name);
            }
        }

        return owners;
    }

    /// <summary>
    /// まだ使われていないキーの別名だけを残す。落としたものは <paramref name="removed"/> に記録する。
    ///
    /// **キーが空になる別名（空文字・記号だけ）は残す。** 索引に載らないのは同じだが、
    /// 衝突しているわけではないので掃除の対象にしない。こちらは検証が別の警告で報せる。
    /// </summary>
    private static IReadOnlyList<string> Keep(
        IReadOnlyList<string>? values,
        Dictionary<string, string> owners,
        List<RemovedAlias> removed,
        string category,
        string owner)
    {
        List<string> kept = [];

        foreach (string value in values ?? [])
        {
            string key = NormalizationKey.Create(value);

            if (key.Length == 0)
            {
                kept.Add(value);

                continue;
            }

            if (owners.TryGetValue(key, out string? existing))
            {
                removed.Add(new RemovedAlias(category, owner, value, existing));

                continue;
            }

            owners[key] = value;
            kept.Add(value);
        }

        return kept;
    }
}
