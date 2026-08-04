using System.Collections.ObjectModel;
using System.IO;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// フォルダツリーの 1 ノード。
/// </summary>
/// <param name="name">表示名（フォルダ名）。</param>
/// <param name="relativePath">ライブラリルートからの相対パス。ルート自身は空文字。</param>
public sealed class FolderNodeViewModel(string name, string relativePath)
{
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

    /// <summary>ルート直下のノードは既定で開いた状態にする。</summary>
    public bool IsExpanded { get; set; }

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
