using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// 角を超楕円（|x|⁴ + |y|⁴ = 1 の 4 分の 1）で描く <see cref="Border"/>（ZasDesignSystem implementation/wpf.md「角の形」）。
/// Border の CornerRadius は円弧で描くため、角を丸める部品のテンプレートでは Border の代わりにこれを使う。
/// プロパティは Border のものを共有するので、Border 向けに書いた Setter・Trigger・TemplateBinding がそのまま効く。
/// 子は輪郭で切り抜かない（tokens.md「角の丸み」は切り抜きで模すことを禁じる）。
/// </summary>
public sealed class SquircleBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = Border.BackgroundProperty.AddOwner(typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender));

    public static readonly DependencyProperty BorderBrushProperty = Border.BorderBrushProperty.AddOwner(typeof(SquircleBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender));

    public static readonly DependencyProperty BorderThicknessProperty = Border.BorderThicknessProperty.AddOwner(typeof(SquircleBorder),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(SquircleBorder),
        new FrameworkPropertyMetadata(new CornerRadius(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = Border.PaddingProperty.AddOwner(typeof(SquircleBorder),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public Thickness BorderThickness { get => (Thickness)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var chrome = Inflate(BorderThickness, Padding);
        if (Child is null) return chrome;
        Child.Measure(new Size(Math.Max(0, constraint.Width - chrome.Width), Math.Max(0, constraint.Height - chrome.Height)));
        return new Size(Child.DesiredSize.Width + chrome.Width, Child.DesiredSize.Height + chrome.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not null)
        {
            var b = BorderThickness;
            var p = Padding;
            var chrome = Inflate(b, p);
            Child.Arrange(new Rect(b.Left + p.Left, b.Top + p.Top,
                Math.Max(0, arrangeSize.Width - chrome.Width), Math.Max(0, arrangeSize.Height - chrome.Height)));
        }
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (Background is { } fill) dc.DrawGeometry(fill, null, Outline(bounds, CornerRadius));

        var t = BorderThickness;
        var width = Math.Max(Math.Max(t.Left, t.Right), Math.Max(t.Top, t.Bottom));
        if (BorderBrush is { } stroke && width > 0)
            dc.DrawGeometry(null, new Pen(stroke, width), Stroke(bounds, CornerRadius, t));
    }

    private static Size Inflate(Thickness a, Thickness b)
        => new(a.Left + a.Right + b.Left + b.Right, a.Top + a.Bottom + b.Top + b.Bottom);

    /// <summary>角 1 つを描く点の数。UI 倍率 2 で角が 24 になっても、折れ線の角が見えない細かさ。</summary>
    private const int CornerSteps = 24;

    /// <summary>矩形を超楕円の角で丸めた、閉じた輪郭。塗りと、フォーカス表示のような枠だけの図形に使う。</summary>
    public static Geometry Outline(Rect rect, CornerRadius radius)
        => Build(rect, Clamp(rect, radius), new[] { true, true, true, true });

    /// <summary>枠の線の中心を通る輪郭。線が要素の外にはみ出さないよう、太さのある辺だけ半分内側へ寄せ、角の値も同じだけ減らす。
    /// 太さ 0 の辺（タブの下辺など）は線を引かず、その両端の角も描かない。</summary>
    public static Geometry Stroke(Rect rect, CornerRadius radius, Thickness thickness)
    {
        var w = Math.Max(Math.Max(thickness.Left, thickness.Right), Math.Max(thickness.Top, thickness.Bottom));
        var h = w / 2;
        var inner = new Rect(
            rect.Left + (thickness.Left > 0 ? h : 0),
            rect.Top + (thickness.Top > 0 ? h : 0),
            Math.Max(0, rect.Width - (thickness.Left > 0 ? h : 0) - (thickness.Right > 0 ? h : 0)),
            Math.Max(0, rect.Height - (thickness.Top > 0 ? h : 0) - (thickness.Bottom > 0 ? h : 0)));
        var shrunk = new CornerRadius(
            Math.Max(0, radius.TopLeft - h), Math.Max(0, radius.TopRight - h),
            Math.Max(0, radius.BottomRight - h), Math.Max(0, radius.BottomLeft - h));
        return Build(inner, Clamp(inner, shrunk),
            new[] { thickness.Top > 0, thickness.Right > 0, thickness.Bottom > 0, thickness.Left > 0 });
    }

    /// <summary>隣り合う角の値の和が辺の長さを超えるときは、CSS の border-radius と同じく全ての角を同じ比で縮める。</summary>
    private static CornerRadius Clamp(Rect rect, CornerRadius r)
    {
        var f = 1.0;
        void Fit(double side, double a, double b) { if (a + b > side) f = Math.Min(f, side / (a + b)); }
        Fit(rect.Width, r.TopLeft, r.TopRight);
        Fit(rect.Width, r.BottomLeft, r.BottomRight);
        Fit(rect.Height, r.TopLeft, r.BottomLeft);
        Fit(rect.Height, r.TopRight, r.BottomRight);
        return f >= 1 ? r : new CornerRadius(r.TopLeft * f, r.TopRight * f, r.BottomRight * f, r.BottomLeft * f);
    }

    /// <summary>
    /// 時計回りに 上辺・右上の角・右辺・右下の角・下辺・左下の角・左辺・左上の角 と並べ、
    /// 描く辺（sides：上・右・下・左）が途切れる所で図形を分ける。角は両側の辺を描くときだけ描く。
    /// </summary>
    private static Geometry Build(Rect rect, CornerRadius r, bool[] sides)
    {
        var corners = new[]
        {
            Corner(new Point(rect.Right - r.TopRight, rect.Top + r.TopRight), r.TopRight, 270),
            Corner(new Point(rect.Right - r.BottomRight, rect.Bottom - r.BottomRight), r.BottomRight, 0),
            Corner(new Point(rect.Left + r.BottomLeft, rect.Bottom - r.BottomLeft), r.BottomLeft, 90),
            Corner(new Point(rect.Left + r.TopLeft, rect.Top + r.TopLeft), r.TopLeft, 180),
        };

        var closed = sides.All(s => s);
        // 開いた図形は、描かない辺の次の辺から書き始める。そうすると 1 周の途中で図形が切れるのは描かない辺だけになる。
        var start = closed ? 0 : (Array.FindIndex(sides, s => !s) + 1) % 4;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var open = false;
            for (var k = 0; k < 4; k++)
            {
                var side = (start + k) % 4;
                if (!sides[side]) { open = false; continue; }
                // side の辺は、前の角の終点から side の後ろの角の始点まで。
                var before = corners[(side + 3) % 4];
                var after = corners[side];
                if (!open)
                {
                    ctx.BeginFigure(before[^1], closed, closed);
                    open = true;
                }
                ctx.LineTo(after[0], true, false);
                if (sides[(side + 1) % 4]) ctx.PolyLineTo(after.Skip(1).ToList(), true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>中心 c、値 r の角を、開始角 from°から 90°分（画面座標で時計回り）たどる点列。
    /// 超楕円の媒介変数表示 (cos^½ t, sin^½ t) を使う。値が 0 なら角の頂点 1 点だけになる。</summary>
    private static Point[] Corner(Point c, double r, double from)
    {
        if (r <= 0) return new[] { c };
        var points = new Point[CornerSteps + 1];
        for (var i = 0; i <= CornerSteps; i++)
        {
            var t = (from + 90.0 * i / CornerSteps) * Math.PI / 180;
            var cos = Math.Cos(t);
            var sin = Math.Sin(t);
            points[i] = new Point(c.X + r * Math.Sign(cos) * Math.Sqrt(Math.Abs(cos)),
                                  c.Y + r * Math.Sign(sin) * Math.Sqrt(Math.Abs(sin)));
        }
        return points;
    }
}
