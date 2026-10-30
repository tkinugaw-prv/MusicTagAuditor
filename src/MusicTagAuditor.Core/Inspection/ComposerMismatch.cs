using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Normalization;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// <c>composer</c> と違う作曲家名が見つかった経路。根拠として表示する。
/// </summary>
/// <param name="Tagged"><c>composer</c> タグの正規形。</param>
/// <param name="FromFileName">ファイル名に出てくる別の作曲家の正規形。無ければ null。</param>
/// <param name="FromTitle"><c>title</c> に出てくる別の作曲家の正規形。無ければ null。</param>
/// <param name="FromFolder">フォルダ名に出てくる別の作曲家の正規形。無ければ null。</param>
public sealed record ComposerMismatchHit(
    string Tagged,
    string? FromFileName,
    string? FromTitle,
    string? FromFolder);

/// <summary>
/// R-210 の例外に登録しようとしている語句の問題（docs/TAGGING_POLICY.md 6.9）。
/// </summary>
public enum ComposerMentionPhraseIssue
{
    /// <summary>問題なし。</summary>
    None,

    /// <summary>語が 2 つ未満。<c>Bach</c> だけを外すと、本物の <c>Bach</c> の曲も検出できなくなる。</summary>
    TooFewWords,

    /// <summary>全語が作曲家名。作品の語句ではなく、作曲家を丸ごと外すことになる。</summary>
    OnlyComposerNames,

    /// <summary>別の作曲家名を含まない。登録しても何も起きない。</summary>
    NoOtherComposer,
}

/// <summary>
/// ファイル名・<c>title</c>・フォルダ名に <c>composer</c> と違う作曲家名が出てくるファイルを見つける
/// （R-210 / docs/TAGGING_POLICY.md 6.9）。
///
/// <c>composer</c> が「辞書の正規形として正しいが、そのファイルの作曲家ではない」状態は
/// 値が辞書と一致してしまうため、R-201 でも R-501 でも検出できない。手掛かりはファイル名・
/// <c>title</c>・フォルダ名に出てくる作曲家名しかない。
///
/// **一致しても誤りとは限らない。** ブラームス『ハイドンの主題による変奏曲』のように、曲名が
/// 別の作曲家の名前を正当に含む作品がある。この判別は機械ではできないので、判定は返すだけにして
/// 修正案は出さない。人が正しいと判断した語句は辞書の例外に残し、照合から外す。
///
/// **辞書に載っている作曲家名しか検出できない。** 辞書が育つほど誤検出は増える（6.9）。
///
/// 本体の検査ルール（R-210）と <c>tools/AlbumProbe</c> の測定が同じ判定を使うためにここに置く。
/// 実装が割れると、docs/library-baseline-2026-08-03.md の実測値との突き合わせが成立しなくなる。
/// </summary>
public static class ComposerMismatch
{
    /// <summary>
    /// ファイル名・曲名を語に分ける区切り。
    /// 部分一致で照合すると <c>Bach</c> が <c>Bachmann</c> に当たるため、語単位に割ってから引く
    /// （docs/SPEC.md 6.2）。<c>DictionaryIndex</c> が内部で使う区切りと同じものにそろえる。
    /// </summary>
    private static readonly char[] SEPARATORS =
        [' ', '\t', ',', ';', ':', '/', '-', '_', '(', ')', '[', ']', '&', '.', '　', '、', '・'];

    /// <summary>例外の語句に要る最小の語数（docs/TAGGING_POLICY.md 6.9）。</summary>
    private const int MINIMUM_PHRASE_WORDS = 2;

    /// <summary>語句のキーで語を繋ぐ区切り。タグの値に現れない制御文字を選ぶ。</summary>
    private const string PHRASE_KEY_SEPARATOR = "\u0001";

    /// <summary>
    /// <c>composer</c> と違う作曲家名がファイル名・<c>title</c>・フォルダ名に出てくるかを調べる。
    ///
    /// **<c>composer</c> が未設定のファイルは対象にしない。** 6.9 は「値が辞書の正規形と一致するのに
    /// そのファイルの作曲家ではない」状態を指す。未設定は R-401 が扱うため、ここで拾うと同じ
    /// ファイルが二重に明細へ出る。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="folderTracks">
    /// 同じフォルダのファイル（対象自身を含む）。フォルダ名を手がかりに使ってよいかの判定に要る。
    /// 空を渡すとフォルダ名は手がかりにしない。
    /// </param>
    /// <returns>見つかった食い違い。無ければ null。</returns>
    public static ComposerMismatchHit? Find(
        TrackTags track,
        DictionaryIndex dictionary,
        IReadOnlyList<TrackTags> folderTracks)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(folderTracks);

