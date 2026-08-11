using System.Text.Json;
using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Normalization;

namespace AlbumProbe;

/// <summary>
/// 作品エントリ（docs/SPEC.md 7.4）の雛形を作る。
///
/// アルバム単位から作曲家と手がかり（フォルダ名・現在の <c>album</c> の値）を集め、
/// <c>works</c> の候補として書き出す。**<c>canonical</c> は空のまま出す。** 現在の <c>album</c> の値は
/// 誤っていることがあり（docs/TAGGING_POLICY.md 3.5 補足2）、機械が正規形として採用してはならない。
///
/// **単位をまとめる鍵はフォルダ名だけにする。**<c>album</c> の値でまとめると、
/// <c>Brahms 交響曲全集</c> や <c>Shostakovich Symphony No.5,9</c> のように複数の作品にまたがる値が
/// 橋渡しになって別作品が 1 エントリに融合する。実測ではブラームスの交響曲 1〜4 番が 1 件に、
/// シューベルトの 8 番と 9 番が 1 件に潰れた。<c>album</c> は候補として拾うだけにする。
///
/// **読み取りだけを行う。** ライブラリにも辞書にも書き込まない。
/// </summary>
public static class WorksTemplate
{
    /// <summary>日本語の文字。エイリアスを <c>aliases</c> と <c>aliasesJa</c> に振り分けるのに使う。</summary>
    private static readonly Regex JAPANESE = new(
        @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]",
        RegexOptions.Compiled);

    /// <summary>雛形の書き出し設定。人が編集するファイルなので整形して出す。</summary>
    private static readonly JsonSerializerOptions JSON = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 雛形を組み立ててファイルに書き出し、レポートに要約を残す。
    /// </summary>
    /// <param name="report">出力先のレポート。</param>
    /// <param name="units">アルバム単位。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="path">雛形の書き出し先。</param>
    /// <returns>書き出した作品エントリの数。</returns>
    public static async Task<int> WriteAsync(
        ReportWriter report,
        IReadOnlyList<AlbumUnit> units,
        DictionaryIndex dictionary,
        string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(dictionary);

        List<WorkGroup> groups = [];
        List<AlbumUnit> unresolved = [];

        foreach (AlbumUnit unit in units)
        {
            // 作曲家が 1 つに定まらない単位は雛形にできない。3.5 規則5・規則6 の対象で、
            // 主作品を機械では決められない（docs/SPEC.md 7.4.3 手順2）。
            if (unit.Composers.Count != 1)
            {
                unresolved.Add(unit);
                continue;
            }

            string[] hints = [.. FolderHints(unit.Folder, dictionary)];

            if (hints.Length == 0)
            {
                unresolved.Add(unit);
                continue;
            }

            Merge(groups, unit.Composers[0], hints, unit.Folder, unit.Albums);
        }

        IReadOnlyList<AmbiguousAlbum> ambiguous = DropAmbiguousAlbums(groups);
        IReadOnlyList<ForeignAlias> foreign = FindForeignAliases(groups, dictionary);

        WorkEntry[] works =
        [
            .. groups
                .OrderBy(group => group.Composer, StringComparer.Ordinal)
                .ThenBy(group => group.Aliases.Order(StringComparer.Ordinal).First(), StringComparer.Ordinal)
                .Select(group => new WorkEntry
                {
                    Composer = group.Composer,
                    Canonical = string.Empty,
                    Aliases = [.. group.Aliases.Where(alias => !JAPANESE.IsMatch(alias)).Order(StringComparer.Ordinal)],
                    AliasesJa = [.. group.Aliases.Where(alias => JAPANESE.IsMatch(alias)).Order(StringComparer.Ordinal)],
                }),
        ];

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new WorksTemplateFile(works), JSON)).ConfigureAwait(false);

        WriteSummary(report, groups, unresolved, ambiguous, foreign, works.Length, units.Count, path);

        return works.Length;
    }

    /// <summary>
    /// フォルダから作品を引く手がかりを集める（docs/SPEC.md 7.4.3 手順4）。
    ///
    /// **作曲家フォルダを飛ばした先頭のセグメントを採る。** 末端から採ると
    /// <c>ワーグナー\タンホイザー\第一幕</c> と <c>ワーグナー\ワルキューレ\第一幕</c> が
    /// <c>第一幕</c> で繋がり、別の歌劇が 1 エントリに融合する。幕やディスクは作品の下位区分であって
    /// 作品そのものではない。
    /// </summary>
    /// <param name="folder">ライブラリルートからのフォルダ。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <returns>手がかり。セグメント全体と、演奏者を落とした前半。</returns>
    private static IEnumerable<string> FolderHints(string folder, DictionaryIndex dictionary)
    {
        if (string.Equals(folder, Const.ROOT_FOLDER_LABEL, StringComparison.Ordinal))
        {
            return [];
        }

        string? segment = folder
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => !dictionary.TryResolveComposer(part, out _));

        if (segment is null)
        {
            return [];
        }

        List<string> hints = [segment];

        // フォルダ名には演奏者が付いていることが多い（`ブルックナー 8 - Wand`）。
        // 全体でしか引かないと、同じ作品が演奏者の数だけ別のエントリになる。
        string head = segment.Split('-')[0].Trim();

        if (head.Length > 0
            && !string.Equals(head, segment, StringComparison.Ordinal)
            && !dictionary.TryResolveComposer(head, out _))
        {
            hints.Add(head);
        }

        return hints.Where(hint => NormalizationKey.Create(hint).Length > 0);
    }

    /// <summary>
    /// 手がかりを共有するグループへ単位を合流させる。共有先が複数あればそれらもまとめる。
    ///
    /// **同じ作曲家の中だけで合流させる。** <c>Symphony No.5</c> は作曲家が違えば別の作品である。
    /// </summary>
    /// <param name="groups">既存のグループ。</param>
    /// <param name="composer">単位の作曲家。</param>
    /// <param name="hints">単位のフォルダ由来の手がかり。合流の鍵になる。</param>
    /// <param name="folder">単位のフォルダ。由来を残すために持つ。</param>
    /// <param name="albums">単位の <c>album</c> の値。候補として持つだけで、合流の鍵にはしない。</param>
    private static void Merge(
        List<WorkGroup> groups,
        string composer,
        IReadOnlyList<string> hints,
        string folder,
        IReadOnlyList<string> albums)
    {
        HashSet<string> keys = [.. hints.Select(NormalizationKey.Create)];

        WorkGroup[] matched =
        [
            .. groups.Where(group =>
                string.Equals(group.Composer, composer, StringComparison.Ordinal) && group.Keys.Overlaps(keys)),
        ];

        if (matched.Length == 0)
        {
            groups.Add(new WorkGroup(composer, keys, [.. hints], [folder], [.. albums]));
            return;
        }

        WorkGroup target = matched[0];

        foreach (WorkGroup other in matched.Skip(1))
        {
            target.Absorb(other);
            groups.Remove(other);
        }

        target.Add(keys, hints, folder, albums);
    }

    /// <summary>
    /// 同じ作曲家の複数のエントリが名乗る <c>album</c> の値を落とす。
    ///
    /// <c>Brahms 交響曲全集</c> のように複数の作品にまたがる値や、誤ったタグ
    /// （<c>シューベルト 9</c> に付いた <c>Schubert Symphony No.8</c>）がこれに当たる。
    /// **どの作品の別名にもならない値なので、エイリアスに残すと引いたときに衝突する。**
    /// </summary>
    /// <param name="groups">グループ。合致した候補はエイリアスへ昇格させる。</param>
    /// <returns>落とした値。レポートに出す。</returns>
    private static IReadOnlyList<AmbiguousAlbum> DropAmbiguousAlbums(IReadOnlyList<WorkGroup> groups)
    {
        List<AmbiguousAlbum> dropped = [];

        foreach (var byComposer in groups.GroupBy(group => group.Composer, StringComparer.Ordinal))
        {
            WorkGroup[] members = [.. byComposer];

            // 正規化キー → その値を名乗るグループ。2 つ以上あれば作品を特定できない。
            Dictionary<string, List<WorkGroup>> owners = [];

            foreach (WorkGroup group in members)
            {
                foreach (string album in group.Albums)
                {
                    string key = NormalizationKey.Create(album);

                    if (key.Length == 0)
                    {
                        continue;
                    }

                    if (!owners.TryGetValue(key, out List<WorkGroup>? list))
                    {
                        owners[key] = list = [];
                    }

                    if (!list.Contains(group))
                    {
                        list.Add(group);
                    }
                }
            }

            foreach (WorkGroup group in members)
            {
                foreach (string album in group.Albums)
                {
                    string key = NormalizationKey.Create(album);

                    if (key.Length == 0 || group.Keys.Contains(key))
                    {
                        continue;
                    }

                    if (owners[key].Count == 1)
                    {
                        group.PromoteAlias(key, album);
                        continue;
                    }

                    dropped.Add(new AmbiguousAlbum(byComposer.Key, album, owners[key].Count));
                }
            }
        }

        return [.. dropped.DistinctBy(item => (item.Composer, item.Album))];
    }

    /// <summary>
    /// エイリアス候補のうち、別の作曲家の名前を含むものを拾う。
    ///
    /// 1 つのエントリしか名乗っていない値は 6.2 では落ちないが、
    /// <c>シューベルト 8</c> のフォルダに付いた <c>Mendelssohn : Symphony No.4 "Italia"</c> のように
    /// **そもそも別の作品のタグ**であることがある。判定は R-210 と同じものを使う。
    /// </summary>
    /// <param name="groups">グループ。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <returns>別の作曲家名を含むエイリアス候補。</returns>
    private static IReadOnlyList<ForeignAlias> FindForeignAliases(
        IReadOnlyList<WorkGroup> groups,
        DictionaryIndex dictionary)
    {
        List<ForeignAlias> found = [];

        foreach (WorkGroup group in groups)
        {
            foreach (string alias in group.Aliases)
            {
                if (MusicTagAuditor.Core.Inspection.ComposerMismatch.FindOther(dictionary, alias, group.Composer)
                    is { } other)
                {
                    found.Add(new ForeignAlias(group.Composer, alias, other, group.Folders[0]));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// レポートに要約を書く。**人が埋める前提のファイルなので、何をすべきかを添える。**
    /// </summary>
    private static void WriteSummary(
        ReportWriter report,
        IReadOnlyList<WorkGroup> groups,
        IReadOnlyList<AlbumUnit> unresolved,
        IReadOnlyList<AmbiguousAlbum> ambiguous,
        IReadOnlyList<ForeignAlias> foreign,
        int workCount,
        int unitCount,
        string path)
    {
        report.Heading(2, $"6. 作品エントリの雛形（SPEC 7.4.6） — {workCount} 件 / {unitCount} 単位");
        report.Line($"書き出し先: `{path}`");
        report.Line();
        report.Line("**`canonical` は空で出している。ここは人が埋める。** 現在の `album` の値は誤っていることがあり"
            + "（3.5 補足2）、機械が正規形として採用してはならない。");
        report.Line("言語は「ジャンル名は英語・固有の題名は原語」（3.5 規則8）。");
        report.Line();
        report.Line("**単位をまとめる鍵はフォルダ名だけ。**`album` の値は候補として拾い、"
            + "同じ作曲家の複数のエントリが名乗る値は落としてある（6.2）。");
        report.Line();
        report.TableHeader("作曲家", "フォルダ数", "エイリアス候補", "フォルダ");

        foreach (WorkGroup group in groups
                     .OrderByDescending(group => group.Folders.Count)
                     .ThenBy(group => group.Composer, StringComparer.Ordinal))
        {
            report.TableRow(
                group.Composer,
                $"{group.Folders.Count}",
                string.Join(" / ", group.Aliases.Order(StringComparer.Ordinal)),
                string.Join("<br>", group.Folders.Order(StringComparer.OrdinalIgnoreCase).Select(folder => $"`{folder}`")));
        }

        report.Heading(3, $"6.1 雛形にできなかった単位 — {unresolved.Count} 単位");
        report.Line("作曲家が単位内で 1 つに定まらないもの。**`albumOverrides` で個別に決める**"
            + "（3.5 規則5・規則6 / SPEC 7.4.5）。");
        report.Line();
        report.TableHeader("フォルダ", "disc", "ファイル", "composer");

        foreach (AlbumUnit unit in unresolved)
        {
            report.TableRow(
                $"`{unit.Folder}`",
                $"{unit.Disc}",
                $"{unit.Tracks.Count}",
                unit.Composers.Count == 0 ? Const.NO_VALUE : string.Join(" / ", unit.Composers));
        }

        report.Heading(3, $"6.2 エイリアスにしなかった `album` の値 — {ambiguous.Count} 件");
        report.Line("同じ作曲家の複数の作品が名乗っている値。**作品を特定できないのでエイリアスに使えない。**");
        report.Line("全集・カップリング盤の値（`Brahms 交響曲全集`）と、誤ったタグ"
            + "（`シューベルト 9` に付いた `Schubert Symphony No.8`）の両方がここに出る。後者は R-504 とは別に直す。");
        report.Line();
        report.TableHeader("作曲家", "`album` の値", "名乗る作品数");

        foreach (AmbiguousAlbum item in ambiguous
                     .OrderBy(item => item.Composer, StringComparer.Ordinal)
                     .ThenBy(item => item.Album, StringComparer.Ordinal))
        {
            report.TableRow(item.Composer, $"`{item.Album}`", $"{item.Owners}");
        }

        report.Heading(3, $"6.3 別の作曲家名を含むエイリアス候補 — {foreign.Count} 件");
        report.Line("**そのままエイリアスにしてはならない。** 別の作品のタグが紛れている可能性が高い。");
        report.Line("判定は R-210 と同じで、**一致しても誤りとは限らない**（作品名が別の作曲家名を正当に含むことがある）。");
        report.Line();
        report.TableHeader("エントリの作曲家", "エイリアス候補", "含まれる作曲家", "フォルダの例");

        foreach (ForeignAlias item in foreign
                     .OrderBy(item => item.Composer, StringComparer.Ordinal)
                     .ThenBy(item => item.Alias, StringComparer.Ordinal))
        {
            report.TableRow(item.Composer, $"`{item.Alias}`", item.Other, $"`{item.Folder}`");
        }
    }

    /// <summary>
    /// 別の作曲家名を含むエイリアス候補。
    /// </summary>
    /// <param name="Composer">エントリの作曲家。</param>
    /// <param name="Alias">エイリアス候補。</param>
    /// <param name="Other">含まれていた別の作曲家。</param>
    /// <param name="Folder">由来のフォルダ（代表 1 つ）。</param>
    private sealed record ForeignAlias(string Composer, string Alias, string Other, string Folder);

    /// <summary>
    /// 作品を特定できなかった <c>album</c> の値。
    /// </summary>
    /// <param name="Composer">作曲家。</param>
    /// <param name="Album">値。</param>
    /// <param name="Owners">この値を名乗る作品の数。</param>
    private sealed record AmbiguousAlbum(string Composer, string Album, int Owners);

    /// <summary>
    /// 雛形ファイルの中身。辞書の一部として貼り付けられる形にする。
    /// </summary>
    /// <param name="Works">作品エントリ。</param>
    private sealed record WorksTemplateFile(IReadOnlyList<WorkEntry> Works);

    /// <summary>
    /// フォルダ名を共有する単位の集まり。1 つが作品エントリ 1 件になる。
    /// </summary>
    private sealed class WorkGroup(
        string composer,
        HashSet<string> keys,
        List<string> aliases,
        List<string> folders,
        List<string> albums)
    {
        /// <summary>作曲家の正規形。</summary>
        public string Composer { get; } = composer;

        /// <summary>エイリアスの正規化キー。合流の判定に使う。</summary>
        public HashSet<string> Keys { get; } = keys;

        /// <summary>エイリアスの元の表記。</summary>
        public List<string> Aliases { get; } = aliases;

        /// <summary>由来のフォルダ。誤って融合していないかの確認に使う。</summary>
        public List<string> Folders { get; } = folders;

        /// <summary><c>album</c> の値。**合流の鍵にしない。** 曖昧でなければエイリアスへ昇格する。</summary>
        public List<string> Albums { get; } = albums;

        /// <summary>
        /// 単位を 1 つ足す。
        /// </summary>
        public void Add(IEnumerable<string> keys, IEnumerable<string> aliases, string folder, IEnumerable<string> albums)
        {
            Keys.UnionWith(keys);
            AddNew(Aliases, aliases);
            AddNew(Albums, albums);

            if (!Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(folder);
            }
        }

        /// <summary>
        /// 別のグループを取り込む。手がかりが橋渡しになって 2 つのグループが繋がった場合に使う。
        /// </summary>
        public void Absorb(WorkGroup other)
        {
            Keys.UnionWith(other.Keys);
            AddNew(Aliases, other.Aliases);
            AddNew(Albums, other.Albums);
            Folders.AddRange(other.Folders.Where(folder => !Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// <c>album</c> の値をエイリアスに昇格させる。
        /// </summary>
        public void PromoteAlias(string key, string album)
        {
            if (Keys.Add(key))
            {
                Aliases.Add(album);
            }
        }

        /// <summary>
        /// まだ持っていない値だけを足す。
        /// </summary>
        private static void AddNew(List<string> target, IEnumerable<string> values)
        {
            target.AddRange(values.Where(value => !target.Contains(value, StringComparer.Ordinal)));
        }
    }
}
