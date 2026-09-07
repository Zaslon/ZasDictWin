using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// TextBlock の表示幅に収まるよう、文字列の途中を「…」で省略して表示する添付プロパティ。
/// 末尾だけで切る省略（TextTrimming）だと URL のクエリなど後方が消えて判別できなくなるため、
/// 先頭と末尾を残す。既定の TextBlock スタイルは Wrap なので、使い所で NoWrap にする。
/// Text はこのクラスが書き込むので、元文字列は Source 側にバインドすること
/// （Text をバインドすると上書きして競合する）。
/// </summary>
public static class EllipsisMiddle
{
    private const string Mark = "…";

    /// <summary>省略前の全文。ここへバインドする。</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached("Source", typeof(string), typeof(EllipsisMiddle),
            new PropertyMetadata(string.Empty, OnSourceChanged));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(EllipsisMiddle),
            new PropertyMetadata(false));

    public static string GetSource(DependencyObject element) => (string)element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, string value) => element.SetValue(SourceProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        Hook(tb);
        Update(tb);
    }

    /// <summary>幅・文字サイズの変更で再計算するフックを付ける（一度だけ）。</summary>
    private static void Hook(TextBlock tb)
    {
        if ((bool)tb.GetValue(IsHookedProperty)) return;
        tb.SetValue(IsHookedProperty, true);

        tb.SizeChanged += (_, _) => Update(tb);
        tb.Loaded += (_, _) => Update(tb);
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.FontSizeProperty, typeof(TextBlock));
        descriptor?.AddValueChanged(tb, (_, _) => Update(tb));
    }

    /// <summary>収まる先頭＋末尾の文字数を探索して Text に書き込む。</summary>
    private static void Update(TextBlock tb)
    {
        var source = GetSource(tb);
        if (string.IsNullOrEmpty(source))
        {
            SetText(tb, string.Empty);
            return;
        }

        var width = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        if (width <= 0 || double.IsInfinity(width))
        {
            SetText(tb, source);   // まだレイアウト前。SizeChanged で必ず再計算される
            return;
        }

        if (Measure(tb, source) <= width)
        {
            SetText(tb, source);
            return;
        }

        var lo = 1;
        var hi = source.Length - 1;
        var best = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Measure(tb, Compose(source, mid)) <= width)
            {
                best = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }

        SetText(tb, best > 0 ? Compose(source, best) : Mark);
    }

    /// <summary>先頭 6:末尾 4 の割合で切り出し、間を「…」で結ぶ。</summary>
    private static string Compose(string source, int keep)
    {
        if (keep >= source.Length) return source;
        var tail = Math.Max(1, (int)(keep * 0.4));
        var head = Math.Max(1, keep - tail);
        if (head + tail >= source.Length) return source;   // 省略が要らない長さまで削れた

        // サロゲート対の途中で切ると文字化けするので 1 文字ずらす（常に狭める方向）。
        if (head > 0 && char.IsLowSurrogate(source[head])) head--;
        var tailStart = source.Length - tail;
        if (char.IsHighSurrogate(source[tailStart - 1])) tailStart++;

        return string.Concat(source.AsSpan(0, head), Mark, source.AsSpan(tailStart));
    }

    private static double Measure(TextBlock tb, string text)
    {
        double pixelsPerDip;
        try
        {
            pixelsPerDip = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
        }
        catch (InvalidOperationException)
        {
            pixelsPerDip = 1.0;   // まだビジュアルツリーに繋がれていない
        }

        var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        return new FormattedText(text, CultureInfo.CurrentUICulture, tb.FlowDirection,
            typeface, tb.FontSize, Brushes.Black, pixelsPerDip).WidthIncludingTrailingWhitespace;
    }

    private static void SetText(TextBlock tb, string text)
    {
        if (tb.Text != text) tb.Text = text;   // 同じ値の再代入＝再帰を防ぐ
    }
}
