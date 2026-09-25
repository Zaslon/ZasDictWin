using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.Root;

namespace ZasDictWin.Views;

/// <summary>
/// 一覧の行を掴んで並べ替える添付ビヘイビア。行のつまみに <see cref="IsGripProperty"/> を付けると、
/// その行が属する <see cref="ItemsControl"/> の ItemsSource（<see cref="IList"/>）が並べ替えの対象になる。
/// 掴んでいる間は運んでいる行そのものを動かして見せるので、落とし先を示す線や影は持たない。
/// ここは掴みの取得・解放と、行の実寸から落とし先の行番号を測ることだけを受け持つ（判断は根が行う）。
/// マウスを捕まえる先は行ではなく一覧。行は並べ替えのたびに別の位置へ運ばれるので、
/// 行に捕まえさせると掴みが外れる。
/// </summary>
public static class RowDrag
{
    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(RowDrag), new PropertyMetadata(false, OnIsGripChanged));

    public static void SetIsGrip(DependencyObject o, bool value) => o.SetValue(IsGripProperty, value);

    public static bool GetIsGrip(DependencyObject o) => (bool)o.GetValue(IsGripProperty);

    /// <summary>
    /// 行を <paramref name="from"/> から <paramref name="to"/> へ運ぶ。並べ替えは Move（持っていれば）で行う。
    /// 取り除いて差し込み直すと行そのものが作り直され、掴んだままの行の入力状態
    /// （選択位置や変換中の文字）が消えるため。
    /// </summary>
    public static void ApplyOrder(ItemsControl list, int from, int to)
    {
        if (list.ItemsSource is not IList source) return;
        if (from < 0 || to < 0 || from >= source.Count || to >= source.Count || from == to) return;
        var move = source.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
        if (move is not null)
        {
            move.Invoke(source, new object[] { from, to });
            return;
        }
        var item = source[from];
        source.RemoveAt(from);
        source.Insert(to, item);
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

        var index = source.IndexOf(grip.DataContext);
        if (index < 0) return;

        // 並べ替えの対象（一覧そのもの）は Payload で預ける。行の実体は View にしか無い。
        var args = new UiIntentEventArgs(IntentKind.RowGripPressed, list) { ScreenPoint = PointerCapture.ScreenOf(grip, e) };
        args.Context.RowIndex = index;
        args.Context.LeafLocalPoint = e.GetPosition(list);
        grip.RaiseIntent(args);

        list.MouseMove += OnListMouseMove;
        list.MouseLeftButtonUp += OnListMouseUp;
        list.LostMouseCapture += OnListLostCapture;
        list.CaptureMouse();
    }

    private static void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ItemsControl list || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(list);
        var args = new UiIntentEventArgs(IntentKind.PointerMoved) { ScreenPoint = PointerCapture.ScreenOf(list, e) };
        args.Context.RowIndex = IndexAt(list, at.Y);
        args.Context.LeafLocalPoint = at;
        list.RaiseIntent(args);
    }

    private static void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl list) return;
        Unhook(list);
        PointerCapture.Release(list);
        list.RaiseIntent(IntentKind.PointerReleased, PointerCapture.ScreenOf(list, e));
    }

    private static void OnListLostCapture(object sender, MouseEventArgs e)
    {
        if (sender is not ItemsControl list || PointerCapture.IsReleasing(list)) return;
        Unhook(list);
        list.RaiseIntent(IntentKind.PointerCaptureLost);
    }

    private static void Unhook(ItemsControl list)
    {
        list.MouseMove -= OnListMouseMove;
        list.MouseLeftButtonUp -= OnListMouseUp;
        list.LostMouseCapture -= OnListLostCapture;
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

    private static ItemsControl? Ancestor(DependencyObject o)
    {
        for (var p = VisualTreeHelper.GetParent(o); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ItemsControl list) return list;
        return null;
    }
}
