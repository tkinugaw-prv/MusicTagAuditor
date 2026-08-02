using System.Text;

namespace TagIoProbe;

/// <summary>
/// MP4 ボックスツリーの中の 1 つの atom を表す。
/// </summary>
/// <param name="Path">ルートからのパス（例: <c>moov/udta/meta/ilst/©con</c>）。</param>
/// <param name="NameHex">atom 名 4 バイトの16進表記。© のような非 ASCII を含むため必須。</param>
/// <param name="Size">atom 全体のバイト数。</param>
/// <param name="TextPreview">data ボックスが UTF-8 テキストだった場合の値。それ以外は null。</param>
/// <param name="DataTypeFlag">data ボックスの型フラグ（1=UTF-8, 0=binary, 13=JPEG, 14=PNG, 21=int）。</param>
/// <param name="Values">この atom が持つ data ボックスの値をすべて並べたもの。複数値の検出に使う。</param>
internal sealed record AtomInfo(
    string Path,
    string NameHex,
    long Size,
    string? TextPreview,
    int? DataTypeFlag,
    IReadOnlyList<string> Values);

/// <summary>
/// MP4 (M4A) のボックス構造を直接走査し、<c>moov/udta/meta/ilst</c> 配下の atom を列挙する。
/// タグライブラリが実際にどの atom へ書いたかを、そのライブラリ自身の読み戻しに頼らず
/// バイナリレベルで確認するために使う（docs/SPEC.md 4.2 の検証方法 (c)）。
/// </summary>
internal static class Mp4AtomDumper
{
    /// <summary>ボックスヘッダ（サイズ4 + 型4）の長さ。</summary>
    private const int BOX_HEADER_SIZE = 8;

    /// <summary>子を持つコンテナ atom。この配下は再帰的に走査する。</summary>
    private static readonly string[] CONTAINER_ATOMS = ["moov", "udta", "meta", "ilst"];

    /// <summary>
    /// 指定した M4A ファイルのタグ領域 atom を列挙する。
    /// </summary>
    /// <param name="filePath">対象ファイルのパス。</param>
    /// <returns>ilst 配下の atom 情報。タグ領域が存在しない場合は空。</returns>
    public static IReadOnlyList<AtomInfo> Dump(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        List<AtomInfo> results = [];
        WalkBoxes(bytes, 0, bytes.Length, string.Empty, results);
        return results;
    }

    /// <summary>
    /// ボックス列を順に読み、コンテナなら再帰し、ilst の子なら結果に積む。
    /// </summary>
    private static void WalkBoxes(byte[] bytes, int start, int end, string parentPath, List<AtomInfo> results)
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
                results.Add(BuildAtomInfo(bytes, path, nameBytes, size, childStart, childEnd));
            }

            offset += (int)size;
        }
    }

    /// <summary>
    /// ilst 直下の atom について、内包する data / mean / name ボックスから値を取り出す。
    /// </summary>
    private static AtomInfo BuildAtomInfo(
        byte[] bytes,
        string path,
        byte[] nameBytes,
        long size,
        int childStart,
        int childEnd)
    {
        string? meanValue = null;
        string? nameValue = null;
        int? dataTypeFlag = null;
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
                    // 1 つの atom が data ボックスを複数持つ場合があり、それが MP4 における複数値の表現。
                    if (payloadStart + 8 <= payloadEnd)
                    {
                        dataTypeFlag ??= (int)(ReadUInt32BigEndian(bytes, payloadStart) & 0x00FFFFFF);
                        int thisTypeFlag = (int)(ReadUInt32BigEndian(bytes, payloadStart) & 0x00FFFFFF);
                        int valueStart = payloadStart + 8;
                        values.Add(thisTypeFlag == 1
                            ? Encoding.UTF8.GetString(bytes, valueStart, payloadEnd - valueStart)
                            : $"<binary {payloadEnd - valueStart} bytes>");
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

        // フリーフォーム atom は ----:mean:name の形で識別できるようにする。
        string displayPath = path;
        if (meanValue is not null || nameValue is not null)
        {
            displayPath = $"{path}:{meanValue}:{nameValue}";
        }

        string? textPreview = values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => string.Join(" ⟂ ", values),
        };

        return new AtomInfo(displayPath, Convert.ToHexString(nameBytes), size, textPreview, dataTypeFlag, values);
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
