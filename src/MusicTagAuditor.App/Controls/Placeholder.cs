using System.Windows;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// 入力欄が空のときに出す説明文（プレースホルダ）を添付プロパティで持たせる。
///
/// WPF の TextBox は placeholder を持たない。ラベルを置けない場所
/// （検索欄・絞り込み欄）では、何を入れる欄なのかが空欄だと分からなくなるため、
/// テーマ側の TextBox テンプレートがこの値を薄い文字で描く。
/// </summary>
public static class Placeholder
{
    /// <summary>入力欄が空のときに表示する文字列。</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(Placeholder),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// プレースホルダの文字列を取得する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <returns>設定されている文字列。未設定なら空文字。</returns>
    public static string GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (string)element.GetValue(TextProperty);
    }

    /// <summary>
    /// プレースホルダの文字列を設定する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <param name="value">表示する文字列。</param>
    public static void SetText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(TextProperty, value);
    }
}
