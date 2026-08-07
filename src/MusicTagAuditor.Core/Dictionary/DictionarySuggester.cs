using System.Diagnostics.CodeAnalysis;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 候補 1 件分の母集合。正規形と、それを指しうる表記の一覧。
///
/// **入力欄に入れるのは常に <see cref="Canonical"/> である。** 別名や日本語表記は
/// 探すための手掛かりにすぎず、それをタグへ書くと次の検査で表記揺れとして跳ね返る。
/// </summary>
/// <param name="Canonical">正規形。</param>
/// <param name="Names">正規形・別名・日本語表記をまとめた、照合の対象になる表記。</param>
public sealed record SuggestionEntry(string Canonical, IReadOnlyList<string> Names);

/// <summary>
/// 絞り込み結果 1 件。
/// </summary>
/// <param name="Canonical">確定したときに入る正規形。</param>
/// <param name="MatchedAlias">正規形以外の表記で当たった場合、その表記。正規形自身が当たったなら null。</param>
public sealed record DictionarySuggestion(string Canonical, string? MatchedAlias)
{
    /// <summary>候補一覧に出す文字列。別名で当たった場合は、どの表記で当たったかを併記する。</summary>
    public string DisplayText => MatchedAlias is null ? Canonical : $"{Canonical}  —  {MatchedAlias}";

    /// <summary>
    /// 表示用の文字列を返す。
    ///
    /// レコードの既定の実装だとプロパティを並べた文字列になり、それが支援技術の読み上げに
    /// そのまま出る（候補一覧の項目名は画面表示ではなくこちらが使われる）。
    /// </summary>
    /// <returns>候補一覧に出す文字列。</returns>
    public override string ToString()
    {
        return DisplayText;
    }
}

/// <summary>
/// 手編集の入力欄に出す、辞書由来の候補を組み立てる。
///
/// 照合は <see cref="NormalizationKey"/> を通して行う。辞書引きと同じ土俵に乗せることで、
/// 大文字小文字・中黒・ダイアクリティカルマーク・ひらがな/カタカナの差を候補側でも吸収できる。
/// </summary>
public static class DictionarySuggester
{
    /// <summary>一度に出す候補の上限。docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。</summary>
    public const int MAX_SUGGESTIONS = 20;

    /// <summary>
    /// タグのフィールドに対応する辞書の種別を返す。
    ///
    /// <see cref="DictionaryEditor.SuggestCategory"/> は流用しない。あちらは検出済みの値を
    /// どの表へ入れるかの推定で、既定が人物になっている。そのまま使うと曲名や年の欄にまで
    /// 人物名の候補が出てしまう。**辞書が扱わないフィールドでは候補を出さない。**
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>対応する種別。辞書が扱わないフィールドなら null。</returns>
    public static DictionaryCategory? CategoryFor(TagField field)
    {
        return field switch
        {
            TagField.Composer => DictionaryCategory.Composer,
            TagField.AlbumArtist => DictionaryCategory.Ensemble,
            TagField.Artist or TagField.Conductor => DictionaryCategory.Person,
            _ => null,
        };
    }

    /// <summary>
    /// 指定した種別の候補の母集合を作る。
    /// </summary>
    /// <param name="dictionary">対象の辞書。</param>
    /// <param name="category">種別。</param>
    /// <returns>正規形ごとの候補。正規形の序数順。</returns>
    public static IReadOnlyList<SuggestionEntry> BuildCandidates(TagDictionary dictionary, DictionaryCategory category)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        IEnumerable<SuggestionEntry> entries = category switch
        {
            DictionaryCategory.Composer => (dictionary.Composers ?? [])
                .Select(entry => Build(entry.Canonical, entry.Aliases, entry.AliasesJa)),
            DictionaryCategory.Person => (dictionary.Persons ?? [])
                .Select(entry => Build(entry.Canonical, entry.Aliases, entry.AliasesJa)),
            _ => (dictionary.Ensembles ?? []).SelectMany(BuildEnsemble),
        };

