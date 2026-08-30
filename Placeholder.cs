using System.Windows;

namespace ZasDictWin.Views;

/// <summary>
/// TextBox に案内文字（プレースホルダー）を添える添付プロパティ。
/// Theme.xaml の TextBox テンプレートが「入力が空のときだけ」表示する。
/// 入力値そのものには触れない（Text に書き込むと確定入力と誤認されるため）。
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Placeholder),
            new PropertyMetadata(string.Empty));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
}
