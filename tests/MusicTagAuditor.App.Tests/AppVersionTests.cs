namespace MusicTagAuditor.App.Tests;

/// <summary>
/// <see cref="AppVersion"/> のテスト。
/// </summary>
public sealed class AppVersionTests
{
    /// <summary>リリース版はコミットハッシュを落として v を付ける。</summary>
    [Theory]
    [InlineData("1.10.0+be7791e0123456789", "v1.10.0")]
    [InlineData("1.10.0", "v1.10.0")]
    public void DescribesReleaseVersion(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.Describe(informational));
    }

    /// <summary>プレリリースのタグは配布物なので、開発版として扱わない。</summary>
    [Fact]
    public void DescribesPrereleaseTagAsRelease()
    {
        Assert.Equal("v2.0.0-rc.1", AppVersion.Describe("2.0.0-rc.1+be7791e0123456789"));
    }

    /// <summary>開発版は短縮したコミットハッシュを添える。</summary>
    [Theory]
    [InlineData("0.0.0-dev+be7791e0123456789", "開発版 (be7791e)")]
    [InlineData("0.0.0-dev+abc", "開発版 (abc)")]
    public void DescribesDevelopmentBuildWithHash(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.Describe(informational));
    }

    /// <summary>ハッシュや版数が取れなくても開発版として表示する（git の無い環境でのビルドなど）。</summary>
    [Theory]
    [InlineData("0.0.0-dev")]
    [InlineData("0.0.0-dev+")]
    [InlineData("")]
    [InlineData(null)]
    public void DescribesDevelopmentBuildWithoutHash(string? informational)
    {
        Assert.Equal("開発版", AppVersion.Describe(informational));
    }
}
