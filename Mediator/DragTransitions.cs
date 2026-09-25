using System.Windows;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

/// <summary>ドラッグ中に持ち回す状態。DragPhase と、その相で意味を持つ値だけを入れる。</summary>
public readonly record struct DragContext(
    DragPhase Phase,
    // Area（枠の四隅）
    int? GripLeafId, AreaCorner? Corner, Point Origin,
    SplitPreview? Preview, IReadOnlyList<int>? JoinTargets,
    // Tab（タブの運び）
    string? TabKind, int? SourceLeafId, int SourceItemCount, int? TargetLeafId, Point? OutsideDip,
    // Split（境目のつまみ）
    int? SplitId,
    // Row（一覧の行）
    int RowOriginIndex, int RowCurrentIndex)
{
    public static DragContext Idle { get; } = new(
        DragPhase.Idle, null, null, default, null, null, null, null, 0, null, null, null, -1, -1);
}

/// <summary>
/// Step が参照する周辺情報。View から取れる事実だけを渡し、Step 自身は WPF の入力 API に触らない
/// （Point / Size は値型なのでテストからそのまま作れる）。
/// </summary>
public readonly record struct DragEnvironment(
    HitLeaf? Hit,
    /// <summary>掴んでいる枠の実寸（Area ドラッグで分割の可否と比率を決めるのに使う）。</summary>
    Size GripLeafSize,
    /// <summary>掴んでいる要素を基準にしたポインタ位置。Area は枠、Tab は見出し、Row は一覧が基準で、
    /// 動き出しの閾値はどれもこの座標の移動量で測る。</summary>
    Point GripLeafLocal,
    /// <summary>掴んでいる枠の兄弟にぶら下がる葉（結合で消える側の候補）。</summary>
    IReadOnlyList<int> SiblingLeafIds,
    int LeafCount, int MaxLeaves,
    double MinLeafSize, double EdgeZone,
    double DragThresholdX, double DragThresholdY,
    /// <summary>タブを掴んだ時点で運び元の枠に並んでいたタブの数。</summary>
    int SourceItemCount = 0);

public readonly record struct DragStep(DragContext Next, DragEffect Effect);

/// <summary>
/// ドラッグの遷移表。純粋関数なので全遷移を単体テストで検査できる。
/// 枠の四隅（Area）・タブの運び（Tab）・一覧の行（Row）・境目のつまみ（Split）の 4 種を 1 つの状態で持つので、
/// 別種のドラッグが同時に成立することは構造的に無い（掴む Intent は Idle でしか受けない）。
/// </summary>
public static class DragTransitions
{
    public static DragStep Step(DragContext current, Intent intent, DragEnvironment env)
    {
        return current.Phase switch
        {
            DragPhase.Idle => FromIdle(current, intent, env),
            DragPhase.AreaGripArmed or DragPhase.AreaSplitPreview or DragPhase.AreaSplitBlocked
                or DragPhase.AreaJoinPreview => Area(current, intent, env),
            DragPhase.TabGripArmed or DragPhase.TabOverLeaf or DragPhase.TabOverSourceEdge
                or DragPhase.TabOverNoLeaf or DragPhase.TabOutside => Tab(current, intent, env),
            DragPhase.RowGripArmed or DragPhase.RowReordering => Row(current, intent, env),
            DragPhase.SplitGripDragging => Split(current, intent),
            _ => Stay(current),
        };
    }

    /// <summary>枠のどの辺に寄せているか。真ん中なら null（タブとして合流）。
    /// 4 辺を距離で比べて最も近い辺を選び、Ratio は常に 0.5。</summary>
    public static SplitPreview? EdgeSplit(Size leafSize, Point local, double edgeZone, double minLeafSize)
    {
        var best = edgeZone;
        SplitPreview? preview = null;

        void Consider(double distance, DockAxis axis, bool newIsSecond, double total)
        {
            if (total < minLeafSize * 2 || distance >= best) return;
            best = distance;
            preview = new SplitPreview(axis, 0.5, newIsSecond);
        }

        Consider(local.X, DockAxis.Columns, false, leafSize.Width);                   // 左端 → 左に新しい枠
        Consider(leafSize.Width - local.X, DockAxis.Columns, true, leafSize.Width);   // 右端 → 右に新しい枠
        Consider(local.Y, DockAxis.Rows, false, leafSize.Height);                     // 上端 → 上に新しい枠
        Consider(leafSize.Height - local.Y, DockAxis.Rows, true, leafSize.Height);    // 下端 → 下に新しい枠

        return preview;
    }

