using System.Windows;
using System.Windows.Controls;

namespace ZasDictWin.Views;

/// <summary>
/// 見出しと中身を 1 行に並べ、収まらなければ中身を見出しの下へ折り返す入れ物。
/// ラジオグループの横一列の形（ZasDesignSystem components.md「ラジオグループ」）に使う。
/// 並べたときは中身が行の残りいっぱいに広がるので、UniformGrid の選択肢は残りを等分する。
/// 子は見出し・中身の順に 2 つ置く。
/// </summary>
public sealed class HeadedRow : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(HeadedRow),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>見出しと中身の間。並べたときは横、折り返したときは縦の間になる。</summary>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    public static readonly DependencyProperty HeadingWidthProperty = DependencyProperty.Register(
        nameof(HeadingWidth), typeof(double), typeof(HeadedRow),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>見出しの幅。縦に並べた複数のグループで選択肢の左端を揃えるときに指定する（既定は見出しの幅なり）。</summary>
    public double HeadingWidth { get => (double)GetValue(HeadingWidthProperty); set => SetValue(HeadingWidthProperty, value); }

    private bool _stacked;

    private UIElement? Heading => InternalChildren.Count > 0 ? InternalChildren[0] : null;
    private UIElement? Body => InternalChildren.Count > 1 ? InternalChildren[1] : null;

    protected override Size MeasureOverride(Size available)
    {
        if (Heading is not { } heading || Body is not { } body)
        {
            Heading?.Measure(available);
            return Heading?.DesiredSize ?? default;
        }

        heading.Measure(new Size(double.PositiveInfinity, available.Height));
        var headingWidth = double.IsNaN(HeadingWidth) ? heading.DesiredSize.Width : Math.Max(HeadingWidth, heading.DesiredSize.Width);

        // 中身は折り返さずに並べたときの幅を先に測り、見出しの横に収まるかを決める。
        body.Measure(new Size(double.PositiveInfinity, available.Height));
        var natural = body.DesiredSize.Width;
        _stacked = !double.IsInfinity(available.Width) && headingWidth + Gap + natural > available.Width;

        if (_stacked)
        {
            heading.Measure(new Size(available.Width, double.PositiveInfinity));
            body.Measure(new Size(available.Width, double.PositiveInfinity));
            return new Size(Math.Max(heading.DesiredSize.Width, body.DesiredSize.Width),
                            heading.DesiredSize.Height + Gap / 2 + body.DesiredSize.Height);
        }

        var rest = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width - headingWidth - Gap;
        body.Measure(new Size(rest, available.Height));
        return new Size(headingWidth + Gap + body.DesiredSize.Width, Math.Max(heading.DesiredSize.Height, body.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Heading is not { } heading || Body is not { } body)
        {
            Heading?.Arrange(new Rect(final));
            return final;
        }

        if (_stacked)
        {
            heading.Arrange(new Rect(0, 0, final.Width, heading.DesiredSize.Height));
            body.Arrange(new Rect(0, heading.DesiredSize.Height + Gap / 2, final.Width, body.DesiredSize.Height));
            return final;
        }

        var headingWidth = double.IsNaN(HeadingWidth) ? heading.DesiredSize.Width : Math.Max(HeadingWidth, heading.DesiredSize.Width);
        var height = final.Height;
        heading.Arrange(new Rect(0, (height - heading.DesiredSize.Height) / 2, headingWidth, heading.DesiredSize.Height));
        body.Arrange(new Rect(headingWidth + Gap, 0, Math.Max(0, final.Width - headingWidth - Gap), height));
        return final;
    }
}
