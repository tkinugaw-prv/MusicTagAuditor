namespace MusicTagAuditor.App.Tests;

/// <summary>
/// エクスプローラー起動のテスト。
///
/// 実際に起動する部分は試せないので、**一度踏んだ落とし穴だけを固定する。**
/// 引数を <c>ArgumentList</c> で渡すと <c>/select,&lt;パス&gt;</c> 全体が 1 個の引数として
/// 引用され、エクスプローラーが解釈できずマイドキュメントが開く。
/// </summary>
public sealed class ExplorerLauncherTests
{
    /// <summary>
    /// 選択して開く引数が <c>/select,</c> と引用符で組まれることを確認する。
    /// </summary>
    [Fact]
    public void BuildsSelectArguments()
    {
        Assert.Equal(
            @"/select,""D:\Music\ブルックナー\01.flac""",
            ExplorerLauncher.BuildSelectArguments(@"D:\Music\ブルックナー\01.flac"));
    }

    /// <summary>
    /// 空白を含むパスが 1 個の引数として渡ることを確認する。
    /// 引用が外れると、空白の手前までしかエクスプローラーに届かない。
    /// </summary>
    [Fact]
    public void QuotesPathContainingSpace()
    {
        string arguments = ExplorerLauncher.BuildSelectArguments(@"D:\Music Library\01.flac");

        Assert.Equal(@"/select,""D:\Music Library\01.flac""", arguments);
    }
}