    /// <summary>角を内へ引いたときの分割。引いた向きで軸を決め、掴んだ角の側に新しい枠を作る。
    /// Ratio は掴んだ位置から決まる。割った先が minLeafSize を下回る場合は null（＝AreaSplitBlocked）。</summary>
    public static SplitPreview? CornerSplit(
        AreaCorner corner, Point origin, Point current, Size leafSize, double minLeafSize)
    {
        var axis = Math.Abs(current.X - origin.X) >= Math.Abs(current.Y - origin.Y)
            ? DockAxis.Columns
            : DockAxis.Rows;
        var total = axis == DockAxis.Columns ? leafSize.Width : leafSize.Height;
        if (total < minLeafSize * 2) return null;

        var margin = minLeafSize / total;
        var ratio = Math.Clamp((axis == DockAxis.Columns ? current.X : current.Y) / total, margin, 1 - margin);
        // 角から引き出す感覚に合わせ、新しい枠は掴んだ角の側にできる。
        var newIsSecond = axis == DockAxis.Columns
            ? corner is AreaCorner.TopRight or AreaCorner.BottomRight
            : corner is AreaCorner.BottomLeft or AreaCorner.BottomRight;
        return new SplitPreview(axis, ratio, newIsSecond);
    }

    // ---- Idle ----------------------------------------------------------------

    private static DragStep FromIdle(DragContext current, Intent intent, DragEnvironment env)
    {
        var ctx = intent.Context;
        var at = ctx.LeafLocalPoint ?? env.GripLeafLocal;
        switch (intent.Kind)
        {
            case IntentKind.AreaGripPressed when ctx.LeafId is { } leaf && ctx.Corner is { } corner:
                return Go(DragContext.Idle with
                {
                    Phase = DragPhase.AreaGripArmed, GripLeafId = leaf, Corner = corner, Origin = at,
                });
            case IntentKind.TabGripPressed when ctx.TabKind is { } kind:
                return Go(DragContext.Idle with
                {
                    Phase = DragPhase.TabGripArmed, TabKind = kind, SourceLeafId = ctx.LeafId,
                    SourceItemCount = env.SourceItemCount, Origin = at,
                });
            case IntentKind.RowGripPressed when ctx.RowIndex is >= 0 and var row:
                return Go(DragContext.Idle with
                {
                    Phase = DragPhase.RowGripArmed, RowOriginIndex = row, RowCurrentIndex = row, Origin = at,
                });
            case IntentKind.SplitGripPressed when ctx.SplitId is { } split:
                return Go(DragContext.Idle with { Phase = DragPhase.SplitGripDragging, SplitId = split });
            default:
                return Stay(current);
        }
    }

    // ---- Area（枠の四隅）------------------------------------------------------

    private static DragStep Area(DragContext c, Intent intent, DragEnvironment env)
    {
        var leaf = c.GripLeafId ?? -1;
        switch (intent.Kind)
        {
            case IntentKind.PointerMoved:
            {
                var point = env.GripLeafLocal;
                if (c.Phase == DragPhase.AreaGripArmed && c.Preview is null && c.JoinTargets is null
                    && BelowThreshold(c.Origin, point, env))
                    return Stay(c);

                var size = env.GripLeafSize;
                var inside = point.X >= 0 && point.Y >= 0 && point.X <= size.Width && point.Y <= size.Height;
                if (inside)
                {
                    var preview = CornerSplit(c.Corner ?? AreaCorner.TopLeft, c.Origin, point, size, env.MinLeafSize);
                    return preview is { } p
                        ? Go(c with { Phase = DragPhase.AreaSplitPreview, Preview = p, JoinTargets = null },
                            new DragEffect.ClearJoinTargets(), new DragEffect.ShowSplitPreview(leaf, p))
                        : Go(c with { Phase = DragPhase.AreaSplitBlocked, Preview = null, JoinTargets = null },
                            new DragEffect.ClearJoinTargets(), new DragEffect.ClearSplitPreview(leaf));
                }

                // 枠の外。同じ境目を挟む相手（兄弟）に乗っているときだけ、消える側を着色する。
                if (env.Hit is { HasLeaf: true } hit && env.SiblingLeafIds.Contains(hit.LeafId))
                {
                    var targets = env.SiblingLeafIds.ToList();
                    return Go(c with { Phase = DragPhase.AreaJoinPreview, Preview = null, JoinTargets = targets },
                        new DragEffect.ClearSplitPreview(leaf), new DragEffect.ShowJoinTargets(targets));
                }
                // 枠の外で兄弟にも乗っていない。以後も動き出し済みとして扱うため JoinTargets を空で残す。
                return Go(c with { Phase = DragPhase.AreaGripArmed, Preview = null, JoinTargets = Array.Empty<int>() },
                    new DragEffect.ClearSplitPreview(leaf), new DragEffect.ClearJoinTargets());
            }

            case IntentKind.PointerReleased:
                return c.Phase switch
                {
                    DragPhase.AreaSplitPreview when c.Preview is { } p => Go(DragContext.Idle,
                        new DragEffect.ClearSplitPreview(leaf), new DragEffect.CommitSplit(leaf, p), new DragEffect.Persist()),
                    DragPhase.AreaJoinPreview => Go(DragContext.Idle,
                        new DragEffect.ClearJoinTargets(), new DragEffect.CommitJoin(leaf), new DragEffect.Persist()),
                    _ => Go(DragContext.Idle),
                };

            case IntentKind.CancelRequested or IntentKind.PointerCaptureLost:
                return Go(DragContext.Idle, new DragEffect.ClearSplitPreview(leaf), new DragEffect.ClearJoinTargets());

            default:
                return Stay(c);
        }
    }

