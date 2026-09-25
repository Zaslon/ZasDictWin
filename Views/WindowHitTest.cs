using System.Windows;
using System.Windows.Media;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 枠を並べる窓（本体・独立ウィンドウ）の当たり判定。窓ごとに Visual Tree が分かれているので、
/// 画面上の位置から枠を引くのは各窓が自分の木の中で行い、どの窓を先に見るかは AppRoot が決める。
/// </summary>
internal static class WindowHitTest
{
    /// <summary>画面上の位置（デバイスピクセル）がこの窓の中か。最小化中・非表示・まだ描かれていない窓は偽。</summary>
    public static bool Contains(Window window, Point screen)
    {
        if (!window.IsVisible || window.WindowState == WindowState.Minimized) return false;
        if (!TryLocal(window, screen, out var local)) return false;
        return local.X >= 0 && local.Y >= 0 && local.X <= window.ActualWidth && local.Y <= window.ActualHeight;
    }

    /// <summary>画面上の位置の下にある枠。窓には乗っているが枠の外（ヘッダ・フッタなど）なら偽で leafId は -1。</summary>
    public static bool TryHitLeaf(Window window, Point screen, out int leafId, out Size leafSize, out Point leafLocal)
    {
        leafId = -1;
        leafSize = default;
        leafLocal = default;
        if (!TryLocal(window, screen, out var local)) return false;

        var hit = window.InputHitTest(local) as DependencyObject;
        while (hit is not null)
        {
            if (hit is DockGroupPanel { DataContext: DockLeaf leaf } panel)
            {
                leafId = leaf.Id;
                leafSize = new Size(panel.ActualWidth, panel.ActualHeight);
                leafLocal = panel.PointFromScreen(screen);
                return true;
            }
            // 当たるのは描いている要素なので、視覚ツリーだけ遡れば枠に届く。
            hit = hit is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }
        return false;
    }

    private static bool TryLocal(Window window, Point screen, out Point local)
    {
        try
        {
            local = window.PointFromScreen(screen);
            return true;
        }
        catch (InvalidOperationException)
        {
            // まだ描かれていない（HWND を持たない）窓は当たり判定に入れない。
            local = default;
            return false;
        }
    }
}
