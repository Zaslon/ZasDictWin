using System.Windows;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

/// <summary>Window を作らずに当たり判定の結果だけを決め打ちで返す窓。</summary>
internal sealed class StubHost : IUiHost
{
    public StubHost(HostRole role, Rect bounds, int leafId = -1, bool active = false, Guid? id = null)
    {
        Role = role;
        BoundsDip = bounds;
        LeafId = leafId;
        IsActiveHost = active;
        HostId = id ?? Guid.NewGuid();
    }

    public Guid HostId { get; }
    public HostRole Role { get; }
    public DockNode? DockRoot => null;
    public Rect BoundsDip { get; }
    public bool IsActiveHost { get; set; }
    public int LeafId { get; set; }
    public Size LeafSize { get; set; } = new(400, 300);
    public int Closed { get; private set; }
    public int Focused { get; private set; }

    public void CloseFromRoot() => Closed++;
    public void FocusFromRoot() => Focused++;

    public bool TryHitLeaf(Point screen, out int leafId, out Size leafSize, out Point leafLocal)
    {
        leafId = -1;
        leafSize = default;
        leafLocal = default;
        if (!ContainsScreenPoint(screen) || LeafId < 0) return false;
        leafId = LeafId;
        leafSize = LeafSize;
        leafLocal = new Point(screen.X - BoundsDip.X, screen.Y - BoundsDip.Y);
        return true;
    }

    public bool ContainsScreenPoint(Point screen) => BoundsDip.Contains(screen);
}
