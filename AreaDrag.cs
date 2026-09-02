using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>カーソルの下にある枠を拾う。タブの運びと角の引き回しで同じ判定を使う。</summary>
internal static class DockHit
{
    /// <summary>端からこの範囲内にカーソルがあれば、タブとしての合流ではなく分割の下見を出す。</summary>
    private const double EdgeZone = 48;

    /// <summary>どの枠にも乗っていなければ null。</summary>
    public static DockLeaf? LeafAt(UIElement root, Point point) => PanelAt(root, point)?.Leaf;

    /// <summary>どの枠にも乗っていなければ null。枠そのもの（座標変換に要る）とセットで返す。</summary>
    public static (DockGroupPanel Panel, DockLeaf Leaf)? PanelAt(UIElement root, Point point)
    {
        var hit = root.InputHitTest(point) as DependencyObject;
        while (hit is not null)
        {
            if (hit is DockGroupPanel { DataContext: DockLeaf leaf } panel) return (panel, leaf);
            // 当たるのは描いている要素なので、視覚ツリーだけ遡れば枠に届く。
            hit = hit is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }
        return null;
    }

    /// <summary>要素そのものが属する枠。タブを掴んだ時点でどの枠から運び出したかを覚えるのに使う。</summary>
    public static DockLeaf? AncestorLeaf(DependencyObject? o)
    {
        while (o is not null)
        {
            if (o is DockGroupPanel { DataContext: DockLeaf leaf }) return leaf;
            o = o is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }
        return null;
    }

    /// <summary>
    /// タブを運んでいるカーソルが枠のどのあたりにあるか。端に寄せていれば、
    /// その辺へ新しい枠を割り出す下見を返す。真ん中（合流）なら null。
    /// </summary>
    public static SplitPreview? EdgeSplit(double width, double height, double x, double y)
    {
        var best = EdgeZone;
        SplitPreview? preview = null;

        void Consider(double distance, DockAxis axis, bool newIsSecond, double total)
        {
            if (total < DockSplit.MinLeafSize * 2 || distance >= best) return;
            best = distance;
            preview = new SplitPreview(axis, 0.5, newIsSecond);
        }

        Consider(x, DockAxis.Columns, false, width);          // 左端 → 左に新しい枠
        Consider(width - x, DockAxis.Columns, true, width);   // 右端 → 右に新しい枠
        Consider(y, DockAxis.Rows, false, height);            // 上端 → 上に新しい枠
        Consider(height - y, DockAxis.Rows, true, height);    // 下端 → 下に新しい枠

        return preview;
    }
}

