using System.Windows;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// TextBox の先頭に添えるアイコン（検索欄の虫眼鏡など）。値は Themes/Icons.xaml の Icon_名前。
/// Themes/Theme.xaml の TextBox テンプレートが入力の手前の列に描くので、入力と案内文字はアイコンの後ろから始まる。
/// アイコンは文字に添えるので文字の倍率で伸び、文字の開始位置も一緒に下がる（重ならない）。
/// </summary>
public static class FieldIcon
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.RegisterAttached("Data", typeof(Geometry), typeof(FieldIcon), new PropertyMetadata(null));

    public static Geometry? GetData(DependencyObject element) => (Geometry?)element.GetValue(DataProperty);

    public static void SetData(DependencyObject element, Geometry? value) => element.SetValue(DataProperty, value);
}
