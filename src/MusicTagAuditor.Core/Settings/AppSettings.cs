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
/// <param name="LastLibraryRoot">
/// 前回開いていたライブラリ。null なら未指定（起動時は何も開かない）。
/// </param>
/// <param name="PathsCollapsed">
/// ライブラリ・バックアップ先のパス欄を畳んでいるか（docs/SPEC.md 5.1）。
///
/// **「展開しているか」ではなく「畳んでいるか」で持つ。** bool の既定値 false が
/// 「展開」に当たるので、この項目を持たない古い settings.json を読んでも
/// 従来どおりの見た目で開く。逆向きに持つと、既存の利用者が全員
/// 畳んだ状態で起動することになる。
/// </param>
public sealed record AppSettings(string? BackupRoot, string? LastLibraryRoot, bool PathsCollapsed = false)
{
    /// <summary>何も設定していない状態。</summary>
    public static AppSettings Default { get; } =
        new(BackupRoot: null, LastLibraryRoot: null, PathsCollapsed: false);
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
