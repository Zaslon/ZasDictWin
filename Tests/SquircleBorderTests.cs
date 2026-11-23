using System.Windows;
using System.Windows.Media;
using ZasDictWin.Views;

namespace ZasDictWin.Tests;

public class SquircleBorderTests
{
    private static readonly Rect Box = new(0, 0, 100, 40);

    // 超楕円の角は 45° の位置で頂点から値の 0.159 倍まで張り出す。同じ値の円弧は 0.293 倍。
    // その間の点が内側に入れば円弧ではなく超楕円で描けている。
    [Fact]
    public void Corner_IsSquircleNotArc() => AppThread.Run(() =>
    {
        var outline = SquircleBorder.Outline(Box, new CornerRadius(12));
        var between = 12 * 0.22;
        Assert.True(outline.FillContains(new Point(between, between)));
        Assert.False(outline.FillContains(new Point(12 * 0.12, 12 * 0.12)));
    });

    [Fact]
    public void Outline_StaysInsideTheBox() => AppThread.Run(() =>
    {
        var bounds = SquircleBorder.Outline(Box, new CornerRadius(8)).Bounds;
        Assert.Equal(Box.Left, bounds.Left, 6);
        Assert.Equal(Box.Top, bounds.Top, 6);
        Assert.Equal(Box.Right, bounds.Right, 6);
        Assert.Equal(Box.Bottom, bounds.Bottom, 6);
    });

    // 辺より大きい値は CSS の border-radius と同じく縮める。縮めないと角どうしが重なって輪郭が裏返る。
    [Fact]
    public void OversizedRadius_IsScaledDown() => AppThread.Run(() =>
    {
        var bounds = SquircleBorder.Outline(new Rect(0, 0, 100, 10), new CornerRadius(12)).Bounds;
        Assert.Equal(10, bounds.Height, 6);
        Assert.True(SquircleBorder.Outline(new Rect(0, 0, 100, 10), new CornerRadius(12)).FillContains(new Point(50, 5)));
    });

    [Fact]
    public void ZeroRadius_IsASharpRectangle() => AppThread.Run(() =>
    {
        var outline = SquircleBorder.Outline(Box, new CornerRadius(0));
        Assert.True(outline.FillContains(new Point(0.2, 0.2)));
    });

    // 枠は太さの半分だけ内側の輪郭に引く。そのまま引くと外側の半分が要素の外にはみ出し、隣の要素に掛かる。
    [Fact]
    public void Stroke_StaysInsideTheElement() => AppThread.Run(() =>
    {
        var stroke = SquircleBorder.Stroke(Box, new CornerRadius(8), new Thickness(2));
        var drawn = stroke.GetRenderBounds(new Pen(Brushes.Black, 2));
        Assert.Equal(Box.Left, drawn.Left, 3);
        Assert.Equal(Box.Top, drawn.Top, 3);
        Assert.Equal(Box.Right, drawn.Right, 3);
        Assert.Equal(Box.Bottom, drawn.Bottom, 3);
    });

    // 下の辺の太さが 0 のタブは、下に線を引かない（中身とつなげるため）。左右の線は下端まで届く。
    [Fact]
    public void ZeroSide_IsNotStroked() => AppThread.Run(() =>
    {
        var stroke = SquircleBorder.Stroke(Box, new CornerRadius(8, 8, 0, 0), new Thickness(1, 1, 1, 0));
        var pen = new Pen(Brushes.Black, 1);
        Assert.False(stroke.StrokeContains(pen, new Point(50, Box.Bottom)));
        Assert.True(stroke.StrokeContains(pen, new Point(0.5, Box.Bottom)));
        Assert.True(stroke.StrokeContains(pen, new Point(50, 0.5)));
    });

    [Fact]
    public void Layout_ReservesBorderAndPadding() => AppThread.Run(() =>
    {
        var child = new FrameworkElement { Width = 30, Height = 10 };
        var border = new SquircleBorder { BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2), Child = child };
        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        border.Arrange(new Rect(border.DesiredSize));
        Assert.Equal(new Size(40, 16), border.DesiredSize);
        Assert.Equal(new Point(5, 3), child.TranslatePoint(new Point(0, 0), border));
    });
}
