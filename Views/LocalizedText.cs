using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.Views;

/// <summary>
/// UI 文字列（Resources/Strings.*.resx）を表示欄に流し込む添付プロパティ群。文中の
/// <c>[en]…[/en]</c> を <see cref="EnTag.Split"/> で解析し、その区間だけ
/// Strings.EnFont（Strings.en.resx の Meta_UIFontFamily）を当てる。タグを含まない文字列は
/// これまでどおり単一の Run になるので、既存の見た目は変わらない。
///
/// TextBlock.Text／ContentControl.Content／HeaderedContentControl.Header はどれも素の string を
/// 受け取るだけで Inlines を組めないため、Strings の値を渡す先をこの 3 つの添付プロパティに
/// 差し替えて使う。対象は Resources/Strings.*.resx 由来の文字列だけに限ること
/// （辞書データや利用者入力の文字列にこれを使うと、たまたま含まれる "[en]" がタグとして
/// 消費されてしまう）。
///
/// TextBlock 内で他の Run（動的な値など）と混ぜる箇所は、Run ではなく Span に付けること
/// （Run は Inlines を持てないので複数区間に割れない）。あわせて、そこに当てる Style も
/// TargetType="Span" にすること（Themes/Theme.xaml の FieldNote）。WPF の Style は
/// TargetType と要素の型が一致していないと弾かれるので、Span 向けの Style を派生の Run に
/// 付けたままにすると、その画面を開いた瞬間に XamlParseException で落ちる。
///
/// 区間ごとにフォントを変えられない出力先（OS が描くタイトルバー、ファイルダイアログ、
/// 入力欄、画面の外へ書き出す文字列）にはこれを使えない。そちらは
/// <see cref="EnTag.Strip"/>／Views/Converters.cs の PlainText に通してタグだけ落とす。
/// </summary>
public static class LocalizedText
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(LocalizedText),
            new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var inlines = d switch
        {
            TextBlock tb => tb.Inlines,
            Span span => span.Inlines,
            _ => null
        };
        if (inlines is null) return;

        inlines.Clear();
        foreach (var inline in Build(e.NewValue as string)) inlines.Add(inline);
    }

    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.RegisterAttached("Content", typeof(string), typeof(LocalizedText),
            new PropertyMetadata(null, OnContentChanged));

    public static string? GetContent(DependencyObject element) => (string?)element.GetValue(ContentProperty);

    public static void SetContent(DependencyObject element, string? value) => element.SetValue(ContentProperty, value);

    // Content は既定でも string をそのまま置けば見た目は同じ（ContentPresenter が素の TextBlock を
    // 自動生成して包む）。ここでは Inlines を組んだ TextBlock を明示的に Content へ置き換えるだけなので、
    // アクセスキー（"_"）等の特別な扱いは無いという前提のまま挙動が揃う。
    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl cc) return;
        cc.Content = BuildTextBlock(e.NewValue as string);
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.RegisterAttached("Header", typeof(string), typeof(LocalizedText),
            new PropertyMetadata(null, OnHeaderChanged));

    public static string? GetHeader(DependencyObject element) => (string?)element.GetValue(HeaderProperty);

    public static void SetHeader(DependencyObject element, string? value) => element.SetValue(HeaderProperty, value);

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HeaderedContentControl hc) return;
        hc.Header = BuildTextBlock(e.NewValue as string);
    }

    private static TextBlock BuildTextBlock(string? text)
    {
        var tb = new TextBlock();
        foreach (var inline in Build(text)) tb.Inlines.Add(inline);
        return tb;
    }

    private static IEnumerable<Inline> Build(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        foreach (var segment in EnTag.Split(text))
        {
            var run = new Run(segment.Text);
            if (segment.IsEn) run.FontFamily = Strings.EnFont;
            yield return run;
        }
    }
}
