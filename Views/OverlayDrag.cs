using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 「ここで離すと独立ウィンドウになる」ことを示す、カーソルに付いてくる小さな影。
/// 窓の外には枠が無く着色する相手がいないので、分割・結合と同じ「離す前に結果が分かる」を
/// これで賄う。掴みを取られないよう、出しても前に出ない窓（ShowActivated=false）にしてある。
/// 枠の当たり判定（<see cref="DockHit"/>）は本体と独立ウィンドウだけを見るので、この窓は邪魔をしない。
/// </summary>
internal sealed class DragGhost : Window
{
    public DragGhost(string title)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0.9;

        var scale = FontScaleState.Instance.Scale;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12 * scale,
            Foreground = Brush("Text"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "離すと独立ウィンドウ",
            FontSize = 11 * scale,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Brush("Muted"),
        });

        // 影は本体と同じ書体で出す（コードで組むので、XAML の既定は効かない）。
        FontFamily = new FontFamily("Yu Gothic UI");
        Content = new Border
        {
            Background = Brush("Raised"),
            BorderBrush = Brush("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = stack,
        };
    }

    /// <summary>カーソルの右下に添える。<paramref name="at"/> は画面上の位置（DIP）。</summary>
    public void MoveTo(Point at)
    {
        Left = at.X + 14;
        Top = at.Y + 16;
    }

    private static Brush Brush(string key)
        => Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>
/// タブを掴んで別の枠へ運ばせる添付ビヘイビア。付ける相手の DataContext が
/// <see cref="OverlayViewModel"/> であることが前提（タブの見出しに付ける）。
/// ドロップ先の当たり判定は、実際に並んでいる枠（<see cref="DockGroupPanel"/>）そのものを
/// InputHitTest して拾う。カーソルが乗った枠が着色され、そこで離せばその枠へ移る。
/// 運び先は窓をまたげる。本体の窓でも独立ウィンドウでも同じように落とせて、
/// アプリのどの窓にも乗っていない場所で離すとそのタブが独立ウィンドウになる。
/// 落とす枠が無いときは、先に角を引いて枠を増やす（<see cref="AreaDrag"/>）。
/// </summary>
public static class OverlayDrag
{
    // 同時に運べるタブは 1 枚なので、掴んでいる要素は静的に 1 つだけ覚える。
    private static UIElement? _grip;
    private static Point _origin;
    private static bool _dragging;
    private static DragGhost? _ghost;

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
        if (wasDragging) OverlayDragState.Instance.Cancel();
        return wasDragging;
    }

    private static void OnIsGripChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;

        element.MouseLeftButtonDown -= OnMouseDown;
        element.MouseMove -= OnMouseMove;
        element.MouseLeftButtonUp -= OnMouseUp;
        element.LostMouseCapture -= OnLostCapture;

        // 掴めなくなった要素に「動かせる」カーソルを残さない（据え置きのタブが該当する）。
        if (e.NewValue is not true)
        {
            element.ClearValue(FrameworkElement.CursorProperty);
            return;
        }

        // タブに載せた［✕］は Cursor=Hand を自前で持つので、ここで掴める見た目にしても潰さない。
        element.Cursor = Cursors.SizeAll;
        element.MouseLeftButtonDown += OnMouseDown;
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostCapture;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // タブに載っている［✕］が先に処理した押下は掴みに使わない。
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
            if ((_grip as FrameworkElement)?.DataContext is not OverlayViewModel vm) return;
            _dragging = true;
            OverlayDragState.Instance.BeginDrag(vm, DockHit.AncestorLeaf(_grip));
        }

        UpdateHover(e);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip)) return;
        var wasDragging = _dragging;
        // 先に掴みを解く。LostMouseCapture は _dragging を見て中止するので、順序を変えると確定が消える。
        Release();
        if (wasDragging) OverlayDragState.Instance.CompleteDrag();
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip)) return;
        if (_dragging) OverlayDragState.Instance.Cancel();
        HideGhost();
        _grip = null;
        _dragging = false;
    }

    private static void Release()
    {
        var grip = _grip;
        HideGhost();
        _grip = null;
        _dragging = false;
        grip?.ReleaseMouseCapture();
    }

    /// <summary>
    /// カーソルの下にある枠へ、乗っている位置を反映する。端に寄せていれば分割の下見、
    /// 真ん中なら枠全体の着色（タブとして合流）。窓の中でも枠に乗っていなければ両方消える
    /// （そのまま離しても動かさない）。アプリのどの窓にも乗っていなければ、離した場所を
    /// 覚えておいて独立ウィンドウにする。
    /// </summary>
    private static void UpdateHover(MouseEventArgs e)
    {
        if (_grip is null)
        {
            OverlayDragState.Instance.SetHover(null, null);
            return;
        }

        var screen = _grip.PointToScreen(e.GetPosition(_grip));
        if (DockHit.AtScreen(screen) is not { } hit)
        {
            var at = ToDip(screen);
            OverlayDragState.Instance.SetOutside(at);
            ShowGhost(at);
            return;
        }
        HideGhost();
        if (hit.Panel is not { } panel || hit.Leaf is not { } leaf)
        {
            OverlayDragState.Instance.SetHover(null, null);
            return;
        }

        var local = panel.PointFromScreen(screen);
        var split = DockHit.EdgeSplit(panel.ActualWidth, panel.ActualHeight, local.X, local.Y);
        OverlayDragState.Instance.SetHover(leaf, split);
    }

    private static void ShowGhost(Point at)
    {
        if (_ghost is null)
        {
            if ((_grip as FrameworkElement)?.DataContext is not OverlayViewModel vm) return;
            _ghost = new DragGhost(vm.Title);
            _ghost.MoveTo(at);
            _ghost.Show();
            return;
        }
        _ghost.MoveTo(at);
    }

    private static void HideGhost()
    {
        var ghost = _ghost;
        _ghost = null;
        ghost?.Close();
    }

    /// <summary>
    /// 画面上の位置をデバイスピクセルから DIP へ直す。Window.Left / Top が DIP なので、
    /// 新しい窓を置く座標はこちらに合わせる（掴んでいる窓の拡大率で換算する）。
    /// </summary>
    private static Point ToDip(Point screen)
        => PresentationSource.FromVisual(_grip) is { CompositionTarget: { } target }
            ? target.TransformFromDevice.Transform(screen)
            : screen;
}
