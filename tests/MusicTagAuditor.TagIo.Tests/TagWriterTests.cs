using System.Text;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.TagIo.Mp4;
using MusicTagAuditor.TagIo.Tests.Fixtures;

namespace MusicTagAuditor.TagIo.Tests;

/// <summary>
/// タグ書き込みのテスト。
/// docs/adr/0001-tag-io-library.md の制約（©con に書く / <c>;</c> を分割しない）を固定する。
/// </summary>
public sealed class TagWriterTests : IDisposable
{
    /// <summary>検体を置く一時フォルダ。</summary>
    private readonly string _workDir;

    /// <summary>テスト対象。</summary>
    private readonly TagWriter _writer = new();

    /// <summary>照合用の読み取り。</summary>
    private readonly TagReader _reader = new();

    /// <summary>
    /// 検体用の一時フォルダを用意する。
    /// </summary>
    public TagWriterTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "MusicTagAuditor.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    /// <summary>
    /// 一時フォルダを削除する。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_workDir))
        {
            Directory.Delete(_workDir, recursive: true);
        }
    }

    /// <summary>
    /// M4A の指揮者が <c>©con</c> に書かれ、<c>cond</c> が作られないことを確認する。
    /// **これが崩れると AIMP から指揮者が見えなくなる。** 本テストは退行の最後の砦。
    /// </summary>
    [Fact]
    public void WritesM4aConductorToCopyrightConAtomOnly()
    {
        string path = CreateM4a();

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Conductor] = ["Yevgeny Mravinsky"],
        });

        IReadOnlyList<Mp4Atom> atoms = Mp4AtomReader.Read(path);

        Mp4Atom conductor = Assert.Single(atoms, atom => atom.Name == TagIoConst.ATOM_CONDUCTOR);
        Assert.Equal(["Yevgeny Mravinsky"], conductor.Values);

        Assert.DoesNotContain(atoms, atom => atom.Name == TagIoConst.ATOM_CONDUCTOR_WRONG);
    }

    /// <summary>
    /// M4A の各フィールドが往復することを確認する。
    /// </summary>
    [Fact]
    public void RoundTripsM4aFields()
    {
        string path = CreateM4a();

        Dictionary<TagField, IReadOnlyList<string>> fields = new()
        {
            [TagField.Title] = ["Symphony No.8 - I. Allegro moderato"],
            [TagField.Artist] = ["Herbert von Karajan"],
            [TagField.AlbumArtist] = ["Wiener Philharmoniker"],
            [TagField.Composer] = ["Anton Bruckner"],
            [TagField.Conductor] = ["Herbert von Karajan"],
            [TagField.Album] = ["Bruckner: Symphony No.8"],
            [TagField.Genre] = ["Classic"],
            [TagField.Date] = ["1988"],
            [TagField.TrackNumber] = ["3/6"],
            [TagField.DiscNumber] = ["1/1"],
            [TagField.Comment] = ["Haas edition"],
        };

        _writer.Write(path, fields);

        TrackTags tags = _reader.Read(path, "test.m4a");

        Assert.Equal("Haas edition", tags.Comment);

        Assert.Equal("Symphony No.8 - I. Allegro moderato", tags.Title);
        Assert.Equal("Herbert von Karajan", tags.Artist);
        Assert.Equal("Wiener Philharmoniker", tags.AlbumArtist);
        Assert.Equal("Anton Bruckner", tags.Composer);
        Assert.Equal("Herbert von Karajan", tags.Conductor);
        Assert.Equal("Bruckner: Symphony No.8", tags.Album);
        Assert.Equal("Classic", tags.Genre);
        Assert.Equal("1988", tags.Date);
        Assert.Equal("3/6", tags.TrackNumber);
        Assert.Equal("1/1", tags.DiscNumber);
    }

    /// <summary>
    /// <c>;</c> を含む値が 1 値のまま格納されることを確認する。
    /// 2.3 の保護対象（配役情報）はまさに <c>;</c> を含む。分割すると情報が壊れる。
    /// </summary>
    [Theory]
    [InlineData("m4a")]
    [InlineData("flac")]
    [InlineData("mp3")]
    [InlineData("aif")]
    public void DoesNotSplitSemicolonValue(string extension)
    {
        const string PROTECTED_VALUE = "Kommerchor Stuttgart(Chorus); Karl Münchinger; Stuttgarter Kammerorchester";

        string path = CreateFile(extension);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.AlbumArtist] = [PROTECTED_VALUE],
        });

        TrackTags tags = _reader.Read(path, $"test.{extension}");

        Assert.Equal([PROTECTED_VALUE], tags.GetValues(TagField.AlbumArtist));
        Assert.False(tags.HasMultipleValues(TagField.AlbumArtist));
    }

    /// <summary>
    /// FLAC / MP3 / AIFF は分割済みの複数値を複数値のまま書き戻せることを確認する。
    /// 復元では「壊れた状態」も忠実に戻せる必要がある。
    /// </summary>
    [Theory]
    [InlineData("flac")]
    [InlineData("mp3")]
    [InlineData("aif")]
    public void WritesMultipleValuesAsMultipleValues(string extension)
    {
        string path = CreateFile(extension);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.AlbumArtist] = ["Peter Pears(T)", "Hermann Prey(BR)"],
        });

        TrackTags tags = _reader.Read(path, $"test.{extension}");

        Assert.Equal(["Peter Pears(T)", "Hermann Prey(BR)"], tags.GetValues(TagField.AlbumArtist));
    }

    /// <summary>
    /// **既知の制約**: M4A では複数値を書き分けられない。
    ///
    /// TagLib# の <c>AppleTag.SetText</c> は string[] を <c>"; "</c> で連結して 1 つの data ボックスに書く。
    /// そのため、AIMP が分割した状態（data ボックスが複数）をスナップショットから忠実に復元できない。
    ///
    /// 実害は限定的である。2026-08-03 時点で分割状態のファイルは 0 件であり
    /// （docs/library-baseline-2026-08-03.md）、分割状態は原則としては直すべき不具合でもある。
    /// また復元後の読み戻し照合で不一致として検出されるため、黙って壊れることはない。
    ///
    /// 本テストは、この挙動が意図せず変わったときに気づくために置いてある。
    /// </summary>
    [Fact]
    public void Mp4JoinsMultipleValuesIntoOne()
    {
        string path = CreateM4a();

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.AlbumArtist] = ["Peter Pears(T)", "Hermann Prey(BR)"],
        });

        TrackTags tags = _reader.Read(path, "test.m4a");

        Assert.Equal(["Peter Pears(T); Hermann Prey(BR)"], tags.GetValues(TagField.AlbumArtist));
    }

    /// <summary>
    /// 編集対象でないタグを保存時に失わないことを確認する（docs/SPEC.md 10章の RawTags 要件）。
    /// </summary>
    [Fact]
    public void PreservesUnrelatedTags()
    {
        string path = CreateM4a(
        [
            ("©too", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("iTunes v6.0.5.20")]),
            ("©grp", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("GROUPING")]),
        ]);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = ["Anton Bruckner"],
        });

        TrackTags tags = _reader.Read(path, "test.m4a");

        Assert.Equal(["iTunes v6.0.5.20"], tags.RawTags["©too"]);
        Assert.Equal(["GROUPING"], tags.RawTags["©grp"]);
        Assert.Equal("Anton Bruckner", tags.Composer);
    }

    /// <summary>
    /// 空の値を渡すとタグが削除されることを確認する。
    /// </summary>
    [Fact]
    public void RemovesFieldWhenValuesAreEmpty()
    {
        string path = CreateM4a(
        [
            ("©wrt", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Anton Bruckner")]),
        ]);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = [],
        });

        Assert.Null(_reader.Read(path, "test.m4a").Composer);
    }

    /// <summary>
    /// FLAC / MP3 / AIFF の指揮者が往復することを確認する。
    /// </summary>
    [Theory]
    [InlineData("flac")]
    [InlineData("mp3")]
    [InlineData("aif")]
    public void RoundTripsConductorForOtherFormats(string extension)
    {
        string path = CreateFile(extension);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Conductor] = ["Günter Wand"],
            [TagField.Composer] = ["Anton Bruckner"],
        });

        TrackTags tags = _reader.Read(path, $"test.{extension}");

        Assert.Equal("Günter Wand", tags.Conductor);
        Assert.Equal("Anton Bruckner", tags.Composer);
    }

    /// <summary>
    /// M4A / FLAC の <c>comment</c> が往復することを確認する（docs/TAGGING_POLICY.md 4.1）。
    /// </summary>
    [Theory]
    [InlineData("m4a")]
    [InlineData("flac")]
    public void RoundTripsComment(string extension)
    {
        string path = CreateFile(extension);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = ["ノヴァーク版 1890"],
        });

        Assert.Equal("ノヴァーク版 1890", _reader.Read(path, $"test.{extension}").Comment);
    }

    /// <summary>
    /// M4A の <c>comment</c> が <c>©cmt</c> に書かれることを確認する。
    /// AIMP のコメント欄はこの atom を読む。
    /// </summary>
    [Fact]
    public void WritesM4aCommentToCmtAtom()
    {
        string path = CreateM4a();

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = ["Haas edition"],
        });

        Mp4Atom comment = Assert.Single(Mp4AtomReader.Read(path), atom => atom.Name == "©cmt");
        Assert.Equal(["Haas edition"], comment.Values);
    }

    /// <summary>
    /// 他のフィールドを書いても既存の <c>comment</c> が残ることを確認する。
    /// </summary>
    [Theory]
    [InlineData("m4a")]
    [InlineData("flac")]
    public void PreservesExistingCommentWhenWritingOtherFields(string extension)
    {
        string path = CreateFile(extension);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = ["Haas edition"],
        });

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = ["Anton Bruckner"],
        });

        TrackTags tags = _reader.Read(path, $"test.{extension}");

        Assert.Equal("Haas edition", tags.Comment);
        Assert.Equal("Anton Bruckner", tags.Composer);
    }

    /// <summary>
    /// 空の値を渡すと <c>comment</c> が消えることを確認する。
    /// </summary>
    [Theory]
    [InlineData("m4a")]
    [InlineData("flac")]
    public void RemovesCommentWhenValuesAreEmpty(string extension)
    {
        string path = CreateFile(extension);

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = ["Haas edition"],
        });

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = [],
        });

        Assert.Null(_reader.Read(path, $"test.{extension}").Comment);
    }

    /// <summary>
    /// **ID3 の <c>COMM</c> フレームに一切触らないことを確認する。**
    ///
    /// ID3v2 では iTunes が <c>iTunNORM</c> / <c>iTunSMPB</c> / <c>iTunes_CDDB_IDs</c> を
    /// description 付きの <c>COMM</c> に格納する。実測（2026-08-15）では対象ライブラリの
    /// AIFF 11 件すべてがこの形だった。<c>comment</c> を ID3 の論理フィールドとして扱うと、
    /// 値を空にしたときの <c>RemoveFrames</c> が音量正規化情報ごと消す。
    /// <c>RawTags</c> は記録用で復元に使わないため、消えたら戻せない。
    ///
    /// 本テストは、うっかり <c>ID3_FRAME_BY_FIELD</c> に <c>COMM</c> を足したときに気づくために置く。
    /// </summary>
    [Theory]
    [InlineData("mp3")]
    [InlineData("aif")]
    public void DoesNotTouchId3CommentFrames(string extension)
    {
        string path = CreateFile(extension);
        AddId3ApplicationComments(path);

        // 値を入れる場合と消す場合の両方を通す。消す側が RemoveFrames を踏む。
        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = ["Haas edition"],
            [TagField.Composer] = ["Anton Bruckner"],
        });

        _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = [],
        });

        using TagLib.File file = TagLib.File.Create(path);
        TagLib.Id3v2.Tag id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);

        string[] descriptions =
        [
            .. id3.GetFrames().OfType<TagLib.Id3v2.CommentsFrame>().Select(frame => frame.Description),
        ];

        Assert.Equal(["iTunPGAP", "iTunes_CDDB_IDs", "iTunNORM"], descriptions);
        Assert.Equal("Anton Bruckner", _reader.Read(path, $"test.{extension}").Composer);
    }

    /// <summary>
    /// 対応していない拡張子では例外になることを確認する。
    /// </summary>
    [Fact]
    public void ThrowsForUnsupportedExtension()
    {
        string path = Path.Combine(_workDir, "test.wav");
        File.WriteAllBytes(path, [0x00]);

        Assert.Throws<NotSupportedException>(
            () => _writer.Write(path, new Dictionary<TagField, IReadOnlyList<string>>()));
    }

    /// <summary>
    /// 指定した拡張子の空検体を作る。
    /// </summary>
    private string CreateFile(string extension)
    {
        string path = Path.Combine(_workDir, $"test.{extension}");

        byte[] bytes = extension switch
        {
            "m4a" => MinimalAudioFileBuilder.BuildM4a([]),
            "flac" => MinimalAudioFileBuilder.BuildFlac(),
            "mp3" => MinimalAudioFileBuilder.BuildMp3(),
            "aif" => MinimalAudioFileBuilder.BuildAiff(),
            _ => throw new ArgumentException($"未対応の拡張子です: {extension}", nameof(extension)),
        };

        File.WriteAllBytes(path, bytes);

        return path;
    }

    /// <summary>
    /// 指定した atom を持つ M4A 検体を作る。
    /// </summary>
    private string CreateM4a(IEnumerable<(string Name, int TypeFlag, byte[][] Values)>? atoms = null)
    {
        string path = Path.Combine(_workDir, "test.m4a");
        File.WriteAllBytes(path, MinimalAudioFileBuilder.BuildM4a(atoms ?? []));
        return path;
    }

    /// <summary>文字列を UTF-8 バイト列にする。</summary>
    private static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }

    /// <summary>
    /// 実ライブラリの AIFF と同じ形で、iTunes の内部データを <c>COMM</c> に仕込む。
    /// description 付きであることが要点で、値そのものは実測から取っている。
    /// </summary>
    private static void AddId3ApplicationComments(string path)
    {
        using TagLib.File file = TagLib.File.Create(path);
        TagLib.Id3v2.Tag id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);

        (string Description, string Text)[] comments =
        [
            ("iTunPGAP", "0"),
            ("iTunes_CDDB_IDs", "11++"),
            ("iTunNORM", " 000001A5 00000174 00003186 00002A4A 0003EDE1 000A3552"),
        ];

        foreach ((string description, string text) in comments)
        {
            id3.AddFrame(new TagLib.Id3v2.CommentsFrame(description, "eng") { Text = text });
        }

        file.Save();
    }
}
