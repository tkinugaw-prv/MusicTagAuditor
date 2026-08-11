namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 辞書の構成を 1 行で表す。
///
/// **どの辞書を読んでいるのかをログとテスト出力に必ず残すため**に用意した。
/// 辞書は所蔵に依存し（作品エントリは同梱の既定辞書に入らない。docs/SPEC.md 13章 D5）、
/// 実行環境によって別のファイルを読むこともある。件数が見えないと、検出結果が想定と違うときに
/// 「ルールの誤り」なのか「別の辞書を読んでいる」のかを切り分けられない。
///
/// 実際に、手で編集した辞書とアプリが読む辞書が別物になっていて、R-504 が全件保留になった
/// 原因の特定に時間がかかった（2026-08-12）。
/// </summary>
public static class DictionarySummary
{
    /// <summary>
    /// 構成を 1 行にまとめる。
    /// </summary>
    /// <param name="dictionary">対象の辞書。</param>
    /// <returns>件数を並べた文字列。</returns>
    public static string Describe(TagDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        // 各項目は null になりうる。JSON に `"works": null` と書かれていると
        // プロパティ初期化子ではなく null が入る（本体が書いた辞書に実在した）。
        return $"版={dictionary.Version}"
            + $" 作曲家={Count(dictionary.Composers)}"
            + $" 人物={Count(dictionary.Persons)}"
            + $" 団体={Count(dictionary.Ensembles)}"
            + $" 誤記={Count(dictionary.Typos)}"
            + $" 作品={Count(dictionary.Works)}"
            + $" 個別例外={Count(dictionary.AlbumOverrides)}"
            + $" 保護対象={Count(dictionary.ProtectedAlbumArtists)}";
    }

    /// <summary>
    /// null を 0 として数える。
    /// </summary>
    private static int Count<T>(IReadOnlyList<T>? values)
    {
        return values?.Count ?? 0;
    }
}
