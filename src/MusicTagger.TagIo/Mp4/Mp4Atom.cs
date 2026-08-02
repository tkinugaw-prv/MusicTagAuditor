namespace MusicTagger.TagIo.Mp4;

/// <summary>
/// <c>moov/udta/meta/ilst</c> 配下の atom 1 件。
/// </summary>
/// <param name="Name">
/// atom 名。0xA9 で始まるものは © に置き換えた表示形（例: <c>©nam</c>）。
/// フリーフォーム atom は <c>----:mean:name</c> の形になる。
/// </param>
/// <param name="NameHex">atom 名 4 バイトの16進表記。© を含むため同一性判定にはこちらを使う。</param>
/// <param name="Values">
/// この atom が持つ data ボックスの値。テキスト以外は
/// <see cref="TagIoConst.BINARY_VALUE_PREFIX"/> に続く16進表記になる。
/// 要素が 2 つ以上あるのは、AIMP が <c>;</c> で分割した状態を意味する。
/// </param>
public sealed record Mp4Atom(string Name, string NameHex, IReadOnlyList<string> Values);
