using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// 一覧の行を掴んで並べ替える添付ビヘイビア。行のつまみに <see cref="IsGripProperty"/> を付けると、
/// その行が属する <see cref="ItemsControl"/> の ItemsSource（<see cref="IList"/>）を直接並べ替える。
/// 掴んでいる間は運んでいる行そのものを動かして見せるので、落とし先を示す線や影は持たない。
/// マウスを捕まえる先は行ではなく一覧。行は並べ替えのたびに別の位置へ運ばれるので、
/// 行に捕まえさせると掴みが外れる。
/// </summary>
public static class RowDrag
{
    // 同時に運べる行は 1 つなので、掴んでいる状態は静的に 1 組だけ覚える。
    private static ItemsControl? _list;
    private static object? _item;
    private static FrameworkElement? _row;
    private static int _origin = -1;
    private static Point _start;
    private static bool _dragging;

    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(RowDrag), new PropertyMetadata(false, OnIsGripChanged));

    public static void SetIsGrip(DependencyObject o, bool value) => o.SetValue(IsGripProperty, value);

    public static bool GetIsGrip(DependencyObject o) => (bool)o.GetValue(IsGripProperty);

    /// <summary>Esc での並べ替え中止。掴んでいなければ false を返して他の Esc 処理に譲る。</summary>
    public static bool Cancel()
    {
        if (_list is null) return false;
        var wasDragging = _dragging;
        if (wasDragging) MoveBack();
        Release();
        return wasDragging;
    }

    private static void OnIsGripChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;

        element.MouseLeftButtonDown -= OnMouseDown;

        if (e.NewValue is not true)
        {
            element.ClearValue(FrameworkElement.CursorProperty);
            return;
        }

        element.Cursor = Cursors.SizeAll;
        element.MouseLeftButtonDown += OnMouseDown;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement grip) return;
        if (Ancestor(grip) is not { } list || list.ItemsSource is not IList source) return;

        var item = grip.DataContext;
        var index = source.IndexOf(item);
        if (index < 0) return;

        _list = list;
        _item = item;
        _row = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        _origin = index;
        _dragging = false;
        _start = e.GetPosition(list);

        list.MouseMove += OnMouseMove;
        list.MouseLeftButtonUp += OnMouseUp;
        list.LostMouseCapture += OnLostCapture;
        list.CaptureMouse();
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _list) || _list is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var at = e.GetPosition(_list);
        if (!_dragging)
        {
            if (Math.Abs(at.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(at.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            if (_row is not null) _row.Opacity = 0.6;
        }

        if (_list.ItemsSource is not IList source) return;
        var from = source.IndexOf(_item);
        var to = IndexAt(_list, at.Y);
        if (from >= 0 && to >= 0 && to != from) MoveItem(source, from, to);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, _list)) return;
        // 運んだ先がそのまま並び順になるので、確定は掴みを解くだけでよい。
        Release();
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _list)) return;
        if (_dragging) MoveBack();
        Release();
    }

    private static void Release()
    {
        var list = _list;
        var row = _row;
        _list = null;
        _item = null;
        _row = null;
        _origin = -1;
        _dragging = false;

        if (row is not null) row.Opacity = 1;
        if (list is null) return;
        list.MouseMove -= OnMouseMove;
        list.MouseLeftButtonUp -= OnMouseUp;
        list.LostMouseCapture -= OnLostCapture;
        list.ReleaseMouseCapture();
    }

    private static void MoveBack()
    {
        if (_list?.ItemsSource is not IList source) return;
        var from = source.IndexOf(_item);
        if (from >= 0 && _origin >= 0 && from != _origin) MoveItem(source, from, _origin);
    }

    /// <summary>
    /// 一覧の中で <paramref name="y"/>（一覧を基準にした縦位置）が指す行の番号。
    /// 行の高さは揃っていないので、上から順に中心線を跨いだかどうかで決める。
    /// </summary>
    private static int IndexAt(ItemsControl list, double y)
    {
        var count = list.Items.Count;
        for (var i = 0; i < count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
            if (!row.IsVisible) continue;
            var top = row.TransformToAncestor(list).Transform(new Point(0, 0)).Y;
            if (y < top + row.ActualHeight / 2) return i;
        }
        return count - 1;
    }

    /// <summary>
    /// 並べ替えは Move（持っていれば）で行う。取り除いて差し込み直すと行そのものが作り直され、
    /// 掴んだままの行の入力状態（選択位置や変換中の文字）が消えるため。
    /// </summary>
    private static void MoveItem(IList list, int from, int to)
    {
        var move = list.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
        if (move is not null)
        {
            move.Invoke(list, new object[] { from, to });
            return;
        }
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
    }

    private static ItemsControl? Ancestor(DependencyObject o)
    {
        for (var p = VisualTreeHelper.GetParent(o); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ItemsControl list) return list;
        return null;
    }
}
