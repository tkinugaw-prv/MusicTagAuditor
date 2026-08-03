using MusicTagger.Core.Models;

namespace MusicTagger.Core.Inspection;

/// <summary>
/// 作曲家を特定した経路。根拠として表示する。
/// </summary>
/// <param name="Canonical">特定できた作曲家の正規形。</param>
/// <param name="Source">特定に使った手がかりの説明。</param>
public sealed record ComposerHit(string Canonical, string Source);

/// <summary>
/// <c>composer</c> が空欄のファイルについて、作曲家を特定する（R-401）。
///
/// 実ライブラリの 26 件は、次の 3 経路で全件が決まる
/// （docs/library-baseline-2026-08-03.md 6.5 / 段階 7 の実測）。
///
/// 1. 同一フォルダの他ファイルの <c>composer</c>（5 件）
/// 2. <c>artist</c> / <c>albumartist</c> に入っている作曲家名（17 件）
/// 3. パスに含まれる作曲家名（4 件）
///
/// **いずれも「値が一意に決まる」場合しか採らない。** 候補が割れたら諦める。
/// </summary>
public static class ComposerFinder
{
    /// <summary>フォルダ名を語に分ける区切り。</summary>
    private static readonly char[] SEPARATORS =
        [' ', '\t', ',', ';', ':', '/', '-', '_', '(', ')', '[', ']', '&', '.', '　', '、', '・'];

    /// <summary>
    /// 作曲家を特定する。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <param name="context">ライブラリ全体の文脈。</param>
    /// <returns>特定できた作曲家。できなければ null。</returns>
    public static ComposerHit? Find(TrackTags track, InspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(context);

        if (FindInSiblings(track, context) is { } fromSiblings)
        {
            return fromSiblings;
        }

        // artist / albumartist に作曲家名が残っている場合。R-203 / R-204 が別途これを直す。
        foreach (TagField field in new[] { TagField.Artist, TagField.AlbumArtist })
        {
            foreach (string value in track.GetValues(field))
            {
                if (context.Dictionary.ContainsComposerName(value, out string? composer) && composer is not null)
                {
                    return new ComposerHit(composer, $"{field} の「{value}」から特定");
                }
            }
        }

        return FindInPath(track, context);
    }

    /// <summary>
    /// 同一フォルダの他ファイルから探す。値が割れていたら諦める。
    /// </summary>
    private static ComposerHit? FindInSiblings(TrackTags track, InspectionContext context)
    {
        string[] names =
        [
            .. context.GetSiblings(track)
                .Where(sibling => !ReferenceEquals(sibling, track))
                .SelectMany(sibling => sibling.GetValues(TagField.Composer))
                .Distinct(StringComparer.Ordinal),
        ];

        if (names.Length != 1)
        {
            return null;
        }

        // 辞書に載っていない値を写しても誤りが伝播するだけ。正規形に解決できたものだけ採る。
        if (!context.Dictionary.TryResolveComposer(names[0], out string canonical))
        {
            return null;
        }

        return new ComposerHit(canonical, "同一フォルダの他ファイルから特定");
    }

    /// <summary>
    /// パスに含まれる作曲家名から探す。
    /// <c>シベリウス\シベリウス 7</c> のように、親フォルダに作曲家名が付いている構成を拾う。
    /// </summary>
    private static ComposerHit? FindInPath(TrackTags track, InspectionContext context)
    {
        string folder = InspectionContext.GetFolder(track.RelativePath);

        foreach (string segment in folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (context.Dictionary.TryResolveComposer(segment, out string whole))
            {
                return new ComposerHit(whole, $"フォルダ名「{segment}」から特定");
            }

            foreach (string token in segment.Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (context.Dictionary.TryResolveComposer(token, out string fromToken))
                {
                    return new ComposerHit(fromToken, $"フォルダ名「{segment}」から特定");
                }
            }
        }

        return null;
    }
}
