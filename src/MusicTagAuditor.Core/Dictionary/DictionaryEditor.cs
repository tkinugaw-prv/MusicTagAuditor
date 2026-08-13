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
            ? target with { AliasesJa = Append(target.AliasesJa, alias, [target.Canonical, .. target.Aliases ?? []]) }
            : target with { Aliases = Append(target.Aliases, alias, [target.Canonical, .. target.AliasesJa ?? []]) };

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
            ? target with { AliasesJa = Append(target.AliasesJa, alias, [target.Canonical, .. target.Aliases ?? []]) }
            : target with { Aliases = Append(target.Aliases, alias, [target.Canonical, .. target.AliasesJa ?? []]) };

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

        // 団体は正規形が複数ありうる（時代分割）。そのすべてが別名の先客になる。
        string[] canonicals = [.. GetEnsembleNames(target)];

        ensembles[index] = IsJapanese(alias)
            ? target with { AliasesJa = Append(target.AliasesJa, alias, [.. canonicals, .. target.Aliases ?? []]) }
            : target with { Aliases = Append(target.Aliases, alias, [.. canonicals, .. target.AliasesJa ?? []]) };

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
    /// 作品を足す（docs/SPEC.md 7.4）。
    ///
    /// **自然キーは <c>composer</c> + <c>canonical</c> の組。** 同じ組が既にあれば新しい行を作らず、
    /// 別名だけを既存のエントリに足す。重複したエントリを作ると索引は先勝ちになり、
    /// 後から書いたほうは黙って捨てられる（<see cref="DictionaryValidator"/> がエラーにする）。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <param name="composer">作曲家の正規形。<c>composers</c> の正規形と一致させる。</param>
    /// <param name="canonical">作品名。</param>
    /// <param name="aliases">別名。日本語表記は自動で <c>aliasesJa</c> に振り分ける。</param>
    /// <returns>新しい辞書。</returns>
    public static TagDictionary AddWork(
        TagDictionary dictionary,
        string composer,
        string canonical,
        IEnumerable<string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentException.ThrowIfNullOrWhiteSpace(composer);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        string trimmedComposer = composer.Trim();
        string trimmedCanonical = canonical.Trim();

        (string[] latin, string[] japanese) = SplitByScript(aliases, trimmedCanonical);

        WorkEntry[] works = [.. dictionary.Works ?? []];
        int index = Array.FindIndex(
            works,
            entry => string.Equals(entry.Composer, trimmedComposer, StringComparison.Ordinal)
                && string.Equals(entry.Canonical, trimmedCanonical, StringComparison.Ordinal));

        if (index >= 0)
        {
            WorkEntry target = works[index];

            works[index] = target with
            {
                Aliases = Merge(target.Aliases, latin, [target.Canonical, .. target.AliasesJa ?? []]),
                AliasesJa = Merge(target.AliasesJa, japanese, [target.Canonical, .. target.Aliases ?? []]),
            };

            return dictionary with { Works = works };
        }

        WorkEntry added = new()
        {
            Composer = trimmedComposer,
            Canonical = trimmedCanonical,
            Aliases = latin,
            AliasesJa = japanese,
        };

        return dictionary with { Works = [.. works, added] };
    }

    /// <summary>
    /// 個別例外を足す（docs/SPEC.md 7.4.5）。
    ///
    /// **同じフォルダ + <c>disc</c> の項目があれば置き換える。** 同じ単位に 2 つ書いても
    /// 先に見つかったほうしか効かず、直したつもりの内容が反映されない。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <param name="entry">足す個別例外。</param>
    /// <returns>新しい辞書。</returns>
    public static TagDictionary AddAlbumOverride(TagDictionary dictionary, AlbumOverrideEntry entry)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Folder);

        AlbumOverrideEntry[] overrides = [.. dictionary.AlbumOverrides ?? []];
        string folderKey = DictionaryIndex.NormalizeFolder(entry.Folder);

        int index = Array.FindIndex(
            overrides,
            existing => string.Equals(
                    DictionaryIndex.NormalizeFolder(existing.Folder),
                    folderKey,
                    StringComparison.OrdinalIgnoreCase)
                && existing.Disc == entry.Disc);

        if (index >= 0)
        {
            overrides[index] = entry;

            return dictionary with { AlbumOverrides = overrides };
        }

        return dictionary with { AlbumOverrides = [.. overrides, entry] };
    }

    /// <summary>
    /// フォルダ名から作品の別名の候補を集める（docs/SPEC.md 7.3.2）。
    ///
    /// **人が取捨選択する前提の候補**であり、そのまま登録するものではない。
    /// フォルダ名には演奏者が付いていることが多い（<c>ブルックナー 8 - Wand</c>）。
    ///
    /// **作曲家として引けるセグメントは飛ばす。** 作曲家フォルダは作品名ではない。
    /// 残ったセグメントは、全体と「最初の <c>-</c> より前」の 2 通りを出す（7.4.3 手順4 と同じ切り方）。
    /// </summary>
    /// <param name="folder">ライブラリルートからの相対フォルダ。</param>
    /// <param name="index">現在の索引。作曲家フォルダの判定に使う。</param>
    /// <returns>候補。重複と空は落とす。順は浅いフォルダ → 深いフォルダ。</returns>
    public static IReadOnlyList<string> SuggestWorkAliases(string? folder, DictionaryIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        List<string> candidates = [];

        string[] segments = (folder ?? string.Empty).Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string segment in segments.Where(segment => !index.TryResolveComposer(segment, out _)))
        {
            candidates.Add(segment);

            string head = segment.Split('-')[0].Trim();

            if (head.Length > 0
                && !string.Equals(head, segment, StringComparison.Ordinal)
                && !index.TryResolveComposer(head, out _))
            {
                candidates.Add(head);
            }
        }

        return
        [
            .. candidates
                .Where(candidate => NormalizationKey.Create(candidate).Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
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
    ///
    /// **重複の判定は正規化キーで行う。** 字面で畳むと <c>Symphony No.7</c> と
    /// <c>Symphony No. 7</c> が両方残り、索引には片方しか載らないまま警告になる。
    /// </summary>
    private static (string[] Latin, string[] Japanese) SplitByScript(IEnumerable<string>? aliases, string canonical)
    {
        string canonicalKey = NormalizationKey.Create(canonical);

        string[] cleaned =
        [
            .. (aliases ?? [])
                .Select(alias => alias.Trim())
                .Where(alias => alias.Length > 0 && NormalizationKey.Create(alias) != canonicalKey)
                .DistinctBy(NormalizationKey.Create, StringComparer.Ordinal),
        ];

        return ([.. cleaned.Where(alias => !IsJapanese(alias))], [.. cleaned.Where(IsJapanese)]);
    }

    /// <summary>
    /// 別名の一覧に 1 件足す。
    /// </summary>
    private static IReadOnlyList<string> Append(IReadOnlyList<string>? values, string value, IEnumerable<string> occupied)
    {
        return Merge(values, [value], occupied);
    }

    /// <summary>
    /// 別名の一覧をまとめる。
    ///
    /// **判定は正規化キーで行う。字面の比較では足りない。** 索引は正規化キーで先勝ちに作られるため
    /// （<see cref="DictionaryIndex"/>）、キーが同じ別名を足しても引けるようにはならず、
    /// <see cref="DictionaryValidator"/> の警告だけが増える。<c>Symphony No.7</c> を
    /// <c>Symphony No. 7</c> の隣に置いても意味が無いのはこのため。
    /// </summary>
    /// <param name="values">足す先の一覧。</param>
    /// <param name="added">足す候補。</param>
    /// <param name="occupied">同じエントリが既に使っている名前（正規形と、もう一方の別名の一覧）。</param>
    /// <returns>足した後の一覧。</returns>
    private static IReadOnlyList<string> Merge(
        IReadOnlyList<string>? values,
        IEnumerable<string> added,
        IEnumerable<string> occupied)
    {
        IReadOnlyList<string> current = values ?? [];

        HashSet<string> keys = new(
            current.Concat(occupied).Select(NormalizationKey.Create).Where(key => key.Length > 0),
            StringComparer.Ordinal);

        List<string> result = [.. current];

        foreach (string value in added)
        {
            string key = NormalizationKey.Create(value);

            // キーが空になる別名（記号だけ）は索引に載らないが、衝突しているわけではない。
            // ここで弾くと検証の警告と食い違うので通す。
            if (key.Length == 0 || keys.Add(key))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
