using System.Windows;
using System.Windows.Input;
using ZasDictWin.Root;

namespace ZasDictWin.Views;

/// <summary>
/// 枠の四隅を掴んで画面を割り直す添付ビヘイビア。Blender の画面分割と同じ操作で、
/// 掴んだ角を枠の内側へ引けば分割、隣の枠へ引けば結合になる（取りやめは Esc）。
/// ここは掴みの取得・解放と Intent の発火だけを受け持ち、下見・確定の判断は根（AppMediator）が行う。
/// 枠の番号・実寸・枠を基準にした位置は、Intent が通過する DockGroupPanel が補う。
/// </summary>
public static class AreaDrag
{
    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(AreaDrag), new PropertyMetadata(false, OnIsGripChanged));

    public static void SetIsGrip(DependencyObject o, bool value) => o.SetValue(IsGripProperty, value);

    public static bool GetIsGrip(DependencyObject o) => (bool)o.GetValue(IsGripProperty);

    private static void OnIsGripChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;

        element.MouseLeftButtonDown -= OnMouseDown;
        element.MouseMove -= OnMouseMove;
        element.MouseLeftButtonUp -= OnMouseUp;
        element.LostMouseCapture -= OnLostCapture;
        if (e.NewValue is not true) return;

        element.MouseLeftButtonDown += OnMouseDown;
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostCapture;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement element) return;
        var args = new UiIntentEventArgs(IntentKind.AreaGripPressed) { ScreenPoint = PointerCapture.ScreenOf(element, e) };
        // 引いた向きから割り方を決めるので、どの角を掴んだかを伝える（置き場所がそのまま角の位置）。
        args.Context.Corner = (element.HorizontalAlignment == HorizontalAlignment.Right,
                               element.VerticalAlignment == VerticalAlignment.Bottom) switch
        {
            (false, false) => AreaCorner.TopLeft,
            (true, false) => AreaCorner.TopRight,
            (false, true) => AreaCorner.BottomLeft,
            (true, true) => AreaCorner.BottomRight,
        };
        element.RaiseIntent(args);
        element.CaptureMouse();
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { IsMouseCaptured: true } element) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        element.RaiseIntent(IntentKind.PointerMoved, PointerCapture.ScreenOf(element, e));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { IsMouseCaptured: true } element) return;
        var screen = PointerCapture.ScreenOf(element, e);
        PointerCapture.Release(element);
        element.RaiseIntent(IntentKind.PointerReleased, screen);
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || PointerCapture.IsReleasing(element)) return;
        element.RaiseIntent(IntentKind.PointerCaptureLost);
    }
}
