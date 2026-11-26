using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ZasDictWin.Views;

/// <summary>
/// ラジオグループの並び（ZasDesignSystem components.md「ラジオグループ」、implementation/wpf.md「ラジオグループ」）。
/// 並びの容器（UniformGrid 等）に <c>v:RadioGroup.IsGroup="True"</c> を付けると、
/// 選択肢（RadioButton）の位置から区切り線と両端の角丸を付け、矢印キーでフォーカスと選択を同時に移す。
/// UniformGrid の Columns が 1 なら縦一列として扱う。
/// </summary>
public static class RadioGroup
{
    /// <summary>外枠（radius-control 8）から枠の太さ 1 を引いた角の値。外枠は子の背景を切り抜かないため、
    /// 両端の選択肢の外側の角にだけ付ける。</summary>
    private const double InnerRadius = 7;

    /// <summary>外枠と区切り線の太さ。RadioGroupFrame の BorderThickness と揃える。</summary>
    private const double FrameThickness = 1;

    private static double Outer(double inner) => inner > 0 ? inner + FrameThickness : 0;

    public static readonly DependencyProperty IsGroupProperty = DependencyProperty.RegisterAttached(
        "IsGroup", typeof(bool), typeof(RadioGroup), new PropertyMetadata(false, OnIsGroupChanged));

    public static bool GetIsGroup(DependencyObject o) => (bool)o.GetValue(IsGroupProperty);

    public static void SetIsGroup(DependencyObject o, bool value) => o.SetValue(IsGroupProperty, value);

    public static readonly DependencyProperty ItemCornerRadiusProperty = DependencyProperty.RegisterAttached(
        "ItemCornerRadius", typeof(CornerRadius), typeof(RadioGroup), new PropertyMetadata(default(CornerRadius)));

    public static CornerRadius GetItemCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(ItemCornerRadiusProperty);

    public static void SetItemCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(ItemCornerRadiusProperty, value);

    /// <summary>選択中の縁（外枠と区切り線のうち、選択肢に接する部分を塗りと同じ色で覆う枠）の位置。
    /// 外枠と区切り線は外枠や隣の選択肢が描くため、選択肢の中身の領域から枠の太さだけ外へ広げて重ねる。</summary>
    public static readonly DependencyProperty SelectedEdgeMarginProperty = DependencyProperty.RegisterAttached(
        "SelectedEdgeMargin", typeof(Thickness), typeof(RadioGroup), new PropertyMetadata(default(Thickness)));

    public static Thickness GetSelectedEdgeMargin(DependencyObject o) => (Thickness)o.GetValue(SelectedEdgeMarginProperty);

    public static void SetSelectedEdgeMargin(DependencyObject o, Thickness value) => o.SetValue(SelectedEdgeMarginProperty, value);

    /// <summary>選択中の縁の角。両端の外側の角だけ外枠と同じ値にし、外枠の線にぴったり重ねる。</summary>
    public static readonly DependencyProperty SelectedEdgeCornerRadiusProperty = DependencyProperty.RegisterAttached(
        "SelectedEdgeCornerRadius", typeof(CornerRadius), typeof(RadioGroup), new PropertyMetadata(default(CornerRadius)));

    public static CornerRadius GetSelectedEdgeCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(SelectedEdgeCornerRadiusProperty);

    public static void SetSelectedEdgeCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(SelectedEdgeCornerRadiusProperty, value);

    private static void OnIsGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel) return;
        panel.Loaded -= OnLoaded;
        panel.PreviewKeyDown -= OnPreviewKeyDown;
        if (e.NewValue is not true) return;
        panel.Loaded += OnLoaded;
        panel.PreviewKeyDown += OnPreviewKeyDown;
        // 中身の無い選択肢を見えなくしたとき（Visibility の切り替え）にも端を付け直せるよう、描画の度ではなく
        // 読み込み時と、子の表示が変わったときに付け直す。
        panel.LayoutUpdated += (_, _) => Arrange(panel);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Arrange((Panel)sender);

    private static bool IsVertical(Panel panel) => panel is UniformGrid { Columns: 1 };

    private static List<RadioButton> Items(Panel panel)
        => panel.Children.OfType<RadioButton>().Where(r => r.Visibility == Visibility.Visible).ToList();

    private static void Arrange(Panel panel)
    {
        var items = Items(panel);
        var vertical = IsVertical(panel);
        for (var i = 0; i < items.Count; i++)
        {
            var first = i == 0;
            var last = i == items.Count - 1;
            var thickness = first ? new Thickness(0) : vertical ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
            var r = InnerRadius;
            var radius = vertical
                ? new CornerRadius(first ? r : 0, first ? r : 0, last ? r : 0, last ? r : 0)
                : new CornerRadius(first ? r : 0, last ? r : 0, last ? r : 0, first ? r : 0);
            if (items[i].BorderThickness != thickness) items[i].BorderThickness = thickness;
            if (GetItemCornerRadius(items[i]) != radius) SetItemCornerRadius(items[i], radius);

            var edgeMargin = new Thickness(thickness.Left - FrameThickness, thickness.Top - FrameThickness,
                thickness.Right - FrameThickness, thickness.Bottom - FrameThickness);
            var edgeRadius = new CornerRadius(Outer(radius.TopLeft), Outer(radius.TopRight), Outer(radius.BottomRight), Outer(radius.BottomLeft));
            if (GetSelectedEdgeMargin(items[i]) != edgeMargin) SetSelectedEdgeMargin(items[i], edgeMargin);
            if (GetSelectedEdgeCornerRadius(items[i]) != edgeRadius) SetSelectedEdgeCornerRadius(items[i], edgeRadius);
        }
    }

    /// <summary>矢印キーで次の有効な選択肢へフォーカスと選択を同時に移す。無効の選択肢は飛ばし、端では反対の端へ回る。
    /// IsTabStop が偽の選択肢を WPF の方向移動に任せると移動先として扱われるか保証されないため、自前で移す。</summary>
    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0,
        };
        if (step == 0 || Keyboard.Modifiers != ModifierKeys.None) return;

        var items = Items((Panel)sender).Where(r => r.IsEnabled).ToList();
        if (items.Count == 0) return;
        var current = items.FindIndex(r => r.IsKeyboardFocused);
        if (current < 0) return;
        var next = items[(current + step + items.Count) % items.Count];
        Select(next);
        e.Handled = true;
    }

    /// <summary>押したときと同じく、選択と Click を起こす。IsChecked を OneWay で結び Click で Intent を上げる選択肢と、
    /// TwoWay で結んだ選択肢のどちらにも効くよう、SetCurrentValue（バインディングを外さない）で選ぶ。</summary>
    internal static void Select(RadioButton target)
    {
        target.Focus();
        if (target.IsChecked == true) return;
        target.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, target));
    }
}
