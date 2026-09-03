using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using ZasDictWin.Models;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 内容欄（<see cref="ContentItem"/>）を本文の器に流し込む添付プロパティ。
/// title が「語源」のときだけ <see cref="Etymology.Split"/> で区間に割り、イジェール語の語幹に
/// Heksa フォントを当てる。それ以外の内容欄は素のテキストとして 1 本の Run で置く。
///
/// Inlines はバインドできないため、Text="{Binding Text}" の代わりに
/// v:EtymologyText.Content="{Binding}" を書いて、ここで組み立てる。
/// 器は TextBlock（選べない表示）と <see cref="SelectableRichText"/>（選んでコピーできる本文）の
/// どちらでもよい。
/// </summary>
public static class EtymologyText
{
    /// <summary>本文の行送り。scale=1.0 のときの px 値で、文字サイズと同じ倍率で伸び縮みさせる。</summary>
    private const string LineHeightAtScaleOne = "22";

    private static readonly ScaleFontSizeConverter ScaleLineHeight = new();

    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.RegisterAttached("Content", typeof(ContentItem), typeof(EtymologyText),
            new PropertyMetadata(null, OnContentChanged));

    public static ContentItem? GetContent(DependencyObject element) => (ContentItem?)element.GetValue(ContentProperty);

    public static void SetContent(DependencyObject element, ContentItem? value) => element.SetValue(ContentProperty, value);

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = e.NewValue as ContentItem;

        switch (d)
        {
            case TextBlock tb:
                tb.Inlines.Clear();
                foreach (var inline in Build(item)) tb.Inlines.Add(inline);
                break;

            case RichTextBox rtb:
                // 段落の余白と紙面の余白は、素の TextBlock と同じ位置から書き始めるために潰す
                // （RichTextBox の既定は印刷物向けに四方へ余白を取る）。
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                foreach (var inline in Build(item)) paragraph.Inlines.Add(inline);

                // FlowDocument の既定は両端揃え。素の TextBlock と行末を揃えるため左寄せに戻す。
                var document = new FlowDocument(paragraph)
                {
                    PagePadding = new Thickness(0),
                    TextAlignment = TextAlignment.Left
                };
                // FlowDocument は書式の既定値を自前で持っていて、載せた RichTextBox からは継承しない。
                // 文字サイズは Ctrl＋ホイールでも動くので、値を焼き込まずバインドで追わせる。
                foreach (var property in new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.ForegroundProperty })
                    BindingOperations.SetBinding(document, property, new Binding(property.Name) { Source = rtb });

                BindingOperations.SetBinding(document, FlowDocument.LineHeightProperty,
                    new Binding(nameof(FontScaleState.Scale))
                    {
                        Source = FontScaleState.Instance,
                        Converter = ScaleLineHeight,
                        ConverterParameter = LineHeightAtScaleOne
                    });
                rtb.Document = document;
                break;
        }
    }

    private static IEnumerable<Inline> Build(ContentItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Text)) yield break;

        if (item.Title != Const.EtymologyContentTitle)
        {
            yield return new Run(item.Text);
            yield break;
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
            yield return run;
        }
    }
}
