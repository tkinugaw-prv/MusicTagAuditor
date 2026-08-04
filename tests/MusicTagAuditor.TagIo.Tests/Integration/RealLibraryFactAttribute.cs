namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリを使うテストで参照する定数。
/// docs/llm_guideline.md の規約により、環境変数名はハードコードせずここにまとめる。
/// </summary>
public static class IntegrationConst
{
    /// <summary>実ライブラリのパスを差し替える環境変数名。</summary>
    public const string ENV_LIBRARY_ROOT = "MUSICTAGGER_LIBRARY_ROOT";

    /// <summary>環境変数が無い場合に使う既定のライブラリパス。</summary>
    public const string DEFAULT_LIBRARY_ROOT = @"D:\Music Library for AIMP\Classic";

    /// <summary>
    /// 実ライブラリのパスを解決する。
    /// </summary>
    /// <returns>ライブラリのルートパス。</returns>
    public static string ResolveLibraryRoot()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(ENV_LIBRARY_ROOT);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? DEFAULT_LIBRARY_ROOT : fromEnvironment;
    }
}

/// <summary>
/// 実ライブラリが存在する環境でのみ実行するテスト。
/// CI には実ライブラリが無いため自動的にスキップされる。
/// </summary>
public sealed class RealLibraryFactAttribute : FactAttribute
{
    /// <summary>
    /// 実ライブラリの有無を見てスキップ理由を設定する。
    /// </summary>
    public RealLibraryFactAttribute()
    {
        string root = IntegrationConst.ResolveLibraryRoot();

        if (!Directory.Exists(root))
        {
            Skip = $"実ライブラリが見つからないためスキップした（{IntegrationConst.ENV_LIBRARY_ROOT} で指定可能）: {root}";
        }
    }
}
