namespace MusicTagger.Core.Dictionary;

/// <summary>
/// 現在の辞書と索引を 1 箇所に集約して持つ。
///
/// 辞書は段階 5 で編集できるようになったため、**索引を握り込んだままにできない**。
/// 利用者が値を足したら索引を作り直す必要があり、その差し替え点をここに閉じ込める。
/// 索引を直接注入すると、更新後も古い索引を参照し続ける箇所が生まれる。
/// </summary>
public sealed class DictionaryStore
{
    /// <summary>辞書を置くフォルダ。</summary>
    private readonly string _directory;

    /// <summary>現在の辞書。</summary>
    private TagDictionary _dictionary;

    /// <summary>現在の索引。</summary>
    private DictionaryIndex _index;

    /// <summary>
    /// 辞書を読み込んでストアを作る。ファイルが無ければ同梱の既定辞書がコピーされる。
    /// </summary>
    /// <param name="directory">辞書を置くフォルダ（<c>%APPDATA%\musicTagger</c>）。</param>
    public DictionaryStore(string directory)
    {
        _directory = directory;
        _dictionary = DictionaryLoader.LoadOrCreate(directory);
        _index = new DictionaryIndex(_dictionary);
    }

    /// <summary>現在の辞書。</summary>
    public TagDictionary Dictionary => _dictionary;

    /// <summary>現在の索引。**保持せず、必要になるたびに取り直すこと。**</summary>
    public DictionaryIndex Index => _index;

    /// <summary>辞書ファイルのパス。</summary>
    public string FilePath => DictionaryLoader.GetUserDictionaryPath(_directory);

    /// <summary>
    /// 辞書を保存して索引を作り直す。
    ///
    /// **検証は呼び出し側で済ませておくこと。** ここで弾くと、UI が問題を一覧表示する前に
    /// 例外で止まってしまう。保存できない理由は画面に出したい。
    /// </summary>
    /// <param name="dictionary">保存する辞書。</param>
    public void Save(TagDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        DictionaryWriter.WriteFile(FilePath, dictionary);

        _dictionary = dictionary;
        _index = new DictionaryIndex(dictionary);
    }

    /// <summary>
    /// 保存せずに、この場だけ辞書を差し替える。取り消せる編集の試行に使う。
    /// </summary>
    /// <param name="dictionary">差し替える辞書。</param>
    public void ReplaceWithoutSaving(TagDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        _dictionary = dictionary;
        _index = new DictionaryIndex(dictionary);
    }

    /// <summary>
    /// ファイルから読み直す。編集を破棄して保存済みの状態に戻すときに使う。
    /// </summary>
    public void Reload()
    {
        _dictionary = DictionaryLoader.LoadOrCreate(_directory);
        _index = new DictionaryIndex(_dictionary);
    }

    /// <summary>
    /// 現在の辞書を任意のパスに書き出す。同梱の既定辞書へ書き戻すために使う。
    /// </summary>
    /// <param name="path">書き出し先。</param>
    public void Export(string path)
    {
        DictionaryWriter.WriteFile(path, _dictionary);
    }
}
