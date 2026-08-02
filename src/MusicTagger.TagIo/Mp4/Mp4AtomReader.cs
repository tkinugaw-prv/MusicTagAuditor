using System.Text;

namespace MusicTagger.TagIo.Mp4;

/// <summary>
/// MP4 (M4A) のボックス構造を直接走査し、<c>moov/udta/meta/ilst</c> 配下の atom を列挙する。
///
/// TagLib# の MP4 読み取りを使わないのは、TagLib# が値を <c>"; "</c> で分割して返すため
/// 「1 値に <c>;</c> が含まれる状態」と「複数値に分割済みの状態」を区別できないから
/// （docs/adr/0001-tag-io-library.md）。この区別は検査ルール R-205 / R-206 に必要である。
/// </summary>
public static class Mp4AtomReader
{
    /// <summary>ボックスヘッダ（サイズ4 + 型4）の長さ。</summary>
    private const int BOX_HEADER_SIZE = 8;

    /// <summary>data ボックスの型フラグのうち UTF-8 テキストを表す値。</summary>
    private const int DATA_TYPE_UTF8 = 1;

    /// <summary>子を持つコンテナ atom。この配下は再帰的に走査する。</summary>
    private static readonly string[] CONTAINER_ATOMS = ["moov", "udta", "meta", "ilst"];

    /// <summary>
    /// 指定した M4A ファイルのタグ atom を列挙する。
    ///
    /// タグは <c>moov</c> の中にあり、ファイルの大半を占める <c>mdat</c>（音声本体）には無い。
    /// ファイル全体を読むと 1,000 ファイルで数十 GB の読み取りになり性能要件を満たせないため、
    /// トップレベルのボックスをシークで辿って <c>moov</c> だけをメモリに載せる。
    /// </summary>
    /// <param name="filePath">対象ファイルのパス。</param>
    /// <returns>ilst 配下の atom。タグ領域が無い場合は空。</returns>
    public static IReadOnlyList<Mp4Atom> Read(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        byte[]? moov = ReadTopLevelBox(stream, "moov");

        return moov is null ? [] : Parse(moov);
    }

    /// <summary>
    /// トップレベルのボックスを順に辿り、指定した型のボックスだけを読み出す。
    /// 目的のボックス以外はシークで読み飛ばす。
    /// </summary>
    /// <param name="stream">対象ファイルのストリーム。</param>
    /// <param name="boxType">読み出すボックスの型名。</param>
    /// <returns>ボックス全体（ヘッダを含む）のバイト列。見つからなければ null。</returns>
    private static byte[]? ReadTopLevelBox(FileStream stream, string boxType)
    {
        byte[] header = new byte[BOX_HEADER_SIZE];
        long position = 0;
        long length = stream.Length;

        while (position + BOX_HEADER_SIZE <= length)
        {
            stream.Position = position;

            if (stream.ReadAtLeast(header, BOX_HEADER_SIZE, throwOnEndOfStream: false) < BOX_HEADER_SIZE)
            {
                return null;
            }

            long size = ReadUInt32BigEndian(header, 0);
            int headerSize = BOX_HEADER_SIZE;

            if (size == 1)
            {
                byte[] extended = new byte[8];
                if (stream.ReadAtLeast(extended, 8, throwOnEndOfStream: false) < 8)
                {
                    return null;
                }

                size = ReadUInt64BigEndian(extended, 0);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = length - position;
            }

            if (size < headerSize || position + size > length)
            {
                return null;
            }

            if (Encoding.ASCII.GetString(header, 4, 4) == boxType)
            {
                byte[] box = new byte[size];
                stream.Position = position;
                stream.ReadExactly(box, 0, (int)size);
                return box;
            }

            position += size;
        }

        return null;
    }

    /// <summary>
    /// メモリ上の MP4 バイト列からタグ atom を列挙する。テストから直接呼べるようにしてある。
    /// </summary>
    /// <param name="bytes">MP4 ファイル全体のバイト列。</param>
    /// <returns>ilst 配下の atom。</returns>
    public static IReadOnlyList<Mp4Atom> Parse(byte[] bytes)
    {
        List<Mp4Atom> results = [];
        WalkBoxes(bytes, 0, bytes.Length, string.Empty, results);
        return results;
    }

