using System.Windows;
using ZasDictWin.Mediator;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;
using E = ZasDictWin.Mediator.DragEffect;

namespace ZasDictWin.Tests;

public class DragTransitionsTests
{
    private static readonly IUiHost Host = new StubHost(HostRole.Shell, new Rect(0, 0, 1000, 800));
    private static readonly Size Big = new(600, 400);

    private static DragEnvironment Env(
        HitLeaf? hit = null, Size? grip = null, Point? local = null, int[]? siblings = null,
        int leafCount = 2, int sourceItems = 0)
        => new(hit, grip ?? Big, local ?? default, siblings ?? Array.Empty<int>(), leafCount,
            LayoutRules.MaxLeaves, DockSplit.MinLeafSize, 48, 4, 4, sourceItems);

    private static Intent I(IntentKind kind, object? payload = null, Action<IntentContext>? fill = null)
    {
        var ctx = new IntentContext();
        fill?.Invoke(ctx);
        return new Intent(kind, payload, ctx, null);
    }

    private static HitLeaf Hit(int leaf, Point local, Size? size = null) => new(Host, leaf, size ?? Big, local);

    private static HitLeaf NoLeaf => new(Host, -1, default, default);

    private static List<E> Fx(DragStep step) => step.Effect.Flat().ToList();

    private static void AssertFx(DragStep step, params E[] expected) => Assert.Equal(expected, Fx(step));

    // ---- Area ---------------------------------------------------------------

    private static DragContext AreaArmed(AreaCorner corner = AreaCorner.BottomRight, Point? origin = null)
        => DragContext.Idle with
        {
            Phase = DragPhase.AreaGripArmed, GripLeafId = 1, Corner = corner, Origin = origin ?? new Point(600, 400),
        };

    private static readonly SplitPreview Half = new(DockAxis.Columns, 0.5, true);

    [Fact]
    public void D1_AreaGripPressed_Arms()
    {
        var step = DragTransitions.Step(DragContext.Idle, I(IntentKind.AreaGripPressed, fill: c =>
        {
            c.LeafId = 1; c.Corner = AreaCorner.BottomRight; c.LeafLocalPoint = new Point(600, 400);
        }), Env());

        Assert.Equal(DragPhase.AreaGripArmed, step.Next.Phase);
        Assert.Equal(1, step.Next.GripLeafId);
        Assert.Equal(new Point(600, 400), step.Next.Origin);
        AssertFx(step);
    }

    [Fact]
    public void D2_AreaMoveBelowThreshold_Stays()
    {
        var step = DragTransitions.Step(AreaArmed(), I(IntentKind.PointerMoved), Env(local: new Point(602, 397)));
        Assert.Equal(DragPhase.AreaGripArmed, step.Next.Phase);
        AssertFx(step);
    }

