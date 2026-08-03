using MusicTagger.Core.Normalization;

namespace MusicTagger.Core.Dictionary;

/// <summary>
/// 同梱辞書から取り込める変更の種類。
/// </summary>
public enum DictionaryMergeKind
{
    /// <summary>作曲家のエントリを追加する。</summary>
    AddComposer,

    /// <summary>人物のエントリを追加する。</summary>
    AddPerson,

    /// <summary>団体のエントリを追加する。</summary>
    AddEnsemble,

    /// <summary>誤記のエントリを追加する。</summary>
    AddTypo,

    /// <summary>保護対象の値を追加する。</summary>
    AddProtectedValue,

    /// <summary>既存エントリに別名を追加する。</summary>
    AddAlias,

    /// <summary>団体に「指揮者を置かない」を設定する。</summary>
    EnableNoConductor,

    /// <summary>団体に「時代分割を行わない」を設定する。</summary>
    EnableNoEraSplit,
}

/// <summary>
/// 同梱辞書から取り込める変更 1 件。
/// </summary>
/// <param name="Kind">変更の種類。</param>
/// <param name="Category">種別の表示名。</param>
/// <param name="Target">対象エントリの識別子（正規形 / 実体 ID / パターン / 値）。</param>
/// <param name="Detail">追加される内容。</param>
/// <param name="Label">
/// 表示用の名前。団体は実体 ID で識別するため、それだけでは何の団体か分からない。
/// </param>
public sealed record DictionaryMergeItem(
    DictionaryMergeKind Kind,
    string Category,
    string Target,
    string Detail,
    string Label = "")
{
    /// <summary>
    /// 取り込む対象にするか。
    ///
    /// **既定は取り込む。** ただし利用者が意図的に消したエントリを復活させる可能性があるため、
    /// 1 件ずつ外せるようにしてある（docs/SPEC.md 1章の「適用前に必ず差分を確認できる」）。
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// 表示用の対象名。団体なら <c>I Musici（it-i-musici）</c> のように名前を添える。
    /// </summary>
    public string DisplayName => Label.Length > 0 ? $"{Label}（{Target}）" : Target;

    /// <summary>一覧表示用の 1 行。</summary>
    public string Summary => Kind switch
    {
        DictionaryMergeKind.AddAlias => $"「{DisplayName}」に別名「{Detail}」を追加",
        DictionaryMergeKind.EnableNoConductor => $"「{DisplayName}」を指揮者を置かない団体にする",
        DictionaryMergeKind.EnableNoEraSplit => $"「{DisplayName}」の時代分割を行わない設定にする",
        DictionaryMergeKind.AddTypo => $"{Category}「{Target}」→「{Detail}」を追加",
        DictionaryMergeKind.AddProtectedValue => $"保護対象に「{Target}」を追加",
        _ => $"{Category}「{DisplayName}」を追加",
    };
}

/// <summary>
/// 同梱の既定辞書と利用者辞書を突き合わせ、取り込める差分を作る。
///
/// **利用者辞書は初回起動時にコピーされたきり更新されない。** そのため、
/// 同梱辞書にエントリや項目を足しても既存の利用者には届かない。実際に段階 7 で足した
/// <c>noConductor</c> が届かず、R-402 が 22 件多く検出される状態になっていた。
///
/// **自動では取り込まない。** 段階 5 で辞書からエントリを削除できるようになったため、
/// 黙って差分を当てると利用者が意図的に消したものを復活させてしまう。
/// </summary>
public static class DictionaryMerger
{
    /// <summary>
    /// 取り込める差分を洗い出す。
    /// </summary>
    /// <param name="user">利用者辞書。</param>
    /// <param name="bundled">同梱の既定辞書。</param>
    /// <returns>取り込める差分。無ければ空。</returns>
    public static IReadOnlyList<DictionaryMergeItem> BuildPlan(TagDictionary user, TagDictionary bundled)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(bundled);

        List<DictionaryMergeItem> items = [];

        CompareComposers(user, bundled, items);
        ComparePersons(user, bundled, items);
        CompareEnsembles(user, bundled, items);
        CompareTypos(user, bundled, items);
        CompareProtectedValues(user, bundled, items);