    // ---- Tab（タブの運び）------------------------------------------------------

    private static DragStep Tab(DragContext c, Intent intent, DragEnvironment env)
    {
        var kind = c.TabKind ?? "";
        switch (intent.Kind)
        {
            case IntentKind.PointerMoved:
            {
                if (c.Phase == DragPhase.TabGripArmed && BelowThreshold(c.Origin, env.GripLeafLocal, env))
                    return Stay(c);

                if (env.Hit is not { } hit)
                {
                    // どの窓にも乗っていない。離せば独立ウィンドウになることを影で示す（位置は毎回動かす）。
                    var dip = intent.Payload as Point? ?? c.OutsideDip ?? default;
                    return Go(c with { Phase = DragPhase.TabOutside, TargetLeafId = null, Preview = null, OutsideDip = dip },
                        ClearHover(c), new DragEffect.ShowGhost(kind, dip));
                }

                if (!hit.HasLeaf)
                {
                    if (c.Phase == DragPhase.TabOverNoLeaf) return Stay(c);
                    return Go(c with { Phase = DragPhase.TabOverNoLeaf, TargetLeafId = null, Preview = null, OutsideDip = null },
                        ClearHover(c), HideGhostIfOutside(c));
                }

                // 分割の下見は運び出した元の枠の端に限る（よその枠の端はタブとして合流させる）。
                // 元の枠にタブが 1 枚だけのときは出さない。割ってすぐ運び出すと空になった元の枠が畳まれ、
                // 見た目は元通りのまま枠番号だけ振り直るため。
                var edge = EdgeSplit(hit.LeafSize, hit.LeafLocal, env.EdgeZone, env.MinLeafSize);
                var split = hit.LeafId == c.SourceLeafId && c.SourceItemCount > 1 ? edge : null;
                if (split is { } p)
                {
                    if (c.Phase == DragPhase.TabOverSourceEdge && c.TargetLeafId == hit.LeafId && Equals(c.Preview, p))
                        return Stay(c);
                    return Go(c with { Phase = DragPhase.TabOverSourceEdge, TargetLeafId = hit.LeafId, Preview = p, OutsideDip = null },
                        ClearHover(c), new DragEffect.ShowSplitPreview(hit.LeafId, p), HideGhostIfOutside(c));
                }

                if (c.Phase == DragPhase.TabOverLeaf && c.TargetLeafId == hit.LeafId) return Stay(c);
                return Go(c with { Phase = DragPhase.TabOverLeaf, TargetLeafId = hit.LeafId, Preview = null, OutsideDip = null },
                    ClearHover(c), new DragEffect.ShowDropTarget(hit.LeafId), HideGhostIfOutside(c));
            }

            case IntentKind.PointerReleased:
                switch (c.Phase)
                {
                    case DragPhase.TabOverLeaf when c.TargetLeafId is { } target:
                        return target == c.SourceLeafId
                            ? Go(DragContext.Idle, new DragEffect.ClearDropTarget(target))
                            : Go(DragContext.Idle, new DragEffect.ClearDropTarget(target),
                                new DragEffect.CommitMove(kind, target), new DragEffect.Persist());
                    case DragPhase.TabOverSourceEdge when c.TargetLeafId is { } target && c.Preview is { } p:
                        // 上限で割れなければ、割らずにその枠へタブとして合流させる。
                        return env.LeafCount < env.MaxLeaves
                            ? Go(DragContext.Idle, new DragEffect.ClearSplitPreview(target),
                                new DragEffect.CommitSplitThenMove(kind, target, p), new DragEffect.Persist())
                            : Go(DragContext.Idle, new DragEffect.ClearSplitPreview(target),
                                new DragEffect.CommitMove(kind, target), new DragEffect.Persist());
                    case DragPhase.TabOutside:
                        return Go(DragContext.Idle, new DragEffect.HideGhost(),
                            new DragEffect.CommitFloat(kind, c.OutsideDip ?? default), new DragEffect.Persist());
                    default:
                        // 窓の中で枠を外して離した（ヘッダ・フッタの上）ときは動かさない。
                        return Go(DragContext.Idle);
                }

            case IntentKind.CancelRequested or IntentKind.PointerCaptureLost:
                return Go(DragContext.Idle, ClearHover(c), HideGhostIfOutside(c));

            default:
                return Stay(c);
        }
    }

