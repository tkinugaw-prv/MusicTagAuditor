using System.Text;
using MusicTagAuditor.TagIo.Mp4;
using MusicTagAuditor.TagIo.Tests.Fixtures;

namespace MusicTagAuditor.TagIo.Tests;

/// <summary>
/// 自前の MP4 atom リーダーのテスト。
/// TagLib# を使わずに読む理由（<c>;</c> の分割状態を区別するため）が守られているかを確認する。
/// </summary>
public sealed class Mp4AtomReaderTests
{
    /// <summary>
    /// 指揮者が <c>©con</c> から読めることを確認する。
    /// </summary>
    [Fact]
    public void ReadsConductorFromCopyrightConAtom()
    {
        byte[] file = MinimalAudioFileBuilder.BuildM4a(
        [
            (TagIoConst.ATOM_CONDUCTOR, MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Yevgeny Mravinsky")]),
        ]);

        IReadOnlyList<Mp4Atom> atoms = Mp4AtomReader.Parse(file);

        Mp4Atom conductor = Assert.Single(atoms);
        Assert.Equal(TagIoConst.ATOM_CONDUCTOR, conductor.Name);
        Assert.Equal("A9636F6E", conductor.NameHex);
        Assert.Equal(["Yevgeny Mravinsky"], conductor.Values);
    }

    /// <summary>
    /// 1 つの値に <c>;</c> が含まれる状態を、分割せずに 1 値として読むことを確認する。
    /// ここを取り違えると R-205 の検出が成立しない。
    /// </summary>
    [Fact]
    public void KeepsSemicolonValueAsSingleValue()
    {
        const string VALUE = "Peter Pears(T); Hermann Prey(BR)";

        byte[] file = MinimalAudioFileBuilder.BuildM4a(
        [
            ("aART", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8(VALUE)]),
        ]);

        Mp4Atom albumArtist = Assert.Single(Mp4AtomReader.Parse(file));

        Assert.Equal([VALUE], albumArtist.Values);
    }

    /// <summary>
    /// AIMP が分割した状態（data ボックスが複数）を、複数値として読むことを確認する。
    /// 前のテストと合わせて、両者が区別できていることを保証する。
    /// </summary>
    [Fact]
    public void ReadsMultipleDataBoxesAsMultipleValues()
    {
        byte[] file = MinimalAudioFileBuilder.BuildM4a(
        [
            ("aART", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Peter Pears(T)"), Utf8("Hermann Prey(BR)")]),
        ]);

        Mp4Atom albumArtist = Assert.Single(Mp4AtomReader.Parse(file));

        Assert.Equal(["Peter Pears(T)", "Hermann Prey(BR)"], albumArtist.Values);
    }

    /// <summary>
    /// フリーフォーム atom が <c>----:mean:name</c> の形で識別できることを確認する。
    /// </summary>
    [Fact]
    public void IdentifiesFreeformAtomByMeanAndName()
    {
        byte[] file = BuildFreeformM4a("com.apple.iTunes", "iTunNORM", " 000001F4 00000216");

        Mp4Atom freeform = Assert.Single(Mp4AtomReader.Parse(file));

        Assert.Equal("----:com.apple.iTunes:iTunNORM", freeform.Name);
        Assert.Equal([" 000001F4 00000216"], freeform.Values);
    }

    /// <summary>
    /// テキストでない data ボックスが 16 進表記で読めることを確認する。
    /// </summary>
    [Fact]
    public void ReadsBinaryDataBoxAsHexString()
    {
        byte[] payload = [0x00, 0x00, 0x00, 0x03, 0x00, 0x06, 0x00, 0x00];

        byte[] file = MinimalAudioFileBuilder.BuildM4a(
        [
            ("trkn", MinimalAudioFileBuilder.DATA_TYPE_BINARY, [payload]),
        ]);

        Mp4Atom track = Assert.Single(Mp4AtomReader.Parse(file));

        Assert.Equal([TagIoConst.BINARY_VALUE_PREFIX + "0000000300060000"], track.Values);
    }

    /// <summary>
    /// タグ領域が無いファイルでも例外を投げずに空を返すことを確認する。
    /// </summary>
    [Fact]
    public void ReturnsEmptyWhenNoTagAtomsExist()
    {
        Assert.Empty(Mp4AtomReader.Parse(MinimalAudioFileBuilder.BuildM4a([])));
    }

    /// <summary>
    /// mean / name を持つフリーフォーム atom を含む M4A を組み立てる。
    /// </summary>
    private static byte[] BuildFreeformM4a(string mean, string name, string value)
    {
        // MinimalAudioFileBuilder は mean/name を扱わないため、ここで直接組み立てる。
        List<byte> atomPayload = [];
        atomPayload.AddRange(BuildFullBox("mean", Encoding.UTF8.GetBytes(mean)));
        atomPayload.AddRange(BuildFullBox("name", Encoding.UTF8.GetBytes(name)));

        List<byte> dataPayload = [];
        dataPayload.AddRange([0, 0, 0, MinimalAudioFileBuilder.DATA_TYPE_UTF8]);
        dataPayload.AddRange([0, 0, 0, 0]);
        dataPayload.AddRange(Encoding.UTF8.GetBytes(value));
        atomPayload.AddRange(BuildRawBox("data", [.. dataPayload]));

        byte[] freeform = BuildRawBox(TagIoConst.ATOM_FREEFORM, [.. atomPayload]);

        return WrapInContainers(freeform);
    }

    /// <summary>FullBox（version+flags の 4 バイトを持つボックス）を組み立てる。</summary>
    private static byte[] BuildFullBox(string name, byte[] payload)
    {
        List<byte> body = [0, 0, 0, 0];
        body.AddRange(payload);
        return BuildRawBox(name, [.. body]);
    }

    /// <summary>ボックスを組み立てる。</summary>
    private static byte[] BuildRawBox(string name, byte[] payload)
    {
        List<byte> box = [];
        box.AddRange(ToBigEndian(8 + payload.Length));
        box.AddRange(MinimalAudioFileBuilder.EncodeAtomName(name));
        box.AddRange(payload);
        return [.. box];
    }

    /// <summary>atom を moov/udta/meta/ilst の階層で包む。</summary>
    private static byte[] WrapInContainers(byte[] atom)
    {
        byte[] ilst = BuildRawBox("ilst", atom);

        List<byte> metaPayload = [0, 0, 0, 0];
        metaPayload.AddRange(ilst);

        byte[] meta = BuildRawBox("meta", [.. metaPayload]);
        byte[] udta = BuildRawBox("udta", meta);
        return BuildRawBox("moov", udta);
    }

    /// <summary>32bit 整数をビッグエンディアンのバイト列にする。</summary>
    private static byte[] ToBigEndian(int value)
    {
        return [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    /// <summary>文字列を UTF-8 バイト列にする。</summary>
    private static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }
}
