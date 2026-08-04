using System.Text.Json.Serialization;

namespace MusicTagAuditor.Core.Settings;

/// <summary>
/// 利用者が選べるアプリの設定。
///
/// **既定値は「未指定」で表す。** 未指定と「たまたま既定と同じ値を選んだ」を
/// 区別できないと、既定の振る舞いを後から変えたときに設定済みの利用者だけ取り残される。
/// </summary>
/// <param name="BackupRoot">
/// バックアップの保存先。null ならライブラリ直下（従来の動作）。
/// </param>
public sealed record AppSettings(string? BackupRoot)
{
    /// <summary>何も設定していない状態。</summary>
    public static AppSettings Default { get; } = new(BackupRoot: null);
}

/// <summary>
/// 設定 JSON のシリアライズ設定。
/// System.Text.Json のソース生成を使う（docs/SPEC.md 3章）。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
