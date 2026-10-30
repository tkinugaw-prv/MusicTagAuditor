using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MusicTagAuditor.App.Interop;

/// <summary>
/// モーダルダイアログに無効化されたウィンドウを、入力を受け付ける状態に戻す。
///
/// WPF の ShowDialog は、開いた時点で存在する同じスレッドのウィンドウをすべて無効にする。
/// 原則ウィンドウを先に開いておき、ダイアログの中の参照から呼び出すと、原則ウィンドウは
/// 該当箇所まで移動するのにスクロールも検索もできない。ダイアログを閉じると WPF が改めて
/// 有効にするので、先に有効へ戻しても二重に戻るだけで害は無い。
/// </summary>
internal static class WindowEnabling
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    /// <summary>
    /// 無効になっていれば有効に戻す。
    /// </summary>
    /// <param name="window">対象のウィンドウ。</param>
    public static void EnsureEnabled(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;

        if (hwnd != IntPtr.Zero && !IsWindowEnabled(hwnd))
        {
            _ = EnableWindow(hwnd, true);
        }
    }
}
