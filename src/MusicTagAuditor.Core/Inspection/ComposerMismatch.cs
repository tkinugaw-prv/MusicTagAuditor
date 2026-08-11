using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// <c>composer</c> と違う作曲家名が見つかった経路。根拠として表示する。
/// </summary>
/// <param name="Tagged"><c>composer</c> タグの正規形。</param>
/// <param name="FromFileName">ファイル名に出てくる別の作曲家の正規形。無ければ null。</param>
/// <param name="FromTitle"><c>title</c> に出てくる別の作曲家の正規形。無ければ null。</param>
public sealed record ComposerMismatchHit(string Tagged, string? FromFileName, string? FromTitle);

/// <summary>
/// ファイル名・<c>title</c> に <c>composer</c> と違う作曲家名が出てくるファイルを見つける
/// （R-210 / docs/TAGGING_POLICY.md 6.9）。
///
/// <c>composer</c> が「辞書の正規形として正しいが、そのファイルの作曲家ではない」状態は
/// 値が辞書と一致してしまうため、R-201 でも R-501 でも検出できない。手掛かりはファイル名と
/// <c>title</c> に出てくる作曲家名しかない。
///
/// **一致しても誤りとは限らない。** ブラームス『ハイドンの主題による変奏曲』のように、曲名が
/// 別の作曲家の名前を正当に含む作品がある。この判別は機械ではできないので、判定は返すだけにして
/// 修正案は出さない。
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

    /// <summary>
    /// <c>composer</c> と違う作曲家名がファイル名・<c>title</c> に出てくるかを調べる。
    ///
    /// **<c>composer</c> が未設定のファイルは対象にしない。** 6.9 は「値が辞書の正規形と一致するのに
    /// そのファイルの作曲家ではない」状態を指す。未設定は R-401 が扱うため、ここで拾うと同じ
    /// ファイルが二重に明細へ出る。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <returns>見つかった食い違い。無ければ null。</returns>
    public static ComposerMismatchHit? Find(TrackTags track, DictionaryIndex dictionary)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(dictionary);

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

        if (fromFileName is null && fromTitle is null)
        {
            return null;
        }

        return new ComposerMismatchHit(canonical, fromFileName, fromTitle);
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

        string[] tokens = value.Split(
            SEPARATORS,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string token in tokens)
        {
            if (dictionary.ContainsComposerName(token, out string? found)
                && found is not null
                && !string.Equals(found, taggedCanonical, StringComparison.Ordinal))
            {
                return found;
            }
        }

        return null;
    }
}
