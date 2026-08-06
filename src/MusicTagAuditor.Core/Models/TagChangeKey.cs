namespace MusicTagAuditor.Core.Models;

/// <summary>
/// 1 ファイル 1 フィールドを指す識別子。<see cref="TagChange"/> と適用結果を突き合わせるのに使う。
///
/// パスの大文字小文字は Windows に合わせて無視する（<c>ManualEditSet.Key</c> と同じ作法）。
/// 正規化した文字列をそのままフィールドに持たせ、既定の値比較に乗せる。
/// </summary>
public readonly record struct TagChangeKey
{
    private TagChangeKey(string relativePath, TagField field)
    {
        RelativePath = relativePath;
        Field = field;
    }

    /// <summary>正規化済みの相対パス。</summary>
    public string RelativePath { get; }

    /// <summary>対象フィールド。</summary>
    public TagField Field { get; }

    /// <summary>
    /// 相対パスを正規化してキーを作る。**直接コンストラクタを呼ばないこと。**
    /// 正規化を経ずに作ると、大文字小文字違いのパスが別キー扱いになる。
    /// </summary>
    public static TagChangeKey Of(string relativePath, TagField field)
    {
        return new TagChangeKey(relativePath.ToUpperInvariant(), field);
    }

    /// <summary>
    /// 修正案からキーを作る。
    /// </summary>
    public static TagChangeKey From(TagChange change)
    {
        return Of(change.RelativePath, change.Field);
    }
}