        return
        [
            .. entries
                .Where(entry => entry.Canonical.Length > 0)
                .DistinctBy(entry => entry.Canonical, StringComparer.Ordinal)
                .OrderBy(entry => entry.Canonical, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 入力文字列で候補を絞り込む。
    ///
    /// 前方一致を先に出す。打ち始めた文字で始まる名前が下のほうに沈むと、
    /// 目的の候補があるのに無いように見える。
    /// </summary>
    /// <param name="candidates">候補の母集合。</param>
    /// <param name="input">入力中の文字列。空なら先頭から <paramref name="limit"/> 件を返す。</param>
    /// <param name="limit">返す件数の上限。</param>
    /// <returns>絞り込んだ候補。</returns>
    public static IReadOnlyList<DictionarySuggestion> Filter(
        IReadOnlyList<SuggestionEntry> candidates,
        string? input,
        int limit = MAX_SUGGESTIONS)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        string key = NormalizationKey.Create(input);

        if (key.Length == 0)
        {
            return [.. candidates.Take(limit).Select(entry => new DictionarySuggestion(entry.Canonical, null))];
        }

        List<(int Rank, string Canonical, DictionarySuggestion Suggestion)> matched = [];

        foreach (SuggestionEntry entry in candidates)
        {
            if (!TryMatch(entry, key, out int rank, out DictionarySuggestion? suggestion))
            {
                continue;
            }

            matched.Add((rank, entry.Canonical, suggestion));
        }

        return
        [
            .. matched
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.Canonical, StringComparer.Ordinal)
                .Select(item => item.Suggestion)
                .Take(limit),
        ];
    }

    /// <summary>
    /// 候補 1 件が入力に一致するかを判定する。
    ///
    /// 同じ正規形が複数の表記で当たっても 1 件にまとめる。もっとも良い一致（正規形自身の
    /// 前方一致が最良）を採り、そのときの表記を併記に使う。
    /// </summary>
    /// <param name="entry">判定する候補。</param>
    /// <param name="key">入力の正規化キー。</param>
    /// <param name="rank">並び順。小さいほど前に出す。</param>
    /// <param name="suggestion">一致した場合の候補。</param>
    /// <returns>一致すれば true。</returns>
    private static bool TryMatch(
        SuggestionEntry entry,
        string key,
        out int rank,
        [NotNullWhen(true)] out DictionarySuggestion? suggestion)
    {
        rank = int.MaxValue;
        suggestion = null;

        foreach (string name in entry.Names)
        {
            string nameKey = NormalizationKey.Create(name);

            if (nameKey.Length == 0 || !nameKey.Contains(key, StringComparison.Ordinal))
            {
                continue;
            }

            bool isCanonical = string.Equals(name, entry.Canonical, StringComparison.Ordinal);
            bool startsWith = nameKey.StartsWith(key, StringComparison.Ordinal);

            // 正規形の前方一致 → 別名の前方一致 → 正規形の部分一致 → 別名の部分一致。
            int candidateRank = (startsWith ? 0 : 2) + (isCanonical ? 0 : 1);

            if (candidateRank >= rank)
            {
                continue;
            }

            rank = candidateRank;
            suggestion = new DictionarySuggestion(entry.Canonical, isCanonical ? null : name);
        }

        return suggestion is not null;
    }

    /// <summary>
    /// 正規形と別名から候補を組み立てる。
    /// </summary>
    /// <param name="canonical">正規形。</param>
    /// <param name="aliases">ラテン文字の別表記。</param>
    /// <param name="aliasesJa">日本語表記。</param>
    /// <returns>候補。</returns>
    private static SuggestionEntry Build(
        string canonical,
        IReadOnlyList<string>? aliases,
        IReadOnlyList<string>? aliasesJa)
    {
        string trimmed = (canonical ?? string.Empty).Trim();

        string[] names =
        [
            .. new[] { trimmed }
                .Concat(aliases ?? [])
                .Concat(aliasesJa ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal),
        ];

        return new SuggestionEntry(trimmed, names);
    }

    /// <summary>
    /// 団体から候補を組み立てる。**時代分割エントリは正規形を複数持つ**ので、区分ごとに 1 件作る。
    ///
    /// 別名はどの区分から探しても辿り着けるよう、すべての区分に持たせる。
    /// 収録年で名称が決まるのは検査側の仕事で、入力欄は「その名前があること」を示せれば足りる。
    /// </summary>
    /// <param name="ensemble">対象の団体。</param>
    /// <returns>候補。</returns>
    private static IEnumerable<SuggestionEntry> BuildEnsemble(EnsembleEntry ensemble)
    {
        return DictionaryEditor.GetEnsembleNames(ensemble)
            .Select(canonical => Build(canonical, ensemble.Aliases, ensemble.AliasesJa));
    }
}
