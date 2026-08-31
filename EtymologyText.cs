using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using ZasDictWin.Models;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 内容欄（<see cref="ContentItem"/>）を TextBlock に流し込む添付プロパティ。
/// title が「語源」のときだけ <see cref="Etymology.Split"/> で区間に割り、イジェール語の語幹に
/// Heksa フォントを当てる。それ以外の内容欄は素のテキストとして 1 本の Run で置く。
///
/// TextBlock.Inlines はバインドできないため、Text="{Binding Text}" の代わりに
/// v:EtymologyText.Content="{Binding}" を書いて、ここで Inlines を組み立てる。
/// </summary>
public static class EtymologyText
{
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.RegisterAttached("Content", typeof(ContentItem), typeof(EtymologyText),
            new PropertyMetadata(null, OnContentChanged));

    public static ContentItem? GetContent(DependencyObject element) => (ContentItem?)element.GetValue(ContentProperty);

    public static void SetContent(DependencyObject element, ContentItem? value) => element.SetValue(ContentProperty, value);

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        tb.Inlines.Clear();
        if (e.NewValue is not ContentItem item || string.IsNullOrEmpty(item.Text)) return;

        if (item.Title != Const.EtymologyContentTitle)
        {
            tb.Inlines.Add(new Run(item.Text));
            return;
        }

        foreach (var segment in Etymology.Split(item.Text))
        {
            var run = new Run(segment.Text);
            // 設定でフォントを差し替えたときに追随させたいので、値を焼き込まずバインドで持たせる。
            // Heksa 未設定なら Family は Yu Gothic UI なので、見た目はラテン文字のままになる。
            if (segment.IsIdyerin)
            {
                BindingOperations.SetBinding(run, TextElement.FontFamilyProperty,
                    new Binding(nameof(HeadwordFontState.Family)) { Source = HeadwordFontState.Instance });
            }
            tb.Inlines.Add(run);
        }
    }
}
