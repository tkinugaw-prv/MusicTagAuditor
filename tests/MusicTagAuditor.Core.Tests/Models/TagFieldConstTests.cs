using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Models;

/// <summary>
/// <see cref="TagFieldConst"/> のテスト。
/// フィールドの性質はここから検査・空欄検査・画面の判定へ波及するため、分類そのものを固定する。
/// </summary>
public sealed class TagFieldConstTests
{
    /// <summary>
    /// <c>comment</c> が自由記述に分類されることを確認する（docs/TAGGING_POLICY.md 2.4）。
    /// </summary>
    [Fact]
    public void ClassifiesCommentAsFreeText()
    {
        Assert.True(TagFieldConst.IsFreeText(TagField.Comment));
    }

    /// <summary>
    /// <c>comment</c> 以外は自由記述でないことを確認する。
    /// 誤って広げると、その分だけ検査が黙って手薄になる。
    /// </summary>
    [Fact]
    public void ClassifiesOtherFieldsAsInspectable()
    {
        foreach (TagField field in Enum.GetValues<TagField>().Where(field => field != TagField.Comment))
        {
            Assert.False(TagFieldConst.IsFreeText(field));
        }
    }

    /// <summary>
    /// ID3 では <c>comment</c> を扱わないことを確認する（docs/TAGGING_POLICY.md 4.4）。
    /// </summary>
    [Fact]
    public void DoesNotSupportCommentOnId3()
    {
        Assert.False(TagFieldConst.IsSupported(AudioFormat.Id3, TagField.Comment));
    }

    /// <summary>
    /// M4A / FLAC では <c>comment</c> を扱えることを確認する。
    /// </summary>
    [Theory]
    [InlineData(AudioFormat.M4a)]
    [InlineData(AudioFormat.Flac)]
    public void SupportsCommentOnM4aAndFlac(AudioFormat format)
    {
        Assert.True(TagFieldConst.IsSupported(format, TagField.Comment));
    }

    /// <summary>
    /// <c>comment</c> 以外は全形式で扱えることを確認する。
    /// 形式ごとの差はこの 1 つだけであり、増えたら意図的な変更のはずなのでここで気づく。
    /// </summary>
    [Fact]
    public void SupportsEveryOtherFieldOnAllFormats()
    {
        foreach (AudioFormat format in Enum.GetValues<AudioFormat>())
        {
            foreach (TagField field in Enum.GetValues<TagField>().Where(field => field != TagField.Comment))
            {
                Assert.True(TagFieldConst.IsSupported(format, field));
            }
        }
    }
}
