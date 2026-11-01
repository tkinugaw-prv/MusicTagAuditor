using System.Reflection;

namespace MusicTagAuditor.App;

/// <summary>
/// 画面とログに出すアプリの版数（docs/SPEC.md 5.6）。
///
/// **ソースに版数を書かない。** リリースタグは main へのマージ後に決まり、CI がタグの値を
/// <c>-p:Version=</c> で exe に埋める。ここではそれを実行時に読むだけなので、版数の情報源は
/// タグ 1 つで済み、リリースのたびにソースを直す手順も増えない。
/// </summary>
public static class AppVersion
{
    /// <summary>タグを経ていないビルドの版数の末尾。Directory.Build.props の基準値と揃える。</summary>
    private const string DEVELOPMENT_SUFFIX = "-dev";

    /// <summary>開発版に添えるコミットハッシュの桁数。</summary>
    private const int SHORT_HASH_LENGTH = 7;

    /// <summary>開発版の表示名。</summary>
    private const string DEVELOPMENT_LABEL = "開発版";

    /// <summary>実行中のアプリの版数の表示名。</summary>
    public static string Current { get; } = Describe(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// 情報バージョンを表示名にする。リリース版は <c>v1.10.0</c>、開発版は <c>開発版 (be7791e)</c> の形。
    ///
    /// 開発版の判定を「- を含むか」にしない。<c>v2.0.0-rc.1</c> のようなタグを切ると、
    /// 配布物なのに開発版と表示してしまう。
    /// </summary>
    /// <param name="informationalVersion"><c>1.10.0+コミットハッシュ</c> の形の情報バージョン。</param>
    /// <returns>表示名。</returns>
    public static string Describe(string? informationalVersion)
    {
        string[] parts = (informationalVersion ?? string.Empty).Split('+', 2);
        string version = parts[0];

        if (version.Length > 0 && !version.EndsWith(DEVELOPMENT_SUFFIX, StringComparison.Ordinal))
        {
            return $"v{version}";
        }

        if (parts.Length < 2 || parts[1].Length == 0)
        {
            return DEVELOPMENT_LABEL;
        }

        string hash = parts[1];
        return $"{DEVELOPMENT_LABEL} ({hash[..Math.Min(hash.Length, SHORT_HASH_LENGTH)]})";
    }
}
