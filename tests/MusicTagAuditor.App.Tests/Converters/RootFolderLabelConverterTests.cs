using System.Globalization;
using MusicTagAuditor.App.Converters;

namespace MusicTagAuditor.App.Tests.Converters;

/// <summary>
/// <see cref="RootFolderLabelConverter"/> のテスト。
///
/// 守りたいのは「ルート直下と値なしが画面で見分けられる」の一点。
/// ここが壊れると、ファイル一覧のフォルダ欄が空欄に戻り、ルート直下のファイルなのか
/// 値を取り損ねたのかが利用者から区別できなくなる。
/// </summary>
public sealed class RootFolderLabelConverterTests
{
    /// <summary>テスト対象。</summary>
    private readonly RootFolderLabelConverter _converter = new();

    /// <summary>
    /// ルート直下は FolderPath が空文字になる。ここで目印を出せないと空欄のままになる。
    /// </summary>
    [Fact]
    public void Convert_空文字はルートの目印になる()
    {
        Assert.Equal("(root)", _converter.Convert(string.Empty, typeof(string), null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// バインドの空振りで null が来ても空欄にはしない。空欄では意味が読み取れない。
    /// </summary>
    [Fact]
    public void Convert_nullもルートの目印になる()
    {
        Assert.Equal("(root)", _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 実在のフォルダは触らない。ここで加工すると、画面のパスと実際のパスが食い違う。
    /// </summary>
    [Fact]
    public void Convert_フォルダのある行はそのまま返す()
    {
        Assert.Equal(
            @"Bach\Cantatas",
            _converter.Convert(@"Bach\Cantatas", typeof(string), null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 逆変換は使わない。黙って通すと、表示用の "(root)" が値として書き戻されうる。
    /// </summary>
    [Fact]
    public void ConvertBack_使えない()
    {
        Assert.Throws<NotSupportedException>(
            () => _converter.ConvertBack("(root)", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
