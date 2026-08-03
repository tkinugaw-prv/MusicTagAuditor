using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Inspection;

/// <summary>
/// 指揮者を特定した経路。根拠として表示する。
/// </summary>
/// <param name="Canonical">特定できた指揮者の正規形。</param>
/// <param name="Source">特定に使った手がかりの説明。</param>
public sealed record ConductorHit(string Canonical, string Source);

/// <summary>
/// 指揮者を特定する。docs/TAGGING_POLICY.md 6.2 の順序に従い、いずれも失敗したら諦める。
///
/// 1. フォルダ名に含まれる指揮者名（辞書の日本語↔英語対応を使う）
/// 2. <c>albumartist</c> に含まれる指揮者名
/// 3. 同一アルバムの他ファイル
/// </summary>
public static class ConductorFinder
{
    /// <summary>フォルダ名・値を語に分ける区切り。</summary>
    private static readonly char[] SEPARATORS =
        [' ', '\t', ',', ';', ':', '/', '-', '_', '(', ')', '[', ']', '&', '.', '　', '、', '・'];

    /// <summary>
    /// 指揮者を特定する。
    /// </summary>
    /// <param name="track">対象ファイル。</param>
    /// <param name="context">ライブラリ全体の文脈。</param>
    /// <returns>特定できた指揮者。できなければ null。</returns>
    public static ConductorHit? Find(TrackTags track, InspectionContext context)
    {
        // 1. フォルダ名。「ブルックナー 8 - ショルティ」のような命名から拾う。
        string folder = InspectionContext.GetFolder(track.RelativePath);

        if (FindInText(folder, context.Dictionary) is { } fromFolder)
        {
            return new ConductorHit(fromFolder, $"フォルダ名「{Path.GetFileName(folder)}」から特定");
        }

        // 2. albumartist。楽団名と指揮者名が連結されている場合がある。
        foreach (string albumArtist in track.GetValues(TagField.AlbumArtist))
        {
            if (FindInText(albumArtist, context.Dictionary) is { } fromAlbumArtist)
            {
                return new ConductorHit(fromAlbumArtist, $"albumartist「{albumArtist}」から特定");
            }
        }

        // 3. 同一フォルダの他ファイルの conductor。
        foreach (TrackTags sibling in context.GetSiblings(track))
        {
            if (ReferenceEquals(sibling, track))
            {
                continue;
            }

            foreach (string conductor in sibling.GetValues(TagField.Conductor))
            {
                if (context.Dictionary.TryResolvePerson(conductor, out PersonEntry person)
                    && DictionaryIndex.HasRole(person, PersonRole.Conductor))
                {
                    return new ConductorHit(person.Canonical, $"同一フォルダの「{sibling.RelativePath}」から特定");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 文字列の中から指揮者名を探す。語単位と全体の両方で照合する。
    /// </summary>
    /// <param name="text">探索する文字列。</param>
    /// <param name="dictionary">辞書の索引。</param>
    /// <returns>見つかった指揮者の正規形。見つからなければ null。</returns>
    public static string? FindInText(string? text, DictionaryIndex dictionary)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (IsConductor(text, dictionary, out string? whole))
        {
            return whole;
        }

        string[] tokens = text.Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 「Herbert von Karajan」のような複数語の名前も拾えるよう、連続する語も試す。
        for (int length = Math.Min(3, tokens.Length); length >= 1; length--)
        {
            for (int start = 0; start + length <= tokens.Length; start++)
            {
                string candidate = string.Join(' ', tokens.Skip(start).Take(length));

                if (IsConductor(candidate, dictionary, out string? found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 値が指揮者として辞書にあるかを判定する。
    /// </summary>
    private static bool IsConductor(string value, DictionaryIndex dictionary, out string? canonical)
    {
        canonical = null;

        if (!dictionary.TryResolvePerson(value, out PersonEntry person))
        {
            return false;
        }

        if (!DictionaryIndex.HasRole(person, PersonRole.Conductor))
        {
            return false;
        }

        canonical = person.Canonical;
        return true;
    }
}