        return items;
    }

    /// <summary>
    /// 選ばれた差分を利用者辞書に取り込む。元の辞書は書き換えない。
    /// </summary>
    /// <param name="user">利用者辞書。</param>
    /// <param name="bundled">同梱の既定辞書。</param>
    /// <param name="items">取り込む差分。</param>
    /// <returns>取り込んだ結果。</returns>
    public static TagDictionary Apply(
        TagDictionary user,
        TagDictionary bundled,
        IEnumerable<DictionaryMergeItem> items)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(items);

        List<ComposerEntry> composers = [.. user.Composers ?? []];
        List<PersonEntry> persons = [.. user.Persons ?? []];
        List<EnsembleEntry> ensembles = [.. user.Ensembles ?? []];
        List<TypoEntry> typos = [.. user.Typos ?? []];
        List<string> protectedValues = [.. user.ProtectedAlbumArtists ?? []];

        foreach (DictionaryMergeItem item in items.Where(item => item.IsSelected))
        {
            switch (item.Kind)
            {
                case DictionaryMergeKind.AddComposer:
                    AddIfFound(composers, bundled.Composers, entry => Key(entry.Canonical) == Key(item.Target));
                    break;

                case DictionaryMergeKind.AddPerson:
                    AddIfFound(persons, bundled.Persons, entry => Key(entry.Canonical) == Key(item.Target));
                    break;

                case DictionaryMergeKind.AddEnsemble:
                    AddIfFound(ensembles, bundled.Ensembles, entry => SameEntityId(entry.EntityId, item.Target));
                    break;

                case DictionaryMergeKind.AddTypo:
                    AddIfFound(typos, bundled.Typos, entry => entry.Pattern == item.Target);
                    break;

                case DictionaryMergeKind.AddProtectedValue:
                    protectedValues.Add(item.Target);
                    break;

                case DictionaryMergeKind.AddAlias:
                    ApplyAlias(composers, persons, ensembles, item);
                    break;

                case DictionaryMergeKind.EnableNoConductor:
                    UpdateEnsemble(ensembles, item.Target, entry => entry with { NoConductor = true });
                    break;

                case DictionaryMergeKind.EnableNoEraSplit:
                    UpdateEnsemble(ensembles, item.Target, entry => entry with { NoEraSplit = true });
                    break;

                default:
                    break;
            }
        }

        // 版は同梱側に合わせる。次に比べるときの手がかりになる。
        return user with
        {
            Version = bundled.Version,
            Composers = composers,
            Persons = persons,
            Ensembles = ensembles,
            Typos = typos,
            ProtectedAlbumArtists = protectedValues,
        };
    }

    /// <summary>
    /// 作曲家を突き合わせる。
    /// </summary>
    private static void CompareComposers(TagDictionary user, TagDictionary bundled, List<DictionaryMergeItem> items)
    {
        Dictionary<string, ComposerEntry> byKey = (user.Composers ?? [])
            .GroupBy(entry => Key(entry.Canonical))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (ComposerEntry entry in bundled.Composers ?? [])
        {
            if (!byKey.TryGetValue(Key(entry.Canonical), out ComposerEntry? existing))
            {
                items.Add(new DictionaryMergeItem(
                    DictionaryMergeKind.AddComposer,
                    DictionaryValidator.CATEGORY_COMPOSER,
                    entry.Canonical,
                    string.Empty));

                continue;
            }

            AddMissingAliases(
                items,
                DictionaryValidator.CATEGORY_COMPOSER,
                existing.Canonical,
                Names(existing.Canonical, existing.Aliases, existing.AliasesJa),
                Names(entry.Canonical, entry.Aliases, entry.AliasesJa));
        }
    }

    /// <summary>
    /// 人物を突き合わせる。
    /// </summary>
    private static void ComparePersons(TagDictionary user, TagDictionary bundled, List<DictionaryMergeItem> items)
    {
        Dictionary<string, PersonEntry> byKey = (user.Persons ?? [])
            .GroupBy(entry => Key(entry.Canonical))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (PersonEntry entry in bundled.Persons ?? [])
        {
            if (!byKey.TryGetValue(Key(entry.Canonical), out PersonEntry? existing))
            {
                items.Add(new DictionaryMergeItem(
                    DictionaryMergeKind.AddPerson,
                    DictionaryValidator.CATEGORY_PERSON,
                    entry.Canonical,
                    string.Empty));

                continue;
            }

            AddMissingAliases(
                items,
                DictionaryValidator.CATEGORY_PERSON,
                existing.Canonical,
                Names(existing.Canonical, existing.Aliases, existing.AliasesJa),
                Names(entry.Canonical, entry.Aliases, entry.AliasesJa));
        }
    }

    /// <summary>
    /// 団体を突き合わせる。**同一性は実体 ID で判断する**（docs/TAGGING_POLICY.md 5.3.1）。
    /// </summary>
    private static void CompareEnsembles(TagDictionary user, TagDictionary bundled, List<DictionaryMergeItem> items)
    {
        Dictionary<string, EnsembleEntry> byId = (user.Ensembles ?? [])
            .GroupBy(entry => entry.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (EnsembleEntry entry in bundled.Ensembles ?? [])
        {
            string label = DictionaryEditor.GetEnsembleNames(entry).FirstOrDefault() ?? entry.EntityId;

            if (!byId.TryGetValue(entry.EntityId, out EnsembleEntry? existing))
            {
                items.Add(new DictionaryMergeItem(
                    DictionaryMergeKind.AddEnsemble,
                    DictionaryValidator.CATEGORY_ENSEMBLE,
                    entry.EntityId,
                    string.Empty,
                    label));

                continue;
            }

            // 同梱で立った旗だけを提案する。利用者が下ろした旗を勝手に戻さない。
            if (entry.NoConductor && !existing.NoConductor)
            {
                items.Add(new DictionaryMergeItem(
                    DictionaryMergeKind.EnableNoConductor,
                    DictionaryValidator.CATEGORY_ENSEMBLE,
                    entry.EntityId,
                    string.Empty,
                    label));
            }

            if (entry.NoEraSplit && !existing.NoEraSplit)
            {
                items.Add(new DictionaryMergeItem(
                    DictionaryMergeKind.EnableNoEraSplit,
                    DictionaryValidator.CATEGORY_ENSEMBLE,
                    entry.EntityId,
                    string.Empty,
                    label));
            }

            AddMissingAliases(
                items,
                DictionaryValidator.CATEGORY_ENSEMBLE,
                existing.EntityId,
                AllEnsembleNames(existing),
                AllEnsembleNames(entry),
                DictionaryEditor.GetEnsembleNames(existing).FirstOrDefault() ?? existing.EntityId);
        }
    }

    /// <summary>
    /// 誤記を突き合わせる。パターンが同じものを同一とみなす。
    /// </summary>
    private static void CompareTypos(TagDictionary user, TagDictionary bundled, List<DictionaryMergeItem> items)
    {
        HashSet<string> existing = new((user.Typos ?? []).Select(entry => entry.Pattern), StringComparer.Ordinal);

        foreach (TypoEntry entry in (bundled.Typos ?? []).Where(entry => !existing.Contains(entry.Pattern)))
        {
            items.Add(new DictionaryMergeItem(
                DictionaryMergeKind.AddTypo,
                DictionaryValidator.CATEGORY_TYPO,
                entry.Pattern,
                entry.Replacement));
        }
    }

    /// <summary>
    /// 保護対象を突き合わせる。
    /// </summary>
    private static void CompareProtectedValues(TagDictionary user, TagDictionary bundled, List<DictionaryMergeItem> items)
    {
        HashSet<string> existing = new(
            (user.ProtectedAlbumArtists ?? []).Select(Key),
            StringComparer.Ordinal);

        foreach (string value in (bundled.ProtectedAlbumArtists ?? []).Where(value => !existing.Contains(Key(value))))
        {
            items.Add(new DictionaryMergeItem(
                DictionaryMergeKind.AddProtectedValue,
                DictionaryValidator.CATEGORY_PROTECTED,
                value,
                string.Empty));
        }
    }

    /// <summary>
    /// 同梱側にあって利用者側に無い別名を差分にする。
    /// </summary>
    private static void AddMissingAliases(
        List<DictionaryMergeItem> items,
        string category,
        string target,
        IEnumerable<string> userNames,
        IEnumerable<string> bundledNames,
        string label = "")
    {
        HashSet<string> existing = new(userNames.Select(Key), StringComparer.Ordinal);

        foreach (string name in bundledNames.Where(name => !existing.Contains(Key(name))))
        {
            items.Add(new DictionaryMergeItem(DictionaryMergeKind.AddAlias, category, target, name, label));
        }
    }

    /// <summary>
    /// 別名を追加する。対象は正規形（作曲家・人物）または実体 ID（団体）で引く。
    /// </summary>
    private static void ApplyAlias(
        List<ComposerEntry> composers,
        List<PersonEntry> persons,
        List<EnsembleEntry> ensembles,
        DictionaryMergeItem item)
    {
        bool japanese = DictionaryEditor.IsJapanese(item.Detail);

        if (item.Category == DictionaryValidator.CATEGORY_COMPOSER)
        {
            int index = composers.FindIndex(entry => Key(entry.Canonical) == Key(item.Target));

            if (index >= 0)
            {
                composers[index] = japanese
                    ? composers[index] with { AliasesJa = [.. composers[index].AliasesJa ?? [], item.Detail] }
                    : composers[index] with { Aliases = [.. composers[index].Aliases ?? [], item.Detail] };
            }

            return;
        }

        if (item.Category == DictionaryValidator.CATEGORY_PERSON)
        {
            int index = persons.FindIndex(entry => Key(entry.Canonical) == Key(item.Target));

            if (index >= 0)
            {
                persons[index] = japanese
                    ? persons[index] with { AliasesJa = [.. persons[index].AliasesJa ?? [], item.Detail] }
                    : persons[index] with { Aliases = [.. persons[index].Aliases ?? [], item.Detail] };
            }

            return;
        }

        int ensembleIndex = ensembles.FindIndex(entry => SameEntityId(entry.EntityId, item.Target));

        if (ensembleIndex >= 0)
        {
            ensembles[ensembleIndex] = japanese
                ? ensembles[ensembleIndex] with { AliasesJa = [.. ensembles[ensembleIndex].AliasesJa ?? [], item.Detail] }
                : ensembles[ensembleIndex] with { Aliases = [.. ensembles[ensembleIndex].Aliases ?? [], item.Detail] };
        }
    }

    /// <summary>
    /// 団体を書き換える。
    /// </summary>
    private static void UpdateEnsemble(
        List<EnsembleEntry> ensembles,
        string entityId,
        Func<EnsembleEntry, EnsembleEntry> update)
    {
        int index = ensembles.FindIndex(entry => SameEntityId(entry.EntityId, entityId));

        if (index >= 0)
        {
            ensembles[index] = update(ensembles[index]);
        }
    }

    /// <summary>
    /// 同梱側で見つかったエントリを足す。
    /// </summary>
    private static void AddIfFound<T>(List<T> target, IReadOnlyList<T>? source, Func<T, bool> predicate)
    {
        foreach (T entry in (source ?? []).Where(predicate))
        {
            target.Add(entry);
        }
    }

    /// <summary>
    /// 団体が持つすべての名前を返す。
    /// </summary>
    private static IEnumerable<string> AllEnsembleNames(EnsembleEntry entry)
    {
        return DictionaryEditor.GetEnsembleNames(entry)
            .Concat(entry.Aliases ?? [])
            .Concat(entry.AliasesJa ?? []);
    }

    /// <summary>
    /// 正規形と別名をまとめる。
    /// </summary>
    private static IEnumerable<string> Names(string canonical, IReadOnlyList<string>? aliases, IReadOnlyList<string>? aliasesJa)
    {
        return new[] { canonical }.Concat(aliases ?? []).Concat(aliasesJa ?? []);
    }

    /// <summary>
    /// 実体 ID が同じかを判定する。
    /// </summary>
    private static bool SameEntityId(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 照合に使う正規化キーを作る。
    /// </summary>
    private static string Key(string? value)
    {
        return NormalizationKey.Create(value);
    }
}
