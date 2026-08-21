using System.Diagnostics;
using System.IO;

namespace MusicTagAuditor.App;

/// <summary>
/// エクスプローラーでファイルを見せる。
///
/// **アプリからファイルを操作するわけではない。** 画面で見つけたものを実物のフォルダで
/// 確認したいときに、パスを目で読んで開き直す手間を省くためだけの導線。
/// ファイル一覧タブの行と辞書タブの辞書ファイルで共用する。
/// </summary>
public static class ExplorerLauncher
{
    /// <summary>
    /// ファイルを選択した状態でエクスプローラーを開く。
    /// ファイルが無ければ親フォルダだけを開く。
    /// </summary>
    /// <param name="fullPath">見せたいファイルの絶対パス。</param>
    /// <returns>ファイルを選択して開けたなら true。フォルダだけを開いた、または何も開けなかったなら false。</returns>
    public static bool RevealFile(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            Start(BuildSelectArguments(fullPath));
            return true;
        }

        string? folder = Path.GetDirectoryName(fullPath);

        if (folder is not null && Directory.Exists(folder))
        {
            Start($"\"{folder}\"");
        }

        return false;
    }

    /// <summary>
    /// ファイルを選択して開くための引数を組み立てる。
    ///
    /// **引数は文字列で渡すこと。** <c>ArgumentList</c> を使うと <c>/select,&lt;パス&gt;</c> 全体が
    /// 1 個の引数として引用され、エクスプローラー側が解釈できずマイドキュメントが開く。
    /// Windows のパスに <c>"</c> は入らないため、この引用で閉じられる。
    /// </summary>
    /// <param name="fullPath">見せたいファイルの絶対パス。</param>
    /// <returns>エクスプローラーに渡す引数。</returns>
    public static string BuildSelectArguments(string fullPath)
    {
        return $"/select,\"{fullPath}\"";
    }

    /// <summary>
    /// エクスプローラーを起動する。
    /// 終了コードは見ない。エクスプローラーは正常時も 1 を返す。
    /// </summary>
    /// <param name="arguments">エクスプローラーに渡す引数。</param>
    private static void Start(string arguments)
    {
        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = false,
        });
    }
}
