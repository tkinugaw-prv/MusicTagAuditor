using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Models;

/// <summary>
/// <see cref="AudioFormatConst"/> のテスト。
/// 利用者に見せる文面へ出るため、表記そのものを固定する。
/// </summary>
public sealed class AudioFormatConstTests
{
    /// <summary>
    /// 形式が拡張子の並びとして表示されることを確認する。
    /// </summary>
    [Theory]
    [InlineData(AudioFormat.M4a, ".m4a")]
    [InlineData(AudioFormat.Flac, ".flac")]
    [InlineData(AudioFormat.Id3, ".mp3 / .aif / .aiff")]
    public void LabelsFormatByExtension(AudioFormat format, string expected)
    {
        Assert.Equal(expected, AudioFormatConst.Label(format));
    }

    /// <summary>
    /// すべての形式に拡張子が登録されていることを確認する。
    /// 登録が漏れると enum 名がそのまま利用者に出る。
    /// </summary>
    [Fact]
    public void CoversEveryFormat()
    {
        foreach (AudioFormat format in Enum.GetValues<AudioFormat>())
        {
            Assert.NotEmpty(AudioFormatConst.Extensions(format));
        }
    }
}
