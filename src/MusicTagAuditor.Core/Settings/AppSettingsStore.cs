using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MusicTagAuditor.Core.Settings;

/// <summary>
/// <c>settings.json</c> の読み書き。現在の設定を 1 箇所に集約して持つ。
///
/// **読み込みで失敗しても例外を投げない。** 設定は失っても入力し直せるが、
/// 壊れた JSON で起動できなくなると利用者には手の打ちようがない。
/// 失敗した理由は <see cref="LoadError"/> に残し、呼び出し側がログに出す
/// （Core はログ基盤に依存しない）。
/// </summary>
public sealed class AppSettingsStore
{
    /// <summary>設定ファイル名。</summary>
    public const string SETTINGS_FILE_NAME = "settings.json";

    /// <summary>書き込み中の一時ファイルの拡張子。</summary>
    private const string TEMP_SUFFIX = ".tmp";

    /// <summary>
    /// 書き出し設定。日本語のパスを <c>\uXXXX</c> に落とさない
    /// （<see cref="Dictionary.DictionaryWriter"/> と同じ理由）。
    /// </summary>
    private static readonly JsonSerializerOptions WRITE_OPTIONS = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = AppSettingsJsonContext.Default,
    };

    /// <summary>設定を置くフォルダ。</summary>
    private readonly string _directory;

    /// <summary>現在の設定。</summary>
    private AppSettings _current;

    /// <summary>
    /// 設定を読み込んでストアを作る。ファイルが無ければ既定値で始める。
    /// </summary>
    /// <param name="directory">設定を置くフォルダ（<c>%APPDATA%\MusicTagAuditor</c>）。</param>
    public AppSettingsStore(string directory)
    {
        _directory = directory;
        _current = Load(GetSettingsPath(directory), out string? loadError);
        LoadError = loadError;
    }

    /// <summary>現在の設定。</summary>
    public AppSettings Current => _current;

    /// <summary>設定ファイルのパス。</summary>
    public string FilePath => GetSettingsPath(_directory);

    /// <summary>
    /// 読み込みに失敗した理由。既定値へフォールバックしたときだけ入る。
    /// 起動を止めない代わりに、原因を追えるよう呼び出し側でログに出すこと。
    /// </summary>
    public string? LoadError { get; private set; }

    /// <summary>
    /// 設定ファイルのパスを組み立てる。
    /// </summary>
    /// <param name="directory">設定を置くフォルダ。</param>
    /// <returns>設定ファイルの絶対パス。</returns>
    public static string GetSettingsPath(string directory)
    {
        return Path.Combine(directory, SETTINGS_FILE_NAME);
    }

    /// <summary>
    /// 設定を保存する。一時ファイル経由で差し替える。
    /// </summary>
    /// <param name="settings">保存する設定。</param>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_directory);

        string path = FilePath;
        string tempPath = path + TEMP_SUFFIX;

        // BOM は付けない。JSON の規格上は不要で、他のツールから読むときに邪魔になる。
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(settings, WRITE_OPTIONS),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        File.Move(tempPath, path, overwrite: true);

        _current = settings;
        LoadError = null;
    }

    /// <summary>
    /// ファイルから読み直す。
    /// </summary>
    public void Reload()
    {
        _current = Load(FilePath, out string? loadError);
        LoadError = loadError;
    }

    /// <summary>
    /// 設定ファイルを読む。読めなければ既定値を返し、理由を <paramref name="loadError"/> に入れる。
    /// </summary>
    private static AppSettings Load(string path, out string? loadError)
    {
        loadError = null;

        if (!File.Exists(path))
        {
            return AppSettings.Default;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);

            return JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings)
                ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            loadError = $"設定を読めないため既定値で起動しました: {path}（{ex.Message}）";
            return AppSettings.Default;
        }
    }
}
