using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZasDictWin.ViewModels;
using ZasDictWin.Views;

namespace ZasDictWin.Tests;

public class FieldIconTests
{
    private static TextBox Laid(TextBox box)
    {
        var host = new Grid { Children = { box } };
        host.Measure(new Size(400, 100));
        host.Arrange(new Rect(0, 0, 400, 100));
        host.UpdateLayout();
        return box;
    }

    /// <summary>入力の領域（PART_ContentHost）の左端。打った字はここから Padding の左だけ下がった位置に出る。</summary>
    private static double ContentLeft(TextBox box)
        => ((FrameworkElement)box.Template.FindName("PART_ContentHost", box)).TranslatePoint(new Point(0, 0), box).X;

    private static Viewbox Icon(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Viewbox v) return v;
            try { return Icon(child); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Viewbox が無い");
    }

    // 文字の倍率を上げるとアイコンは伸びる。入力の領域も一緒に後ろへ下がり、アイコンに重ならない。
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void TextStartsAfterTheIcon(double scale) => AppThread.Run(() =>
    {
        try
        {
            FontScaleState.Instance.Scale = scale;
            var box = new TextBox { Padding = new Thickness(4, 12, 40, 12) };
            FieldIcon.SetData(box, (Geometry)Application.Current.FindResource("Icon_search"));
            Laid(box);
            var icon = Icon(box);
            var iconRight = icon.TranslatePoint(new Point(icon.ActualWidth, 0), box).X;

            Assert.Equal(16 * scale, icon.ActualWidth, 1);
            Assert.True(ContentLeft(box) >= iconRight - 0.01, $"content {ContentLeft(box)}, icon right {iconRight}");
        }
        finally { FontScaleState.Instance.Scale = 1.0; }
    });

    // アイコンを付けない入力欄では、アイコンの列が場所を取らない。
    [Fact]
    public void WithoutIcon_TheColumnTakesNoSpace() => AppThread.Run(() =>
    {
        var box = Laid(new TextBox { Padding = new Thickness(18, 12, 18, 12) });
        Assert.Equal(Visibility.Collapsed, Icon(box).Visibility);
        Assert.True(ContentLeft(box) < 2, $"content {ContentLeft(box)}");
    });
}
