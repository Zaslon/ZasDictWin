using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// オーバーレイのヘッダを掴んでドッキング先を選ばせる添付ビヘイビア。
/// ドロップ先の当たり判定は MainWindow の <c>DockZoneHost</c>（升目に DockSide を Tag で持たせた
/// Grid）を InputHitTest して拾う。判定は画面に描いている升目そのものなので、XAML 側の割合を
/// 変えれば判定も追従するが、この名前を変えるとドラッグしても何も起きなくなる。
/// </summary>
public static class OverlayDrag
{
    // 同時に動かせるオーバーレイは 1 枚なので、掴んでいる要素は静的に 1 つだけ覚える。
    private static UIElement? _grip;
    private static Point _origin;
    private static bool _dragging;

    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(OverlayDrag), new PropertyMetadata(false, OnIsGripChanged));

    public static void SetIsGrip(DependencyObject o, bool value) => o.SetValue(IsGripProperty, value);

    public static bool GetIsGrip(DependencyObject o) => (bool)o.GetValue(IsGripProperty);

    /// <summary>Esc でのドラッグ中止。掴んでいなければ false を返して他の Esc 処理に譲る。</summary>
    public static bool Cancel()
    {
        if (_grip is null) return false;
        var wasDragging = _dragging;
        Release();
        if (wasDragging) OverlayDockState.Instance.CancelDrag();
        return wasDragging;
    }

    private static void OnIsGripChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;

        element.MouseLeftButtonDown -= OnMouseDown;
        element.MouseMove -= OnMouseMove;
        element.MouseLeftButtonUp -= OnMouseUp;
        element.LostMouseCapture -= OnLostCapture;

        if (e.NewValue is not true) return;

        // ボタン類は Cursor=Hand を自前で持つので、ここで掴める見た目にしても潰さない。
        element.Cursor = Cursors.SizeAll;
        element.MouseLeftButtonDown += OnMouseDown;
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostCapture;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // ヘッダに載っている［閉じる］などが先に処理した押下は掴みに使わない。
        if (e.Handled || sender is not UIElement element) return;
        _grip = element;
        _dragging = false;
        _origin = e.GetPosition(element);
        element.CaptureMouse();
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip) || _grip is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(_grip) - _origin;
        if (!_dragging)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            OverlayDockState.Instance.BeginDrag();
            // 候補の升目はここで初めて可視になる。測り直すまで InputHitTest が当たらない。
            ZoneHost()?.UpdateLayout();
        }

        if (HitZone(e) is { } side) OverlayDockState.Instance.HoverSide = side;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip)) return;
        var side = OverlayDockState.Instance.HoverSide;
        var wasDragging = _dragging;
        // 先に掴みを解く。LostMouseCapture は _dragging を見て中止するので、順序を変えると確定が消える。
        Release();
        if (wasDragging) OverlayDockState.Instance.CompleteDrag(side);
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip)) return;
        if (_dragging) OverlayDockState.Instance.CancelDrag();
        _grip = null;
        _dragging = false;
    }

    private static void Release()
    {
        var grip = _grip;
        _grip = null;
        _dragging = false;
        grip?.ReleaseMouseCapture();
    }

    /// <summary>升目の隙間や画面外では null を返し、直前の候補を保たせる。</summary>
    private static DockSide? HitZone(MouseEventArgs e)
    {
        if (ZoneHost() is not { } host) return null;
        var hit = host.InputHitTest(e.GetPosition(host)) as DependencyObject;
        while (hit is not null)
        {
            if (hit is FrameworkElement { Tag: DockSide side }) return side;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private static FrameworkElement? ZoneHost()
        => _grip is DependencyObject o
            ? Window.GetWindow(o)?.FindName("DockZoneHost") as FrameworkElement
            : null;
}
