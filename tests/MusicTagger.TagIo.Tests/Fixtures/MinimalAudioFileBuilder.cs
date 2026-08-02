using System.Buffers.Binary;
using System.Text;

namespace MusicTagger.TagIo.Tests.Fixtures;

/// <summary>
/// テスト用の極小な音声ファイルを組み立てる。
///
/// 市販音源の断片をリポジトリに置かずに済ませるため、無音・最小構成のコンテナを生成する。
/// 生成物はテスト実行のたびに作られ、コミットしない。
/// </summary>
public static class MinimalAudioFileBuilder
{
    /// <summary>MP4 のボックスヘッダ長（サイズ4 + 型4）。</summary>
    private const int BOX_HEADER_SIZE = 8;

    /// <summary>data ボックスの型フラグ: UTF-8 テキスト。</summary>
    public const int DATA_TYPE_UTF8 = 1;

    /// <summary>data ボックスの型フラグ: バイナリ。</summary>
    public const int DATA_TYPE_BINARY = 0;

    /// <summary>
    /// 指定した atom を持つ最小の M4A を組み立てる。
    /// </summary>
    /// <param name="atoms">ilst 配下に置く atom。名前は 4 文字（© は 0xA9 に変換される）。</param>
    /// <returns>M4A ファイルのバイト列。</returns>
    public static byte[] BuildM4a(IEnumerable<(string Name, int TypeFlag, byte[][] Values)> atoms)
    {
        List<byte> ilstPayload = [];

        foreach ((string name, int typeFlag, byte[][] values) in atoms)
        {
            List<byte> atomPayload = [];

            foreach (byte[] value in values)
            {
                List<byte> dataPayload = [];
                dataPayload.AddRange(ToBigEndian(typeFlag));
                dataPayload.AddRange(ToBigEndian(0));
                dataPayload.AddRange(value);
                atomPayload.AddRange(BuildBox("data", [.. dataPayload]));
            }

            ilstPayload.AddRange(BuildBox(name, [.. atomPayload]));
        }

        byte[] ilst = BuildBox("ilst", [.. ilstPayload]);

        // hdlr は FullBox。中身は読まないが、実ファイルの構造に合わせて置いておく。
        List<byte> hdlrPayload = [];
        hdlrPayload.AddRange(ToBigEndian(0));
        hdlrPayload.AddRange(ToBigEndian(0));
        hdlrPayload.AddRange(Encoding.ASCII.GetBytes("mdir"));
        hdlrPayload.AddRange(Encoding.ASCII.GetBytes("appl"));
        hdlrPayload.AddRange(new byte[9]);
        byte[] hdlr = BuildBox("hdlr", [.. hdlrPayload]);

        // meta は FullBox（version+flags の 4 バイト）。
        List<byte> metaPayload = [0, 0, 0, 0];
        metaPayload.AddRange(hdlr);
        metaPayload.AddRange(ilst);
        byte[] meta = BuildBox("meta", [.. metaPayload]);

        byte[] udta = BuildBox("udta", meta);

        // TagLib# は mvhd が無いと CorruptFileException を投げる。書き込みテストには必須。
        List<byte> moovPayload = [];
        moovPayload.AddRange(BuildMovieHeader());
        moovPayload.AddRange(BuildSoundTrack());
        moovPayload.AddRange(udta);
        byte[] moov = BuildBox("moov", [.. moovPayload]);

        List<byte> ftypPayload = [];
        ftypPayload.AddRange(Encoding.ASCII.GetBytes("M4A "));
        ftypPayload.AddRange(ToBigEndian(0));
        ftypPayload.AddRange(Encoding.ASCII.GetBytes("M4A mp42isom"));
        byte[] ftyp = BuildBox("ftyp", [.. ftypPayload]);

        List<byte> file = [];
        file.AddRange(ftyp);
        file.AddRange(moov);
        file.AddRange(BuildBox("mdat", []));

        return [.. file];
    }

