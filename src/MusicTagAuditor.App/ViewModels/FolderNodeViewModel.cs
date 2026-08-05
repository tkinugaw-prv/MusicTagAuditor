using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// フォルダツリーの 1 ノード。
///
/// 展開状態と選択状態は双方向にバインドする。**片方向だと画面からしか動かせない。**
/// 検査結果からファイル一覧の行へ飛ぶとき、ツリーの表示を対象フォルダへ追随させる必要がある
/// （追随させないと、一覧の中身とツリーのハイライトが食い違う）。
/// </summary>
/// <param name="name">表示名（フォルダ名）。</param>
/// <param name="relativePath">ライブラリルートからの相対パス。ルート自身は空文字。</param>
public sealed partial class FolderNodeViewModel(string name, string relativePath) : ObservableObject
{
    /// <summary>ツリーで展開されているか。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>ツリーで選択されているか。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>表示名。</summary>
    public string Name { get; } = name;

    /// <summary>ライブラリルートからの相対パス。</summary>
    public string RelativePath { get; } = relativePath;

    /// <summary>子フォルダ。</summary>
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    /// <summary>このフォルダ配下（自身を含む）のファイル数。</summary>
    public int FileCount { get; set; }

    /// <summary>ツリーに出す表示文字列。</summary>
    public string DisplayText => $"{Name} ({FileCount})";

    /// <summary>
    /// 走査結果の相対パスからフォルダツリーを組み立てる。
    /// </summary>
    /// <param name="rootName">ルートノードの表示名。</param>
    /// <param name="relativePaths">ファイルの相対パス。</param>
    /// <returns>ルートノード。</returns>
    public static FolderNodeViewModel BuildTree(string rootName, IEnumerable<string> relativePaths)
    {
        FolderNodeViewModel root = new(rootName, string.Empty) { IsExpanded = true };

        foreach (string relativePath in relativePaths)
        {
            string? directory = Path.GetDirectoryName(relativePath);
            root.FileCount++;

            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            FolderNodeViewModel current = root;
            string accumulated = string.Empty;

            foreach (string segment in directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                accumulated = accumulated.Length == 0 ? segment : Path.Combine(accumulated, segment);

                FolderNodeViewModel? child = current.Children
                    .FirstOrDefault(node => node.Name.Equals(segment, StringComparison.Ordinal));

                if (child is null)
                {
                    child = new FolderNodeViewModel(segment, accumulated);
                    current.Children.Add(child);
                }

                child.FileCount++;
                current = child;
            }
        }

        SortRecursively(root);

        return root;
    }

    /// <summary>
    /// 相対パスに対応するノードを、このノードを起点に探す。
    ///
    /// **辿った経路のノードは展開する。** 畳まれたままだと TreeViewItem が生成されず、
    /// 選択状態のバインドが画面に届かない。
    /// </summary>
    /// <param name="folderRelativePath">探すフォルダの相対パス。空文字ならこのノード自身。</param>
    /// <returns>見つかったノード。無ければ null。</returns>
    public FolderNodeViewModel? Locate(string folderRelativePath)
    {
        if (string.IsNullOrEmpty(folderRelativePath))
        {
            return this;
        }

        FolderNodeViewModel current = this;

        foreach (string segment in folderRelativePath.Split(
                     Path.DirectorySeparatorChar,
                     Path.AltDirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            FolderNodeViewModel? child = current.Children
                .FirstOrDefault(node => node.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (child is null)
            {
                return null;
            }

            current.IsExpanded = true;
            current = child;
        }

        return current;
    }

    /// <summary>
    /// 子ノードを名前順に並べ替える。走査順に依存しない表示にするため。
    /// </summary>
    private static void SortRecursively(FolderNodeViewModel node)
    {
        List<FolderNodeViewModel> sorted =
            [.. node.Children.OrderBy(child => child.Name, StringComparer.CurrentCulture)];

        node.Children.Clear();

        foreach (FolderNodeViewModel child in sorted)
        {
            node.Children.Add(child);
            SortRecursively(child);
        }
    }
}