/// <summary>
/// 枠の四隅を掴んで画面を割り直す添付ビヘイビア。Blender の画面分割と同じ操作で、
/// 掴んだ角を枠の内側へ引けば分割、隣の枠へ引けば結合になる。
/// 分割は下見（これからできる側の着色）、結合は消える側の着色で行き先を示し、離した時点で確定する。
/// 取りやめは Esc。
/// </summary>
public static class AreaDrag
{
    // 同時に引ける角は 1 つなので、掴んでいる状態は静的に 1 組だけ覚える。
    private static FrameworkElement? _grip;
    private static DockGroupPanel? _panel;
    private static DockLeaf? _leaf;
    private static Point _origin;
    private static bool _dragging;
    private static bool _fromRight;
    private static bool _fromBottom;
    private static List<DockLeaf> _joining = new();

    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(AreaDrag), new PropertyMetadata(false, OnIsGripChanged));

    public static void SetIsGrip(DependencyObject o, bool value) => o.SetValue(IsGripProperty, value);

    public static bool GetIsGrip(DependencyObject o) => (bool)o.GetValue(IsGripProperty);

    /// <summary>Esc での取りやめ。掴んでいなければ false を返して他の Esc 処理に譲る。</summary>
    public static bool Cancel()
    {
        if (_grip is null) return false;
        var wasDragging = _dragging;
        ClearPreview();
        Release();
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

        element.MouseLeftButtonDown += OnMouseDown;
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostCapture;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement element) return;
        var panel = Ancestor(element);
        var leaf = panel?.DataContext as DockLeaf;
        if (panel is null || leaf is null) return;

        _grip = element;
        _panel = panel;
        _leaf = leaf;
        _dragging = false;
        // 引いた向きから割り方を決めるので、どの角を掴んだかを覚えておく（置き場所がそのまま角の位置）。
        _fromRight = element.HorizontalAlignment == HorizontalAlignment.Right;
        _fromBottom = element.VerticalAlignment == VerticalAlignment.Bottom;
        _origin = e.GetPosition(panel);
        element.CaptureMouse();
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip) || _panel is null || _leaf is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var point = e.GetPosition(_panel);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
        }

        var inside = point.X >= 0 && point.Y >= 0
                     && point.X <= _panel.ActualWidth && point.Y <= _panel.ActualHeight;
        if (inside) ShowSplit(point);
        else ShowJoin(e);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip)) return;
        var leaf = _leaf;
        var preview = leaf?.Preview;
        var joining = _joining.Count > 0;
        var wasDragging = _dragging;
        // 先に下見と掴みを解く。確定は木を組み替えるので、掴んだままだと解けなくなる。
        ClearPreview();
        Release();
        if (!wasDragging || leaf is null) return;

        if (preview is { } p) leaf.Owner?.Split(leaf, p.Axis, p.Ratio, p.NewIsSecond);
        else if (joining) leaf.Owner?.Join(leaf);
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _grip)) return;
        ClearPreview();
        _grip = null;
        _panel = null;
        _leaf = null;
        _dragging = false;
    }

    /// <summary>枠の内側。引いた向きで割り方を決め、これからできる側を下見に出す。</summary>
    private static void ShowSplit(Point point)
    {
        if (_panel is null || _leaf is null) return;
        ClearJoin();

        var axis = Math.Abs(point.X - _origin.X) >= Math.Abs(point.Y - _origin.Y)
            ? DockAxis.Columns
            : DockAxis.Rows;
        var total = axis == DockAxis.Columns ? _panel.ActualWidth : _panel.ActualHeight;
        // 割った先が使い物にならない細さになる枠は割らせない。
        if (total < DockSplit.MinLeafSize * 2)
        {
            _leaf.Preview = null;
            return;
        }

        var margin = DockSplit.MinLeafSize / total;
        var ratio = Math.Clamp((axis == DockAxis.Columns ? point.X : point.Y) / total, margin, 1 - margin);
        // 新しい枠は掴んだ角の側にできる（角から引き出す感覚に合わせる）。
        var newIsSecond = axis == DockAxis.Columns ? _fromRight : _fromBottom;
        _leaf.Preview = new SplitPreview(axis, ratio, newIsSecond);
    }

    /// <summary>枠の外。隣（＝同じ境目を挟む相手）に乗っているときだけ、消える側を着色する。</summary>
    private static void ShowJoin(MouseEventArgs e)
    {
        if (_grip is null || _leaf is null) return;
        _leaf.Preview = null;

        var sibling = _leaf.Parent?.Other(_leaf);
        var hovered = Window.GetWindow(_grip) is { } window
            ? DockHit.LeafAt(window, e.GetPosition(window))
            : null;
        if (sibling is null || hovered is null || !sibling.Leaves.Contains(hovered))
        {
            ClearJoin();
            return;
        }

        var leaves = sibling.Leaves.ToList();
        if (leaves.Count == _joining.Count && leaves.All(_joining.Contains)) return;
        ClearJoin();
        _joining = leaves;
        foreach (var leaf in _joining) leaf.IsJoinTarget = true;
    }

    private static void ClearJoin()
    {
        foreach (var leaf in _joining) leaf.IsJoinTarget = false;
        _joining = new List<DockLeaf>();
    }

    private static void ClearPreview()
    {
        if (_leaf is not null) _leaf.Preview = null;
        ClearJoin();
    }

    private static void Release()
    {
        var grip = _grip;
        _grip = null;
        _panel = null;
        _leaf = null;
        _dragging = false;
        grip?.ReleaseMouseCapture();
    }

    private static DockGroupPanel? Ancestor(DependencyObject? o)
    {
        while (o is not null)
        {
            if (o is DockGroupPanel panel) return panel;
            o = o is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }
        return null;
    }
}
