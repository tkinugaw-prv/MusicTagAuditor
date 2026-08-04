using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MusicTagger.App.Interop;

/// <summary>
/// DWM API でウィンドウの OS タイトルバーをアプリのダークテーマに合わせて着色するヘルパー。
/// 属性が未サポートの環境（Windows 10 など）では黙って標準の外観のままにする。
/// </summary>
internal static class DwmDarkTitleBar
{
    /// <summary>ダークモードのタイトルバーを有効化する属性。</summary>
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>タイトルバーの背景色を指定する属性（Windows 11 以降）。</summary>
    private const int DWMWA_CAPTION_COLOR = 35;

    /// <summary>タイトルバーの文字色を指定する属性（Windows 11 以降）。</summary>
    private const int DWMWA_TEXT_COLOR = 36;

    /// <summary>タイトルバー背景色のリソースキー。</summary>
    private const string CAPTION_COLOR_RESOURCE_KEY = "PanelBarColor";

    /// <summary>タイトルバー文字色のリソースキー。</summary>
    private const string CAPTION_TEXT_COLOR_RESOURCE_KEY = "TextPrimaryColor";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// 指定ウィンドウのタイトルバーにダークテーマの配色を適用する。
    /// HWND 確定後（OnSourceInitialized 以降）に呼び出すこと。
    /// </summary>
    /// <param name="window">適用対象のウィンドウ。</param>
    public static void Apply(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // 各属性は独立に設定し、一部が未サポートでも他は適用する。
        SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        SetAttribute(hwnd, DWMWA_CAPTION_COLOR, ToColorRef(FindColor(CAPTION_COLOR_RESOURCE_KEY, Color.FromRgb(0x23, 0x26, 0x2B))));
        SetAttribute(hwnd, DWMWA_TEXT_COLOR, ToColorRef(FindColor(CAPTION_TEXT_COLOR_RESOURCE_KEY, Color.FromRgb(0xE4, 0xE5, 0xE8))));
    }

    /// <summary>
    /// 属性を設定する。失敗（未サポート環境での E_INVALIDARG など）は無視する。
    /// </summary>
    /// <param name="hwnd">対象ウィンドウのハンドル。</param>
    /// <param name="attribute">DWM の属性 ID。</param>
    /// <param name="value">設定する値。</param>
    private static void SetAttribute(IntPtr hwnd, int attribute, int value)
    {
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }

    /// <summary>
    /// テーマリソースから色を取得する。見つからない場合は既定値を返す。
    /// </summary>
    /// <param name="resourceKey">リソースキー。</param>
    /// <param name="fallback">見つからないときの色。</param>
    /// <returns>解決した色。</returns>
    private static Color FindColor(string resourceKey, Color fallback)
    {
        return Application.Current?.TryFindResource(resourceKey) is Color color ? color : fallback;
    }

    /// <summary>
    /// WPF の色を Win32 の COLORREF（0x00BBGGRR）へ変換する。
    /// </summary>
    /// <param name="color">変換する色。</param>
    /// <returns>COLORREF 値。</returns>
    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }
}