    [Fact]
    public void D3_AreaMoveInside_ShowsSplitPreview()
    {
        var step = DragTransitions.Step(AreaArmed(), I(IntentKind.PointerMoved), Env(local: new Point(300, 390)));
        Assert.Equal(DragPhase.AreaSplitPreview, step.Next.Phase);
        Assert.Equal(Half, step.Next.Preview);
        AssertFx(step, new E.ClearJoinTargets(), new E.ShowSplitPreview(1, Half));

        var fromBlocked = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaSplitBlocked },
            I(IntentKind.PointerMoved), Env(local: new Point(300, 390)));
        Assert.Equal(DragPhase.AreaSplitPreview, fromBlocked.Next.Phase);
    }

    [Fact]
    public void D3_CornerDecidesSideAndAxis()
    {
        // 左上の角を下へ引く → 上下割り、新しい枠は上（First 側）。
        var step = DragTransitions.Step(AreaArmed(AreaCorner.TopLeft, new Point(0, 0)), I(IntentKind.PointerMoved),
            Env(local: new Point(5, 200)));
        Assert.Equal(new SplitPreview(DockAxis.Rows, 0.5, false), step.Next.Preview);
    }

    [Fact]
    public void D4_AreaMoveInside_TooNarrow_Blocks()
    {
        var step = DragTransitions.Step(AreaArmed(origin: new Point(250, 400)), I(IntentKind.PointerMoved),
            Env(grip: new Size(250, 400), local: new Point(120, 390)));
        Assert.Equal(DragPhase.AreaSplitBlocked, step.Next.Phase);
        AssertFx(step, new E.ClearJoinTargets(), new E.ClearSplitPreview(1));
    }

    [Fact]
    public void D5_AreaMoveOntoSibling_ShowsJoinTargets()
    {
        var step = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaSplitPreview, Preview = Half },
            I(IntentKind.PointerMoved), Env(hit: Hit(2, new Point(10, 10)), local: new Point(700, 200), siblings: new[] { 2, 3 }));
        Assert.Equal(DragPhase.AreaJoinPreview, step.Next.Phase);
        AssertFx(step, new E.ClearSplitPreview(1), new E.ShowJoinTargets(new[] { 2, 3 }));
    }

    [Fact]
    public void D6_AreaMoveOutsideSiblings_Rearms()
    {
        var step = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaJoinPreview, JoinTargets = new[] { 2 } },
            I(IntentKind.PointerMoved), Env(hit: Hit(9, new Point(10, 10)), local: new Point(700, 200), siblings: new[] { 2 }));
        Assert.Equal(DragPhase.AreaGripArmed, step.Next.Phase);
        AssertFx(step, new E.ClearSplitPreview(1), new E.ClearJoinTargets());

        // 一度動き出した後は、元の位置の近くへ戻っても閾値で止めない。
        var back = DragTransitions.Step(step.Next, I(IntentKind.PointerMoved), Env(local: new Point(599, 399)));
        Assert.NotEqual(DragPhase.AreaGripArmed, back.Next.Phase);
    }

    [Fact]
    public void D7_AreaReleaseWithPreview_CommitsSplit()
    {
        var step = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaSplitPreview, Preview = Half },
            I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.ClearSplitPreview(1), new E.CommitSplit(1, Half), new E.Persist());
    }

    [Fact]
    public void D8_AreaReleaseBlockedOrArmed_DoesNothing()
    {
        var blocked = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaSplitBlocked }, I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, blocked.Next.Phase);
        AssertFx(blocked);

        var armed = DragTransitions.Step(AreaArmed(), I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, armed.Next.Phase);
        AssertFx(armed);
    }

    [Fact]
    public void D9_AreaReleaseOnJoin_CommitsJoin()
    {
        var step = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaJoinPreview, JoinTargets = new[] { 2 } },
            I(IntentKind.PointerReleased), Env());
        AssertFx(step, new E.ClearJoinTargets(), new E.CommitJoin(1), new E.Persist());
    }

    [Theory]
    [InlineData(IntentKind.CancelRequested)]
    [InlineData(IntentKind.PointerCaptureLost)]
    public void D10_AreaCancel_ClearsEverything(IntentKind kind)
    {
        var step = DragTransitions.Step(AreaArmed() with { Phase = DragPhase.AreaSplitPreview, Preview = Half }, I(kind), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.ClearSplitPreview(1), new E.ClearJoinTargets());
    }

    // ---- Tab ----------------------------------------------------------------

    private static DragContext TabArmed(int sourceItems = 2) => DragContext.Idle with
    {
        Phase = DragPhase.TabGripArmed, TabKind = "X", SourceLeafId = 1, SourceItemCount = sourceItems,
    };

    private static readonly Point Moved = new(20, 0);
    private static readonly Point Center = new(300, 200);
    private static readonly Point LeftEdge = new(10, 200);
    private static readonly SplitPreview LeftHalf = new(DockAxis.Columns, 0.5, false);

    [Fact]
    public void D11_TabGripPressed_Arms()
    {
        var step = DragTransitions.Step(DragContext.Idle, I(IntentKind.TabGripPressed, fill: c =>
        {
            c.TabKind = "X"; c.LeafId = 1;
        }), Env(sourceItems: 3));
        Assert.Equal(DragPhase.TabGripArmed, step.Next.Phase);
        Assert.Equal(1, step.Next.SourceLeafId);
        Assert.Equal(3, step.Next.SourceItemCount);
        AssertFx(step);
    }

    [Fact]
    public void D12_TabMoveBelowThreshold_Stays()
    {
        var step = DragTransitions.Step(TabArmed(), I(IntentKind.PointerMoved), Env(hit: Hit(2, Center), local: new Point(3, 3)));
        Assert.Equal(DragPhase.TabGripArmed, step.Next.Phase);
        AssertFx(step);
    }

    [Fact]
    public void D13_TabOverOtherLeaf_ShowsDropTarget()
    {
        var step = DragTransitions.Step(TabArmed(), I(IntentKind.PointerMoved), Env(hit: Hit(2, Center), local: Moved));
        Assert.Equal(DragPhase.TabOverLeaf, step.Next.Phase);
        Assert.Equal(2, step.Next.TargetLeafId);
        AssertFx(step, new E.ShowDropTarget(2));

        var moved = DragTransitions.Step(step.Next with { TargetLeafId = 3 }, I(IntentKind.PointerMoved), Env(hit: Hit(2, Center), local: Moved));
        AssertFx(moved, new E.ClearDropTarget(3), new E.ShowDropTarget(2));

        // 同じ枠の上を動くだけなら何も出し直さない。
        var same = DragTransitions.Step(step.Next, I(IntentKind.PointerMoved), Env(hit: Hit(2, Center), local: Moved));
        AssertFx(same);
    }

    [Fact]
    public void D13_TabOverOtherLeafEdge_MergesWithoutPreview()
    {
        var step = DragTransitions.Step(TabArmed(), I(IntentKind.PointerMoved), Env(hit: Hit(2, LeftEdge), local: Moved));
        Assert.Equal(DragPhase.TabOverLeaf, step.Next.Phase);
    }

    [Fact]
    public void D13_TabOverSourceEdge_SingleTab_MergesWithoutPreview()
    {
        var step = DragTransitions.Step(TabArmed(sourceItems: 1), I(IntentKind.PointerMoved), Env(hit: Hit(1, LeftEdge), local: Moved));
        Assert.Equal(DragPhase.TabOverLeaf, step.Next.Phase);
        AssertFx(step, new E.ShowDropTarget(1));
    }

    [Fact]
    public void D14_TabOverSourceEdge_ShowsSplitPreview()
    {
        var step = DragTransitions.Step(TabArmed(), I(IntentKind.PointerMoved), Env(hit: Hit(1, LeftEdge), local: Moved));
        Assert.Equal(DragPhase.TabOverSourceEdge, step.Next.Phase);
        Assert.Equal(LeftHalf, step.Next.Preview);
        AssertFx(step, new E.ShowSplitPreview(1, LeftHalf));

        var fromOutside = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOutside, OutsideDip = new Point(1, 1) },
            I(IntentKind.PointerMoved), Env(hit: Hit(1, LeftEdge), local: Moved));
        AssertFx(fromOutside, new E.ShowSplitPreview(1, LeftHalf), new E.HideGhost());
    }

    [Fact]
    public void D15_TabOverWindowButNoLeaf_ClearsHover()
    {
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverLeaf, TargetLeafId = 2 },
            I(IntentKind.PointerMoved), Env(hit: NoLeaf, local: Moved));
        Assert.Equal(DragPhase.TabOverNoLeaf, step.Next.Phase);
        AssertFx(step, new E.ClearDropTarget(2));
    }

    [Fact]
    public void D16_TabOutsideAllWindows_ShowsGhost()
    {
        var at = new Point(100, 120);
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverSourceEdge, TargetLeafId = 1, Preview = LeftHalf },
            I(IntentKind.PointerMoved, at), Env(hit: null, local: Moved));
        Assert.Equal(DragPhase.TabOutside, step.Next.Phase);
        Assert.Equal(at, step.Next.OutsideDip);
        AssertFx(step, new E.ClearSplitPreview(1), new E.ShowGhost("X", at));
    }

    [Fact]
    public void D17_TabReleaseOnSourceLeaf_OnlyClears()
    {
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverLeaf, TargetLeafId = 1 }, I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.ClearDropTarget(1));
    }

    [Fact]
    public void D18_TabReleaseOnOtherLeaf_Moves()
    {
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverLeaf, TargetLeafId = 2 }, I(IntentKind.PointerReleased), Env());
        AssertFx(step, new E.ClearDropTarget(2), new E.CommitMove("X", 2), new E.Persist());
    }

    [Fact]
    public void D19_TabReleaseOnSourceEdge_SplitsThenMoves()
    {
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverSourceEdge, TargetLeafId = 1, Preview = LeftHalf },
            I(IntentKind.PointerReleased), Env(leafCount: LayoutRules.MaxLeaves - 1));
        AssertFx(step, new E.ClearSplitPreview(1), new E.CommitSplitThenMove("X", 1, LeftHalf), new E.Persist());
    }

    [Fact]
    public void D20_TabReleaseOnSourceEdge_AtLimit_FallsBackToMerge()
    {
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverSourceEdge, TargetLeafId = 1, Preview = LeftHalf },
            I(IntentKind.PointerReleased), Env(leafCount: LayoutRules.MaxLeaves));
        AssertFx(step, new E.ClearSplitPreview(1), new E.CommitMove("X", 1), new E.Persist());
    }

    [Fact]
    public void D21_TabReleaseOffLeafOrArmed_DoesNothing()
    {
        AssertFx(DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverNoLeaf }, I(IntentKind.PointerReleased), Env()));
        var armed = DragTransitions.Step(TabArmed(), I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, armed.Next.Phase);
        AssertFx(armed);
    }

    [Fact]
    public void D22_TabReleaseOutside_Floats()
    {
        var at = new Point(100, 120);
        var step = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOutside, OutsideDip = at }, I(IntentKind.PointerReleased), Env());
        AssertFx(step, new E.HideGhost(), new E.CommitFloat("X", at), new E.Persist());
    }

    [Theory]
    [InlineData(IntentKind.CancelRequested)]
    [InlineData(IntentKind.PointerCaptureLost)]
    public void D23_TabCancel_ClearsHoverAndGhost(IntentKind kind)
    {
        var overLeaf = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOverLeaf, TargetLeafId = 2 }, I(kind), Env());
        Assert.Equal(DragPhase.Idle, overLeaf.Next.Phase);
        AssertFx(overLeaf, new E.ClearDropTarget(2));

        var outside = DragTransitions.Step(TabArmed() with { Phase = DragPhase.TabOutside }, I(kind), Env());
        AssertFx(outside, new E.HideGhost());
    }

    // ---- Tab の並べ替え --------------------------------------------------------

    private static DragContext TabArmedAt(int index, int sourceItems = 3)
        => TabArmed(sourceItems) with { TabOriginIndex = index, TabCurrentIndex = index };

    private static HitLeaf OnStrip(int leaf, int slot) => new(Host, leaf, Big, new Point(100, 10), 36, slot);

    [Fact]
    public void D24_TabGripPressed_RemembersSourceIndex()
    {
        var step = DragTransitions.Step(DragContext.Idle, I(IntentKind.TabGripPressed, fill: c =>
        {
            c.TabKind = "X"; c.LeafId = 1;
        }), Env(sourceItems: 3) with { SourceTabIndex = 2 });
        Assert.Equal(2, step.Next.TabOriginIndex);
        Assert.Equal(2, step.Next.TabCurrentIndex);
    }

    [Fact]
    public void D25_TabOverOwnStrip_ReordersLive()
    {
        var step = DragTransitions.Step(TabArmedAt(0), I(IntentKind.PointerMoved), Env(hit: OnStrip(1, 2), local: Moved));
        Assert.Equal(DragPhase.TabReordering, step.Next.Phase);
        Assert.Equal(1, step.Next.TabCurrentIndex);
        AssertFx(step, new E.CommitTabOrder("X", 1));

        // 動かした直後に自分の上（左右どちらの半分でも）に来ても、元へ跳ね返らない。
        AssertFx(DragTransitions.Step(step.Next, I(IntentKind.PointerMoved), Env(hit: OnStrip(1, 1), local: Moved)));
        AssertFx(DragTransitions.Step(step.Next, I(IntentKind.PointerMoved), Env(hit: OnStrip(1, 2), local: Moved)));

        var back = DragTransitions.Step(step.Next, I(IntentKind.PointerMoved), Env(hit: OnStrip(1, 0), local: Moved));
        Assert.Equal(0, back.Next.TabCurrentIndex);
        AssertFx(back, new E.CommitTabOrder("X", 0));
    }

    [Fact]
    public void D25_TabOverOwnStripFromSplitPreview_ClearsPreview()
    {
        var current = TabArmedAt(0) with { Phase = DragPhase.TabOverSourceEdge, TargetLeafId = 1, Preview = LeftHalf };
        var step = DragTransitions.Step(current, I(IntentKind.PointerMoved), Env(hit: OnStrip(1, 3), local: Moved));
        AssertFx(step, new E.ClearSplitPreview(1), new E.CommitTabOrder("X", 2));
    }

    [Fact]
    public void D25_TabOverOtherLeafStrip_StillMoves()
    {
        var step = DragTransitions.Step(TabArmedAt(0), I(IntentKind.PointerMoved), Env(hit: OnStrip(2, 0), local: Moved));
        Assert.Equal(DragPhase.TabOverLeaf, step.Next.Phase);
        AssertFx(step, new E.ShowDropTarget(2));
    }

    [Fact]
    public void D26_TabReleaseAfterReorder_Persists()
    {
        var reordered = TabArmedAt(0) with { Phase = DragPhase.TabReordering, TargetLeafId = 1, TabCurrentIndex = 2 };
        AssertFx(DragTransitions.Step(reordered, I(IntentKind.PointerReleased), Env()), new E.Persist());

        // 並べ替えてから同じ枠の中身の上へ外して離しても、並びは書き戻す。
        var overLeaf = reordered with { Phase = DragPhase.TabOverLeaf };
        AssertFx(DragTransitions.Step(overLeaf, I(IntentKind.PointerReleased), Env()), new E.ClearDropTarget(1), new E.Persist());

        // 元の位置へ戻してから離したなら書き戻すものは無い。
        var unchanged = reordered with { TabCurrentIndex = 0 };
        AssertFx(DragTransitions.Step(unchanged, I(IntentKind.PointerReleased), Env()));
    }

    [Theory]
    [InlineData(IntentKind.CancelRequested)]
    [InlineData(IntentKind.PointerCaptureLost)]
    public void D27_TabCancelAfterReorder_RestoresOrder(IntentKind kind)
    {
        var reordered = TabArmedAt(0) with { Phase = DragPhase.TabReordering, TargetLeafId = 1, TabCurrentIndex = 2 };
        var step = DragTransitions.Step(reordered, I(kind), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.CommitTabOrder("X", 0));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 3, 2)]
    [InlineData(2, 0, 0)]
    [InlineData(2, 3, 2)]
    public void ReorderTarget_AccountsForDraggedTab(int current, int slot, int expected)
        => Assert.Equal(expected, DragTransitions.ReorderTarget(current, slot));

    [Fact]
    public void EdgeSplit_TopEdgeMeasuredBelowTabStrip()
    {
        var top = new SplitPreview(DockAxis.Rows, 0.5, false);
        Assert.Equal(top, DragTransitions.EdgeSplit(Big, new Point(300, 80), 48, DockSplit.MinLeafSize, 36));
        Assert.Null(DragTransitions.EdgeSplit(Big, new Point(300, 84), 48, DockSplit.MinLeafSize, 36));
    }

    // ---- Row ----------------------------------------------------------------

    private static DragContext RowArmed() => DragContext.Idle with
    {
        Phase = DragPhase.RowGripArmed, RowOriginIndex = 3, RowCurrentIndex = 3, Origin = new Point(10, 60),
    };

    [Fact]
    public void D24_RowGripPressed_Arms()
    {
        var step = DragTransitions.Step(DragContext.Idle, I(IntentKind.RowGripPressed, fill: c =>
        {
            c.RowIndex = 3; c.LeafLocalPoint = new Point(10, 60);
        }), Env());
        Assert.Equal(DragPhase.RowGripArmed, step.Next.Phase);
        Assert.Equal(3, step.Next.RowOriginIndex);
        AssertFx(step);
    }

    [Fact]
    public void D25_RowMoveBeyondThreshold_StartsReordering()
    {
        var below = DragTransitions.Step(RowArmed(), I(IntentKind.PointerMoved, fill: c => c.RowIndex = 3), Env(local: new Point(11, 62)));
        Assert.Equal(DragPhase.RowGripArmed, below.Next.Phase);

        var start = DragTransitions.Step(RowArmed(), I(IntentKind.PointerMoved, fill: c => c.RowIndex = 3), Env(local: new Point(10, 70)));
        Assert.Equal(DragPhase.RowReordering, start.Next.Phase);
        AssertFx(start, new E.SetRowDragging(3, true));

        var startAndMove = DragTransitions.Step(RowArmed(), I(IntentKind.PointerMoved, fill: c => c.RowIndex = 1), Env(local: new Point(10, 20)));
        AssertFx(startAndMove, new E.SetRowDragging(3, true), new E.CommitRowOrder(3, 1));
        Assert.Equal(1, startAndMove.Next.RowCurrentIndex);
    }

    [Fact]
    public void D26_RowMoveToOtherIndex_Reorders()
    {
        var reordering = RowArmed() with { Phase = DragPhase.RowReordering, RowCurrentIndex = 1 };
        var step = DragTransitions.Step(reordering, I(IntentKind.PointerMoved, fill: c => c.RowIndex = 0), Env());
        AssertFx(step, new E.CommitRowOrder(1, 0));
        Assert.Equal(0, step.Next.RowCurrentIndex);

        AssertFx(DragTransitions.Step(reordering, I(IntentKind.PointerMoved, fill: c => c.RowIndex = 1), Env()));
    }

    [Fact]
    public void D27_RowRelease_KeepsOrder()
    {
        var step = DragTransitions.Step(RowArmed() with { Phase = DragPhase.RowReordering, RowCurrentIndex = 1 }, I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.SetRowDragging(1, false));
    }

    [Theory]
    [InlineData(IntentKind.CancelRequested)]
    [InlineData(IntentKind.PointerCaptureLost)]
    public void D28_RowCancel_RestoresOrder(IntentKind kind)
    {
        var step = DragTransitions.Step(RowArmed() with { Phase = DragPhase.RowReordering, RowCurrentIndex = 1 }, I(kind), Env());
        AssertFx(step, new E.RestoreRowOrder(3), new E.SetRowDragging(3, false));
    }

    [Theory]
    [InlineData(IntentKind.PointerReleased)]
    [InlineData(IntentKind.CancelRequested)]
    public void D29_RowArmedReleaseOrCancel_DoesNothing(IntentKind kind)
    {
        var step = DragTransitions.Step(RowArmed(), I(kind), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step);
    }

    // ---- Split --------------------------------------------------------------

    private static DragContext SplitDragging() => DragContext.Idle with { Phase = DragPhase.SplitGripDragging, SplitId = 5 };

    [Fact]
    public void D30_SplitGripPressed_StartsDragging()
    {
        var step = DragTransitions.Step(DragContext.Idle, I(IntentKind.SplitGripPressed, fill: c => c.SplitId = 5), Env());
        Assert.Equal(DragPhase.SplitGripDragging, step.Next.Phase);
        Assert.Equal(5, step.Next.SplitId);
        AssertFx(step);
    }

    [Fact]
    public void D31_SplitMove_Resizes()
    {
        var step = DragTransitions.Step(SplitDragging(), I(IntentKind.PointerMoved, fill: c => { c.DragChange = 10; c.DragTotal = 800; }), Env());
        Assert.Equal(DragPhase.SplitGripDragging, step.Next.Phase);
        AssertFx(step, new E.CommitResize(5, 10, 800));
    }

    [Fact]
    public void D32_SplitRelease_Persists()
    {
        var step = DragTransitions.Step(SplitDragging(), I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.Persist());
    }

    [Theory]
    [InlineData(IntentKind.CancelRequested)]
    [InlineData(IntentKind.PointerCaptureLost)]
    public void D33_SplitCancel_Persists(IntentKind kind)
    {
        var step = DragTransitions.Step(SplitDragging(), I(kind), Env());
        Assert.Equal(DragPhase.Idle, step.Next.Phase);
        AssertFx(step, new E.Persist());
    }

    // ---- 異常系 --------------------------------------------------------------

    [Theory]
    [InlineData(IntentKind.PointerMoved)]
    [InlineData(IntentKind.PointerReleased)]
    [InlineData(IntentKind.PointerCaptureLost)]
    [InlineData(IntentKind.CancelRequested)]
    public void Idle_IgnoresPointerIntents(IntentKind kind)
    {
        var step = DragTransitions.Step(DragContext.Idle, I(kind), Env());
        Assert.Equal(DragContext.Idle, step.Next);
        AssertFx(step);
    }

    [Theory]
    [InlineData(IntentKind.AreaGripPressed)]
    [InlineData(IntentKind.TabGripPressed)]
    [InlineData(IntentKind.RowGripPressed)]
    [InlineData(IntentKind.SplitGripPressed)]
    public void NonIdle_DoesNotStartAnotherDrag(IntentKind kind)
    {
        var current = TabArmed() with { Phase = DragPhase.TabOverLeaf, TargetLeafId = 2 };
        var step = DragTransitions.Step(current, I(kind, fill: c =>
        {
            c.LeafId = 9; c.Corner = AreaCorner.TopLeft; c.TabKind = "Y"; c.RowIndex = 0; c.SplitId = 1;
        }), Env());
        Assert.Equal(current, step.Next);
        AssertFx(step);
    }

    // ---- 境界値 --------------------------------------------------------------

    [Fact]
    public void Threshold_ExactlyEqual_StartsDrag()
    {
        var step = DragTransitions.Step(TabArmed(), I(IntentKind.PointerMoved), Env(hit: Hit(2, Center), local: new Point(4, 0)));
        Assert.Equal(DragPhase.TabOverLeaf, step.Next.Phase);
    }

    [Fact]
    public void CornerSplit_ExactlyTwiceMinSize_Splits()
    {
        var size = new Size(DockSplit.MinLeafSize * 2, 400);
        Assert.NotNull(DragTransitions.CornerSplit(AreaCorner.TopLeft, new Point(0, 0), new Point(100, 5), size, DockSplit.MinLeafSize));
        var narrower = new Size(DockSplit.MinLeafSize * 2 - 1, 400);
        Assert.Null(DragTransitions.CornerSplit(AreaCorner.TopLeft, new Point(0, 0), new Point(100, 5), narrower, DockSplit.MinLeafSize));
    }

    [Fact]
    public void CornerSplit_ClampsRatioToMinSize()
    {
        var preview = DragTransitions.CornerSplit(AreaCorner.TopRight, new Point(600, 0), new Point(10, 5), Big, DockSplit.MinLeafSize);
        Assert.Equal(DockSplit.MinLeafSize / 600, preview!.Ratio, 6);
        Assert.True(preview.NewIsSecond);
    }

    [Fact]
    public void EdgeSplit_ExactlyEdgeZone_IsNotCandidate()
    {
        Assert.Null(DragTransitions.EdgeSplit(Big, new Point(48, 200), 48, DockSplit.MinLeafSize));
        Assert.Equal(LeftHalf, DragTransitions.EdgeSplit(Big, new Point(47.9, 200), 48, DockSplit.MinLeafSize));
    }

    [Fact]
    public void EdgeSplit_PicksNearestEdge_AndSkipsTooSmallAxis()
    {
        Assert.Equal(new SplitPreview(DockAxis.Rows, 0.5, true), DragTransitions.EdgeSplit(Big, new Point(40, 395), 48, DockSplit.MinLeafSize));
        // 高さが足りない枠では上下の辺を候補にしない。
        Assert.Equal(LeftHalf, DragTransitions.EdgeSplit(new Size(600, 200), new Point(40, 195), 48, DockSplit.MinLeafSize));
    }

    // ---- 通しの流れ -----------------------------------------------------------

    [Fact]
    public void Flow_AreaSplit_D1_D3_D7()
    {
        var s = DragTransitions.Step(DragContext.Idle, I(IntentKind.AreaGripPressed, fill: c =>
        {
            c.LeafId = 1; c.Corner = AreaCorner.BottomRight; c.LeafLocalPoint = new Point(600, 400);
        }), Env());
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved), Env(local: new Point(300, 390)));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerReleased), Env());
        Assert.Contains(new E.CommitSplit(1, Half), Fx(s));
    }

    [Fact]
    public void Flow_TabToSourceEdge_D11_D14_D19()
    {
        var s = DragTransitions.Step(DragContext.Idle, I(IntentKind.TabGripPressed, fill: c => { c.TabKind = "X"; c.LeafId = 1; }), Env(sourceItems: 2));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved), Env(hit: Hit(1, LeftEdge), local: Moved));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerReleased), Env());
        Assert.Contains(new E.CommitSplitThenMove("X", 1, LeftHalf), Fx(s));
    }

    [Fact]
    public void Flow_TabToOtherLeaf_D11_D13_D18()
    {
        var s = DragTransitions.Step(DragContext.Idle, I(IntentKind.TabGripPressed, fill: c => { c.TabKind = "X"; c.LeafId = 1; }), Env(sourceItems: 2));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved), Env(hit: Hit(2, Center), local: Moved));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerReleased), Env());
        Assert.Contains(new E.CommitMove("X", 2), Fx(s));
    }

    [Fact]
    public void Flow_TabOutside_D11_D16_D22()
    {
        var at = new Point(50, 60);
        var s = DragTransitions.Step(DragContext.Idle, I(IntentKind.TabGripPressed, fill: c => { c.TabKind = "X"; c.LeafId = 1; }), Env(sourceItems: 2));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved, at), Env(hit: null, local: Moved));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerReleased), Env());
        Assert.Contains(new E.CommitFloat("X", at), Fx(s));
    }

    [Fact]
    public void Flow_Row_D24_D25_D26_D27()
    {
        var s = DragTransitions.Step(DragContext.Idle, I(IntentKind.RowGripPressed, fill: c => { c.RowIndex = 2; c.LeafLocalPoint = new Point(0, 50); }), Env());
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved, fill: c => c.RowIndex = 2), Env(local: new Point(0, 60)));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved, fill: c => c.RowIndex = 0), Env());
        Assert.Contains(new E.CommitRowOrder(2, 0), Fx(s));
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerReleased), Env());
        Assert.Equal(DragPhase.Idle, s.Next.Phase);
    }

    [Fact]
    public void Flow_Split_D30_D31_D32()
    {
        var s = DragTransitions.Step(DragContext.Idle, I(IntentKind.SplitGripPressed, fill: c => c.SplitId = 5), Env());
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerMoved, fill: c => { c.DragChange = 3; c.DragTotal = 900; }), Env());
        s = DragTransitions.Step(s.Next, I(IntentKind.PointerReleased), Env());
        AssertFx(s, new E.Persist());
    }
}