    /// <summary>いま出している下見・着色を消す。</summary>
    private static DragEffect ClearHover(DragContext c) => c.Phase switch
    {
        DragPhase.TabOverLeaf when c.TargetLeafId is { } t => new DragEffect.ClearDropTarget(t),
        DragPhase.TabOverSourceEdge when c.TargetLeafId is { } t => new DragEffect.ClearSplitPreview(t),
        _ => DragEffect.None.Instance,
    };

    private static DragEffect HideGhostIfOutside(DragContext c)
        => c.Phase == DragPhase.TabOutside ? new DragEffect.HideGhost() : DragEffect.None.Instance;

    // ---- Row（一覧の行）--------------------------------------------------------

    private static DragStep Row(DragContext c, Intent intent, DragEnvironment env)
    {
        switch (intent.Kind)
        {
            case IntentKind.PointerMoved:
            {
                var to = intent.Context.RowIndex ?? -1;
                if (c.Phase == DragPhase.RowGripArmed)
                {
                    if (BelowThreshold(c.Origin, env.GripLeafLocal, env)) return Stay(c);
                    // 動き出した同じ移動で、もう落とし先が変わっていればその場で運ぶ。
                    var started = c with { Phase = DragPhase.RowReordering };
                    return to >= 0 && to != c.RowCurrentIndex
                        ? Go(started with { RowCurrentIndex = to },
                            new DragEffect.SetRowDragging(c.RowCurrentIndex, true), new DragEffect.CommitRowOrder(c.RowCurrentIndex, to))
                        : Go(started, new DragEffect.SetRowDragging(c.RowCurrentIndex, true));
                }
                return to >= 0 && to != c.RowCurrentIndex
                    ? Go(c with { RowCurrentIndex = to }, new DragEffect.CommitRowOrder(c.RowCurrentIndex, to))
                    : Stay(c);
            }

            case IntentKind.PointerReleased:
                // 運んだ先がそのまま並び順になるので、確定は掴みを解くだけでよい。
                return c.Phase == DragPhase.RowReordering
                    ? Go(DragContext.Idle, new DragEffect.SetRowDragging(c.RowCurrentIndex, false))
                    : Go(DragContext.Idle);

            case IntentKind.CancelRequested or IntentKind.PointerCaptureLost:
                return c.Phase == DragPhase.RowReordering
                    ? Go(DragContext.Idle, new DragEffect.RestoreRowOrder(c.RowOriginIndex),
                        new DragEffect.SetRowDragging(c.RowOriginIndex, false))
                    : Go(DragContext.Idle);

            default:
                return Stay(c);
        }
    }

    // ---- Split（境目のつまみ）---------------------------------------------------

    private static DragStep Split(DragContext c, Intent intent)
    {
        var id = c.SplitId ?? -1;
        return intent.Kind switch
        {
            IntentKind.PointerMoved => Go(c, new DragEffect.CommitResize(
                id, intent.Context.DragChange ?? 0, intent.Context.DragTotal ?? 0)),
            IntentKind.PointerReleased or IntentKind.CancelRequested or IntentKind.PointerCaptureLost
                => Go(DragContext.Idle, new DragEffect.Persist()),
            _ => Stay(c),
        };
    }

    // ---- 共通 --------------------------------------------------------------------

    /// <summary>動き出しの閾値。WPF の既定（SystemParameters.Minimum*DragDistance）と同じく、
    /// 縦横どちらかが閾値に届いた時点で動き出す（ちょうど等しければ動き出す）。</summary>
    private static bool BelowThreshold(Point origin, Point current, DragEnvironment env)
        => Math.Abs(current.X - origin.X) < env.DragThresholdX && Math.Abs(current.Y - origin.Y) < env.DragThresholdY;

    private static DragStep Stay(DragContext c) => new(c, DragEffect.None.Instance);

    private static DragStep Go(DragContext next, params DragEffect?[] effects) => new(next, DragEffect.Combine(effects));
}