    /// <summary>
    /// ボックス列を順に読み、コンテナなら再帰し、ilst の子なら結果に積む。
    /// </summary>
    private static void WalkBoxes(byte[] bytes, int start, int end, string parentPath, List<Mp4Atom> results)
    {
        int offset = start;
        while (offset + BOX_HEADER_SIZE <= end)
        {
            long size = ReadUInt32BigEndian(bytes, offset);
            int headerSize = BOX_HEADER_SIZE;

            if (size == 1)
            {
                // 64bit 拡張サイズ。ヘッダ直後の 8 バイトが実サイズ。
                if (offset + 16 > end)
                {
                    return;
                }

                size = ReadUInt64BigEndian(bytes, offset + BOX_HEADER_SIZE);
                headerSize = 16;
            }
            else if (size == 0)
            {
                // ファイル末尾まで。
                size = end - offset;
            }

            if (size < headerSize || offset + size > end)
            {
                return;
            }

            byte[] nameBytes = bytes[(offset + 4)..(offset + 8)];
            string name = FormatAtomName(nameBytes);
            string path = parentPath.Length == 0 ? name : $"{parentPath}/{name}";

            int childStart = offset + headerSize;
            int childEnd = offset + (int)size;

            if (CONTAINER_ATOMS.Contains(name))
            {
                // meta は FullBox（version+flags 4 バイト）の場合がある。0 埋め 4 バイトなら読み飛ばす。
                if (name == "meta" && childStart + 4 <= childEnd && IsAllZero(bytes, childStart, 4))
                {
                    childStart += 4;
                }

                WalkBoxes(bytes, childStart, childEnd, path, results);
            }
            else if (parentPath.EndsWith("ilst", StringComparison.Ordinal))
            {
                results.Add(BuildAtom(bytes, name, nameBytes, childStart, childEnd));
            }

            offset += (int)size;
        }
    }

    /// <summary>
    /// ilst 直下の atom について、内包する data / mean / name ボックスから値を取り出す。
    /// </summary>
    private static Mp4Atom BuildAtom(byte[] bytes, string name, byte[] nameBytes, int childStart, int childEnd)
    {
        string? meanValue = null;
        string? nameValue = null;
        List<string> values = [];

        int offset = childStart;
        while (offset + BOX_HEADER_SIZE <= childEnd)
        {
            long childSize = ReadUInt32BigEndian(bytes, offset);
            if (childSize < BOX_HEADER_SIZE || offset + childSize > childEnd)
            {
                break;
            }

            string childName = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            int payloadStart = offset + BOX_HEADER_SIZE;
            int payloadEnd = offset + (int)childSize;

            switch (childName)
            {
                case "data":
                    // version(1) + flags(3) + locale(4) の後が値本体。
                    // 1 つの atom が data ボックスを複数持つことがあり、それが MP4 での複数値の表現になる。
                    if (payloadStart + 8 <= payloadEnd)
                    {
                        int typeFlag = (int)(ReadUInt32BigEndian(bytes, payloadStart) & 0x00FFFFFF);
                        int valueStart = payloadStart + 8;
                        values.Add(typeFlag == DATA_TYPE_UTF8
                            ? Encoding.UTF8.GetString(bytes, valueStart, payloadEnd - valueStart)
                            : TagIoConst.BINARY_VALUE_PREFIX + Convert.ToHexString(bytes, valueStart, payloadEnd - valueStart));
                    }

                    break;

                case "mean":
                    meanValue = ReadFullBoxString(bytes, payloadStart, payloadEnd);
                    break;

                case "name":
                    nameValue = ReadFullBoxString(bytes, payloadStart, payloadEnd);
                    break;
            }

            offset += (int)childSize;
        }

        // フリーフォーム atom は ----:mean:name の形で識別する。
        string displayName = meanValue is null && nameValue is null
            ? name
            : $"{name}:{meanValue}:{nameValue}";

        return new Mp4Atom(displayName, Convert.ToHexString(nameBytes), values);
    }

    /// <summary>
    /// FullBox（version+flags 4 バイト）に続く UTF-8 文字列を読む。
    /// </summary>
    private static string ReadFullBoxString(byte[] bytes, int payloadStart, int payloadEnd)
    {
        int valueStart = payloadStart + 4;

        if (valueStart >= payloadEnd)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(bytes, valueStart, payloadEnd - valueStart);
    }

    /// <summary>
    /// atom 名 4 バイトを表示用文字列にする。0xA9 は © として表示する。
    /// </summary>
    private static string FormatAtomName(byte[] nameBytes)
    {
        StringBuilder builder = new(4);

        foreach (byte b in nameBytes)
        {
            if (b == 0xA9)
            {
                builder.Append('©');
            }
            else if (b >= 0x20 && b < 0x7F)
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('.');
            }
        }

        return builder.ToString();
    }

    /// <summary>指定範囲がすべて 0 かどうかを判定する。</summary>
    private static bool IsAllZero(byte[] bytes, int start, int length)
    {
        for (int i = start; i < start + length; i++)
        {
            if (bytes[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ビッグエンディアンの 32bit 符号なし整数を読む。</summary>
    private static long ReadUInt32BigEndian(byte[] bytes, int offset)
    {
        return ((long)bytes[offset] << 24)
             | ((long)bytes[offset + 1] << 16)
             | ((long)bytes[offset + 2] << 8)
             | bytes[offset + 3];
    }

    /// <summary>ビッグエンディアンの 64bit 符号なし整数を読む。</summary>
    private static long ReadUInt64BigEndian(byte[] bytes, int offset)
    {
        long value = 0;

        for (int i = 0; i < 8; i++)
        {
            value = (value << 8) | bytes[offset + i];
        }

        return value;
    }
}
