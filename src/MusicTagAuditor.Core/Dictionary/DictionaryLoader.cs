using System.Reflection;
using System.Text.Json;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 正規化辞書の読み込み。
///
/// 保存場所は <c>%APPDATA%\MusicTagAuditor\dictionary.json</c>。
/// 初回起動時にアプリ同梱の既定辞書をコピーする（docs/SPEC.md 7.1）。
/// </summary>
public sealed class DictionaryLoader
{
    /// <summary>既定辞書のリソース名の末尾。</summary>
    private const string DEFAULT_RESOURCE_SUFFIX = "default-dictionary.json";

    /// <summary>利用者辞書のファイル名。</summary>
    public const string USER_DICTIONARY_FILE_NAME = "dictionary.json";

    /// <summary>コメント（<c>_</c> 始まりのプロパティ）を許すシリアライズ設定。</summary>
    private static readonly JsonSerializerOptions READ_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        TypeInfoResolver = DictionaryJsonContext.Default,
    };

    /// <summary>
    /// アプリ同梱の既定辞書を読み込む。
    /// </summary>
    /// <returns>既定辞書。</returns>
    /// <exception cref="InvalidOperationException">同梱リソースが見つからない場合。</exception>
    public static TagDictionary LoadDefault()
    {
        Assembly assembly = typeof(DictionaryLoader).Assembly;

        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(DEFAULT_RESOURCE_SUFFIX, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("既定辞書が同梱されていません。");

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"既定辞書を開けません: {resourceName}");

        return Deserialize(stream, resourceName);
    }

    /// <summary>
    /// 利用者辞書のパスを組み立てる。
    /// </summary>
    /// <param name="directory">辞書を置くフォルダ（<c>%APPDATA%\MusicTagAuditor</c>）。</param>
    /// <returns>辞書ファイルの絶対パス。</returns>
    public static string GetUserDictionaryPath(string directory)
    {
        return Path.Combine(directory, USER_DICTIONARY_FILE_NAME);
    }

    /// <summary>
    /// 利用者辞書を読み込む。存在しなければ既定辞書をコピーして作る。
    /// </summary>
    /// <param name="directory">辞書を置くフォルダ（<c>%APPDATA%\MusicTagAuditor</c>）。</param>
    /// <returns>読み込んだ辞書。</returns>
    public static TagDictionary LoadOrCreate(string directory)
    {
        string path = GetUserDictionaryPath(directory);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(directory);
            WriteDefaultTo(path);
        }

        using FileStream stream = File.OpenRead(path);

        return Deserialize(stream, path);
    }

    /// <summary>
    /// 既定辞書を指定パスへ書き出す。
    /// </summary>
    /// <param name="path">書き出し先。</param>
    private static void WriteDefaultTo(string path)
    {
        Assembly assembly = typeof(DictionaryLoader).Assembly;

        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(DEFAULT_RESOURCE_SUFFIX, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("既定辞書が同梱されていません。");

        using Stream resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"既定辞書を開けません: {resourceName}");

        using FileStream output = File.Create(path);
        resource.CopyTo(output);
    }

    /// <summary>
    /// JSON を辞書に変換する。
    /// </summary>
    private static TagDictionary Deserialize(Stream stream, string source)
    {
        return JsonSerializer.Deserialize<TagDictionary>(stream, READ_OPTIONS)
            ?? throw new InvalidDataException($"辞書を読み取れません: {source}");
    }
}
