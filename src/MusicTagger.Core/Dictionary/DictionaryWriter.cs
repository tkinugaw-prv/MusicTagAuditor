using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MusicTagger.Core.Dictionary;

/// <summary>
/// 正規化辞書の書き出し。
///
/// 辞書は利用者が育てていく資産であり、書き込み中の異常終了で失うと痛い。
/// 一時ファイルへ書いてから差し替え、直前版を <c>.bak</c> として残す。
/// </summary>
public static class DictionaryWriter
{
    /// <summary>書き込み中の一時ファイルの拡張子。</summary>
    private const string TEMP_SUFFIX = ".tmp";

    /// <summary>直前版を残す拡張子。</summary>
    public const string BACKUP_SUFFIX = ".bak";

    /// <summary>
    /// 書き出し設定。
    ///
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> を使うのは、既定のエンコーダだと
    /// 日本語もウムラウトも <c>\uXXXX</c> に落ちて**辞書が人間に読めなくなる**ため。
    /// 出力先は HTML ではなく設定ファイルなので、この緩和で困らない。
    /// </summary>
    private static readonly JsonSerializerOptions WRITE_OPTIONS = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = DictionaryJsonContext.Default,
    };

    /// <summary>
    /// 辞書を JSON 文字列にする。
    /// </summary>
    /// <param name="dictionary">書き出す辞書。</param>
    /// <returns>JSON。</returns>
    public static string Serialize(TagDictionary dictionary)
    {
        return JsonSerializer.Serialize(dictionary, WRITE_OPTIONS);
    }

    /// <summary>
    /// 辞書をファイルに書き出す。一時ファイル経由で差し替え、直前版を <c>.bak</c> に残す。
    /// </summary>
    /// <param name="path">書き出し先。</param>
    /// <param name="dictionary">書き出す辞書。</param>
    public static void WriteFile(string path, TagDictionary dictionary)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + TEMP_SUFFIX;

        // BOM は付けない。JSON の規格上は不要で、他のツールから読むときに邪魔になる。
        File.WriteAllText(tempPath, Serialize(dictionary), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, path + BACKUP_SUFFIX);
            return;
        }

        File.Move(tempPath, path);
    }
}
