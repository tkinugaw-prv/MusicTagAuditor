namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリを使うテストで参照する定数。
/// docs/llm_guideline.md の規約により、環境変数名はハードコードせずここにまとめる。
/// </summary>
public static class IntegrationConst
{
    /// <summary>実ライブラリのパスを指定する環境変数名。</summary>
    public const string ENV_LIBRARY_ROOT = "MUSICTAGAUDITOR_LIBRARY_ROOT";

    /// <summary>
    /// 実ライブラリのパスを解決する。
    /// 既定値は持たない。特定の環境のパスを埋め込むと他の環境で意味を成さないうえ、
    /// 公開リポジトリに個人のフォルダ構成が残るため。
    /// </summary>
    /// <returns>環境変数で指定されたライブラリのルートパス。未設定なら <c>null</c>。</returns>
    public static string? ResolveLibraryRoot()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(ENV_LIBRARY_ROOT);

        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    /// <summary>
    /// 実ライブラリのパスを取得する。<see cref="RealLibraryFactAttribute"/> を付けたテストの
    /// 本体から呼ぶ。属性側でスキップ判定を済ませているため、ここに来る時点でパスは必ず存在する。
    /// </summary>
    /// <returns>ライブラリのルートパス。</returns>
    /// <exception cref="InvalidOperationException">環境変数が未設定の場合。</exception>
    public static string RequireLibraryRoot()
    {
        return ResolveLibraryRoot()
            ?? throw new InvalidOperationException(
                $"{ENV_LIBRARY_ROOT} が未設定である。{nameof(RealLibraryFactAttribute)} を付け忘れていないか確認すること。");
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
        string? root = IntegrationConst.ResolveLibraryRoot();

        if (root is null)
        {
            Skip = $"実ライブラリが未指定のためスキップした（環境変数 {IntegrationConst.ENV_LIBRARY_ROOT} にライブラリのルートを設定すると実行される）";
        }
        else if (!Directory.Exists(root))
        {
            Skip = $"実ライブラリが見つからないためスキップした（{IntegrationConst.ENV_LIBRARY_ROOT} で指定）: {root}";
        }
    }
}
