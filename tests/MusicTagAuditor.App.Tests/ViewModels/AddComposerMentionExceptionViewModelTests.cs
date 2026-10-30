using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 「この語句を例外に登録」ダイアログのテスト（docs/SPEC.md 7.3.2 / docs/TAGGING_POLICY.md 6.9）。
///
/// 守りたいのは、**検出を止めすぎる語句を登録させない**ことと、**押した行に効かないことを
/// 登録前に伝える**こと。
/// </summary>
public sealed class AddComposerMentionExceptionViewModelTests
{
    /// <summary>検証に使う辞書。</summary>
    private static readonly TagDictionary DICTIONARY = DictionaryLoader.LoadDefault();

    /// <summary>
    /// 語句の初期値から先頭の番号を外すことを確認する。録音ごとに違う部分を残すと別の録音に効かない。
    /// </summary>
    [Theory]
    [InlineData("06 2. Szene am Bach", "Szene am Bach")]
    [InlineData("02 2 Szene am Bach.", "Szene am Bach")]
    [InlineData("Szene am Bach", "Szene am Bach")]
    public void SuggestsPhraseWithoutLeadingNumbers(string value, string expected)
    {
        Assert.Equal(expected, AddComposerMentionExceptionViewModel.SuggestPhrase(value));
    }

    /// <summary>
    /// 作曲家を明細から固定で埋め、理由が入るまで登録させないことを確認する。
    /// </summary>
    [Fact]
    public void FillsComposerAndRequiresNote()
    {
        AddComposerMentionExceptionViewModel viewModel = Create();

        Assert.Equal("Ludwig van Beethoven", viewModel.Composer);
        Assert.Equal("Szene am Bach", viewModel.Phrase);

        Assert.False(viewModel.CanApply(out string reason));
        Assert.Contains("理由", reason, StringComparison.Ordinal);

        viewModel.Note = "小川";

        Assert.True(viewModel.CanApply(out _));

        ComposerMentionExceptionEntry entry = Assert.Single(viewModel.Apply().ComposerMentionExceptions);

        Assert.Equal("Szene am Bach", entry.Phrase);
        Assert.Equal("小川", entry.Note);
    }

    /// <summary>
    /// 検出を止めすぎる語句と、何も起きない語句を登録させないことを確認する。
    /// </summary>
    [Theory]
    [InlineData("Bach", "2 語以上")]
    [InlineData("Bach Weber", "作曲家名以外")]
    [InlineData("Allegro con brio", "別の作曲家名")]
    public void RejectsInvalidPhrase(string phrase, string expected)
    {
        AddComposerMentionExceptionViewModel viewModel = Create();

        viewModel.Note = "小川";
        viewModel.Phrase = phrase;

        Assert.False(viewModel.CanApply(out string reason));
        Assert.Contains(expected, reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// 語句がこの行に効くかを、入力に応じて注意書きへ出すことを確認する。
    /// 同じ値にある別の作曲家名は残るので、登録しても行は消えない。
    /// </summary>
    [Fact]
    public void NoticeTellsWhetherPhraseClearsThisRow()
    {
        AddComposerMentionExceptionViewModel viewModel = Create("x/06 Weber Szene am Bach.flac");

        viewModel.Phrase = "Szene am Bach";

        Assert.Contains("Carl Maria von Weber", viewModel.Notice, StringComparison.Ordinal);

        viewModel = Create();

        Assert.Contains("消えます", viewModel.Notice, StringComparison.Ordinal);
    }

    /// <summary>
    /// ダイアログを作る。
    /// </summary>
    private static AddComposerMentionExceptionViewModel Create(string path = "x/06 2. Szene am Bach.flac")
    {
        string normalized = path.Replace('/', Path.DirectorySeparatorChar);

        TrackTags track = new()
        {
            RelativePath = normalized,
            FullPath = Path.Combine(Path.GetTempPath(), normalized),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
            [
                new KeyValuePair<TagField, IReadOnlyList<string>>(TagField.Composer, ["Ludwig van Beethoven"]),
            ]),
            RawTags = new Dictionary<string, string[]>(),
        };

        ComposerMismatchHit hit = ComposerMismatch.Find(track, new DictionaryIndex(DICTIONARY), [track])!;

        return new AddComposerMentionExceptionViewModel(DICTIONARY, track, [track], hit);
    }
}
