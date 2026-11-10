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
    /// <summary>外枠（radius-control 8）から枠の太さ 1 を引いた角丸。外枠の Border は子の背景を切り抜かないため、
    /// 両端の選択肢の外側の角にだけ付ける。</summary>
    private const double InnerRadius = 7;

    public static readonly DependencyProperty IsGroupProperty = DependencyProperty.RegisterAttached(
        "IsGroup", typeof(bool), typeof(RadioGroup), new PropertyMetadata(false, OnIsGroupChanged));

    public static bool GetIsGroup(DependencyObject o) => (bool)o.GetValue(IsGroupProperty);

    public static void SetIsGroup(DependencyObject o, bool value) => o.SetValue(IsGroupProperty, value);

    public static readonly DependencyProperty ItemCornerRadiusProperty = DependencyProperty.RegisterAttached(
        "ItemCornerRadius", typeof(CornerRadius), typeof(RadioGroup), new PropertyMetadata(default(CornerRadius)));

    public static CornerRadius GetItemCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(ItemCornerRadiusProperty);

    public static void SetItemCornerRadius(DependencyObject o, CornerRadius value) => o.SetValue(ItemCornerRadiusProperty, value);

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