        string? tagged = track.Composer;

        if (string.IsNullOrWhiteSpace(tagged))
        {
            return null;
        }

        // 表記揺れを食い違いに数えない。`Shostakovich` と `Dmitri Shostakovich` は同一人物であり、
        // 正規形に寄せてから比べないと、R-201 が直す値を R-210 が別の作曲家として拾う。
        string canonical = dictionary.TryResolveComposer(tagged, out string resolved) ? resolved : tagged;

        string? fromFileName = FindOther(
            dictionary,
            Path.GetFileNameWithoutExtension(track.RelativePath),
            canonical);
        string? fromTitle = FindOther(dictionary, track.Title, canonical);
        string? fromFolder = FindInFolder(track, dictionary, folderTracks, canonical);

        if (fromFileName is null && fromTitle is null && fromFolder is null)
        {
            return null;
        }

        return new ComposerMismatchHit(canonical, fromFileName, fromTitle, fromFolder);
    }

    /// <summary>
    /// フォルダ名に別の作曲家名が出てくるかを調べる。
    ///
    /// **フォルダ内の <c>composer</c> が 1 種類のときだけ手がかりにする。** カップリング盤
    /// （docs/TAGGING_POLICY.md 3.5 規則5）はフォルダ名が主作品の作曲家を名乗るので、
    /// 併録曲のファイルは「フォルダ名と違う作曲家」になるのが**正しい**。この除外をしないと
    /// 主作品 + カップリングの 4 単位が丸ごと誤検出になる。
    ///
    /// 逆に、フォルダ全体が 1 人の作曲家で埋まっているのにフォルダ名が別の作曲家を指す場合は、
    /// 6.9 の「演奏会 1 回分のプログラムが主作品の作曲家で埋められていた」と同じ形になる。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="folderTracks">同じフォルダのファイル。</param>
    /// <param name="canonical">対象ファイルの <c>composer</c> の正規形。</param>
    /// <returns>見つかった別の作曲家の正規形。無ければ null。</returns>
    private static string? FindInFolder(
        TrackTags track,
        DictionaryIndex dictionary,
        IReadOnlyList<TrackTags> folderTracks,
        string canonical)
    {
        if (folderTracks.Count == 0 || CountComposers(dictionary, folderTracks) != 1)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(track.RelativePath) ?? string.Empty;

        foreach (string segment in folder.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (FindOther(dictionary, segment, canonical) is { } other)
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// フォルダ内の <c>composer</c> が何種類あるかを数える。表記揺れは 1 種類として数える。
    /// </summary>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="folderTracks">同じフォルダのファイル。</param>
    /// <returns>相異なる作曲家の数。未設定は数えない。</returns>
    private static int CountComposers(DictionaryIndex dictionary, IReadOnlyList<TrackTags> folderTracks)
    {
        return folderTracks
            .Select(sibling => sibling.Composer)
            .Where(composer => !string.IsNullOrWhiteSpace(composer))
            .Select(composer => dictionary.TryResolveComposer(composer, out string resolved) ? resolved : composer!)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>
    /// 値の中に、指定した作曲家とは違う作曲家名が出てくれば返す。
    ///
    /// R-210 の判定そのもの。<c>tools/AlbumProbe</c> は作品エントリの雛形を作るときに、
    /// <c>album</c> の値が別の作曲家を指していないかを見るのに使う。
    /// </summary>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="value">判定する値（ファイル名・曲名・アルバム名など）。</param>
    /// <param name="taggedCanonical">比較の基準になる作曲家の正規形。</param>
    /// <returns>見つかった別の作曲家の正規形。無ければ null。</returns>
    public static string? FindOther(DictionaryIndex dictionary, string? value, string taggedCanonical)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // 値そのものが団体名・人物名なら、作曲家の姓を含んでいても作曲家ではない。
        // 語に割ってから引くと `Münchener Bach-Chor` の `Bach` が当たるため、割る前に外す
        // （`DictionaryIndex.ContainsComposerName` が値全体に対して行っているのと同じ判定）。
        if (dictionary.TryResolveEnsemble(value, out _) || dictionary.TryResolvePerson(value, out _))
        {
            return null;
        }

        IReadOnlyList<string> words = SplitWords(value);
        bool[] excepted = MarkExceptedWords(words, dictionary.GetComposerMentionExceptions(taggedCanonical));

        for (int i = 0; i < words.Count; i++)
        {
            if (!excepted[i]
                && dictionary.ContainsComposerName(words[i], out string? found)
                && found is not null
                && !string.Equals(found, taggedCanonical, StringComparison.Ordinal))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 値を照合に使う語へ分ける。記号だけの語は落とす。
    /// 残すと <c>Szene - am Bach</c> のような区切りの違いで語句の並びが崩れる。
    /// </summary>
    /// <param name="value">分ける値。</param>
    /// <returns>語の並び。</returns>
    public static IReadOnlyList<string> SplitWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return
        [
            .. value
                .Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(word => NormalizationKey.Create(word).Length > 0),
        ];
    }

    /// <summary>
    /// 語句の同一性を比べるキーを作る。大小文字・発音区別符号・区切りの違いを同じものとして扱う。
    /// </summary>
    /// <param name="phrase">語句。</param>
    /// <returns>キー。</returns>
    public static string CreatePhraseKey(string? phrase)
    {
        return string.Join(PHRASE_KEY_SEPARATOR, SplitWords(phrase).Select(NormalizationKey.Create));
    }

    /// <summary>
    /// 語句を R-210 の例外にしてよいかを調べる（docs/TAGGING_POLICY.md 6.9）。
    /// 辞書の検証と登録ダイアログで判定が割れないよう、ここに 1 つだけ置く。
    /// </summary>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="phrase">語句。</param>
    /// <param name="composerCanonical">例外を効かせる作曲家の正規形。</param>
    /// <returns>見つかった問題。</returns>
    public static ComposerMentionPhraseIssue CheckExceptionPhrase(
        DictionaryIndex dictionary,
        string? phrase,
        string composerCanonical)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        IReadOnlyList<string> words = SplitWords(phrase);

        if (words.Count < MINIMUM_PHRASE_WORDS)
        {
            return ComposerMentionPhraseIssue.TooFewWords;
        }

        List<string> composers = [];

        foreach (string word in words)
        {
            if (dictionary.ContainsComposerName(word, out string? found) && found is not null)
            {
                composers.Add(found);
            }
        }

        if (composers.Count == words.Count)
        {
            return ComposerMentionPhraseIssue.OnlyComposerNames;
        }

        return composers.Exists(found => !string.Equals(found, composerCanonical, StringComparison.Ordinal))
            ? ComposerMentionPhraseIssue.None
            : ComposerMentionPhraseIssue.NoOtherComposer;
    }

    /// <summary>
    /// 例外の語句に一致した語に印を付ける。
    ///
    /// **語の並びで比べる。** 値全体の正規化キーは空白を除くため、語の境界が消えて
    /// <c>Szene am Bach</c> が <c>Szene am Bachmann</c> に当たる。
    /// **外すのは一致した部分だけ。** 同じ値にある別の作曲家名は検出を続ける。
    /// </summary>
    /// <param name="words">値の語の並び。</param>
    /// <param name="phrases">例外の語句。</param>
    /// <returns>語ごとの印。</returns>
    private static bool[] MarkExceptedWords(IReadOnlyList<string> words, IReadOnlyList<string> phrases)
    {
        bool[] excepted = new bool[words.Count];

        if (phrases.Count == 0)
        {
            return excepted;
        }

        string[] wordKeys = [.. words.Select(NormalizationKey.Create)];

        foreach (string phrase in phrases)
        {
            string[] phraseKeys = [.. SplitWords(phrase).Select(NormalizationKey.Create)];

            // 手で書かれた `Bach` 単独を効かせない。検証のエラーを無視して起動しても検出を止めない。
            if (phraseKeys.Length < MINIMUM_PHRASE_WORDS)
            {
                continue;
            }

            for (int start = 0; start + phraseKeys.Length <= wordKeys.Length; start++)
            {
                if (wordKeys.AsSpan(start, phraseKeys.Length).SequenceEqual(phraseKeys))
                {
                    Array.Fill(excepted, true, start, phraseKeys.Length);
                }
            }
        }

        return excepted;
    }
}