    /// <summary>
    /// タグを持たない最小の FLAC（STREAMINFO のみ）を組み立てる。
    /// </summary>
    /// <returns>FLAC ファイルのバイト列。</returns>
    public static byte[] BuildFlac()
    {
        List<byte> file = [];
        file.AddRange(Encoding.ASCII.GetBytes("fLaC"));

        // メタデータブロックヘッダ: 最終ブロック(1) + 種別 STREAMINFO(0) + 長さ 34。
        file.Add(0x80);
        file.AddRange([0x00, 0x00, 0x22]);

        byte[] streamInfo = new byte[34];
        BinaryPrimitives.WriteUInt16BigEndian(streamInfo.AsSpan(0), 4096);
        BinaryPrimitives.WriteUInt16BigEndian(streamInfo.AsSpan(2), 4096);

        // sampleRate(20) + channels-1(3) + bitsPerSample-1(5) + totalSamples(36) を 64bit に詰める。
        ulong packed = ((ulong)44100 << 44) | ((ulong)1 << 41) | ((ulong)15 << 36);
        BinaryPrimitives.WriteUInt64BigEndian(streamInfo.AsSpan(10), packed);

        file.AddRange(streamInfo);

        return [.. file];
    }

    /// <summary>
    /// タグを持たない最小の AIFF を組み立てる。
    /// </summary>
    /// <returns>AIFF ファイルのバイト列。</returns>
    public static byte[] BuildAiff()
    {
        List<byte> comm = [];
        comm.AddRange(ToBigEndianUInt16(2));
        comm.AddRange(ToBigEndian(0));
        comm.AddRange(ToBigEndianUInt16(16));
        // 80bit IEEE 拡張精度で 44100Hz。指数 0x400E、仮数 0xAC44 を最上位に置く。
        comm.AddRange([0x40, 0x0E, 0xAC, 0x44, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        List<byte> ssnd = [];
        ssnd.AddRange(ToBigEndian(0));
        ssnd.AddRange(ToBigEndian(0));

        List<byte> formPayload = [];
        formPayload.AddRange(Encoding.ASCII.GetBytes("AIFF"));
        formPayload.AddRange(BuildChunk("COMM", [.. comm]));
        formPayload.AddRange(BuildChunk("SSND", [.. ssnd]));

        return BuildChunk("FORM", [.. formPayload]);
    }

    /// <summary>
    /// タグを持たない最小の MP3（MPEG-1 Layer III の無音フレームのみ）を組み立てる。
    /// </summary>
    /// <param name="frameCount">生成するフレーム数。</param>
    /// <returns>MP3 ファイルのバイト列。</returns>
    public static byte[] BuildMp3(int frameCount = 16)
    {
        // 0xFF 0xFB: 同期語 + MPEG1 + Layer III + CRC なし
        // 0x90: ビットレート 128kbps / サンプリング 44100Hz / パディングなし
        // 0x00: ステレオ
        byte[] header = [0xFF, 0xFB, 0x90, 0x00];
        const int FRAME_SIZE = 417;

        List<byte> file = [];
        for (int i = 0; i < frameCount; i++)
        {
            file.AddRange(header);
            file.AddRange(new byte[FRAME_SIZE - header.Length]);
        }

        return [.. file];
    }

    /// <summary>
    /// <c>mvhd</c>（ムービーヘッダ）を組み立てる。TagLib# が存在を必須とする。
    /// </summary>
    private static byte[] BuildMovieHeader()
    {
        List<byte> payload = [];
        payload.AddRange(ToBigEndian(0));      // version + flags
        payload.AddRange(ToBigEndian(0));      // creation_time
        payload.AddRange(ToBigEndian(0));      // modification_time
        payload.AddRange(ToBigEndian(1000));   // timescale
        payload.AddRange(ToBigEndian(1000));   // duration
        payload.AddRange(ToBigEndian(0x00010000)); // rate = 1.0
        payload.AddRange(ToBigEndianUInt16(0x0100)); // volume = 1.0
        payload.AddRange(new byte[2 + 8]);     // reserved
        payload.AddRange(BuildUnityMatrix());
        payload.AddRange(new byte[24]);        // pre_defined
        payload.AddRange(ToBigEndian(2));      // next_track_ID

        return BuildBox("mvhd", [.. payload]);
    }

    /// <summary>
    /// 音声トラック（<c>trak</c>）を組み立てる。サンプルは 0 個でよいが、
    /// TagLib# が音声プロパティを読むために階層自体は必要になる。
    /// </summary>
    private static byte[] BuildSoundTrack()
    {
        List<byte> tkhd = [];
        tkhd.AddRange([0, 0, 0, 7]);           // version 0 + flags(enabled|in movie|in preview)
        tkhd.AddRange(ToBigEndian(0));         // creation_time
        tkhd.AddRange(ToBigEndian(0));         // modification_time
        tkhd.AddRange(ToBigEndian(1));         // track_ID
        tkhd.AddRange(ToBigEndian(0));         // reserved
        tkhd.AddRange(ToBigEndian(1000));      // duration
        tkhd.AddRange(new byte[8]);            // reserved
        tkhd.AddRange(ToBigEndianUInt16(0));   // layer
        tkhd.AddRange(ToBigEndianUInt16(0));   // alternate_group
        tkhd.AddRange(ToBigEndianUInt16(0x0100)); // volume = 1.0
        tkhd.AddRange(ToBigEndianUInt16(0));   // reserved
        tkhd.AddRange(BuildUnityMatrix());
        tkhd.AddRange(ToBigEndian(0));         // width
        tkhd.AddRange(ToBigEndian(0));         // height

        List<byte> mdhd = [];
        mdhd.AddRange(ToBigEndian(0));         // version + flags
        mdhd.AddRange(ToBigEndian(0));         // creation_time
        mdhd.AddRange(ToBigEndian(0));         // modification_time
        mdhd.AddRange(ToBigEndian(44100));     // timescale
        mdhd.AddRange(ToBigEndian(44100));     // duration
        mdhd.AddRange(ToBigEndianUInt16(0x55C4)); // language = und
        mdhd.AddRange(ToBigEndianUInt16(0));   // pre_defined

        List<byte> hdlr = [];
        hdlr.AddRange(ToBigEndian(0));         // version + flags
        hdlr.AddRange(ToBigEndian(0));         // pre_defined
        hdlr.AddRange(Encoding.ASCII.GetBytes("soun"));
        hdlr.AddRange(new byte[12]);           // reserved
        hdlr.Add(0);                           // name（空文字）

        List<byte> smhd = [];
        smhd.AddRange(ToBigEndian(0));         // version + flags
        smhd.AddRange(ToBigEndianUInt16(0));   // balance
        smhd.AddRange(ToBigEndianUInt16(0));   // reserved

        List<byte> dref = [];
        dref.AddRange(ToBigEndian(0));         // version + flags
        dref.AddRange(ToBigEndian(1));         // entry_count
        dref.AddRange(ToBigEndian(12));        // url ボックスのサイズ
        dref.AddRange(Encoding.ASCII.GetBytes("url "));
        dref.AddRange([0, 0, 0, 1]);           // version + flags（自己完結）
        byte[] dinf = BuildBox("dinf", BuildBox("dref", [.. dref]));

        // mp4a サンプルエントリ。音声フォーマットの記述だけを持つ。
        List<byte> mp4a = [];
        mp4a.AddRange(new byte[6]);            // reserved
        mp4a.AddRange(ToBigEndianUInt16(1));   // data_reference_index
        mp4a.AddRange(new byte[8]);            // version / revision / vendor
        mp4a.AddRange(ToBigEndianUInt16(2));   // channel_count
        mp4a.AddRange(ToBigEndianUInt16(16));  // sample_size
        mp4a.AddRange(new byte[4]);            // compression_id / packet_size
        mp4a.AddRange(ToBigEndian(44100 << 16)); // sample_rate（16.16 固定小数）

        List<byte> stsd = [];
        stsd.AddRange(ToBigEndian(0));         // version + flags
        stsd.AddRange(ToBigEndian(1));         // entry_count
        stsd.AddRange(BuildBox("mp4a", [.. mp4a]));

        List<byte> stbl = [];
        stbl.AddRange(BuildBox("stsd", [.. stsd]));
        stbl.AddRange(BuildBox("stts", [0, 0, 0, 0, 0, 0, 0, 0]));
        stbl.AddRange(BuildBox("stsc", [0, 0, 0, 0, 0, 0, 0, 0]));
        stbl.AddRange(BuildBox("stsz", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        stbl.AddRange(BuildBox("stco", [0, 0, 0, 0, 0, 0, 0, 0]));

        List<byte> minf = [];
        minf.AddRange(BuildBox("smhd", [.. smhd]));
        minf.AddRange(dinf);
        minf.AddRange(BuildBox("stbl", [.. stbl]));

        List<byte> mdia = [];
        mdia.AddRange(BuildBox("mdhd", [.. mdhd]));
        mdia.AddRange(BuildBox("hdlr", [.. hdlr]));
        mdia.AddRange(BuildBox("minf", [.. minf]));

        List<byte> trak = [];
        trak.AddRange(BuildBox("tkhd", [.. tkhd]));
        trak.AddRange(BuildBox("mdia", [.. mdia]));

        return BuildBox("trak", [.. trak]);
    }

    /// <summary>
    /// 変換なしを表す 3x3 行列（16.16 固定小数、末尾のみ 2.30）。
    /// </summary>
    private static byte[] BuildUnityMatrix()
    {
        List<byte> matrix = [];
        int[] values = [0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000];

        foreach (int value in values)
        {
            matrix.AddRange(ToBigEndian(value));
        }

        return [.. matrix];
    }

    /// <summary>
    /// MP4 のボックスを組み立てる。名前に © を含む場合は 0xA9 に変換する。
    /// </summary>
    private static byte[] BuildBox(string name, byte[] payload)
    {
        byte[] nameBytes = EncodeAtomName(name);

        List<byte> box = [];
        box.AddRange(ToBigEndian(BOX_HEADER_SIZE + payload.Length));
        box.AddRange(nameBytes);
        box.AddRange(payload);

        return [.. box];
    }

    /// <summary>
    /// AIFF のチャンクを組み立てる。奇数長のときはパディングを 1 バイト足す。
    /// </summary>
    private static byte[] BuildChunk(string id, byte[] payload)
    {
        List<byte> chunk = [];
        chunk.AddRange(Encoding.ASCII.GetBytes(id));
        chunk.AddRange(ToBigEndian(payload.Length));
        chunk.AddRange(payload);

        if (payload.Length % 2 != 0)
        {
            chunk.Add(0);
        }

        return [.. chunk];
    }

    /// <summary>
    /// atom 名を 4 バイトに変換する。© は 0xA9 にする。
    /// </summary>
    public static byte[] EncodeAtomName(string name)
    {
        List<byte> bytes = [];

        foreach (char c in name)
        {
            bytes.Add(c == '©' ? (byte)0xA9 : (byte)c);
        }

        if (bytes.Count != 4)
        {
            throw new ArgumentException($"atom 名は 4 バイトである必要があります: {name}", nameof(name));
        }

        return [.. bytes];
    }

    /// <summary>32bit 整数をビッグエンディアンのバイト列にする。</summary>
    private static byte[] ToBigEndian(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>16bit 整数をビッグエンディアンのバイト列にする。</summary>
    private static byte[] ToBigEndianUInt16(ushort value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }
}
