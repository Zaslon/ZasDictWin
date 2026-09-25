using System.Windows;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

/// <summary>ドラッグの遷移が生む副作用。純粋関数から返るデータなので、そのままテストで検査できる。</summary>
public abstract record DragEffect
{
    public sealed record None : DragEffect { public static readonly None Instance = new(); }

    public sealed record Many(IReadOnlyList<DragEffect> Effects) : DragEffect
    {
        // record の既定の等価比較は IReadOnlyList を参照で比べるので、テストで中身を比べられるよう並びで比べる。
        public bool Equals(Many? other) => other is not null && Effects.SequenceEqual(other.Effects);

        public override int GetHashCode() => Effects.Count;
    }

    // ---- 下見・着色（描画パラメータの更新だけ。木は変えない）----
    public sealed record ShowSplitPreview(int LeafId, SplitPreview Preview) : DragEffect;
    public sealed record ClearSplitPreview(int LeafId) : DragEffect;

    public sealed record ShowJoinTargets(IReadOnlyList<int> LeafIds) : DragEffect
    {
        public bool Equals(ShowJoinTargets? other) => other is not null && LeafIds.SequenceEqual(other.LeafIds);

        public override int GetHashCode() => LeafIds.Count;
    }

    public sealed record ClearJoinTargets : DragEffect;
    public sealed record ShowDropTarget(int LeafId) : DragEffect;
    public sealed record ClearDropTarget(int LeafId) : DragEffect;
    public sealed record ShowGhost(string TabKind, Point AtDip) : DragEffect;
    public sealed record HideGhost : DragEffect;
    public sealed record SetRowDragging(int RowIndex, bool Dragging) : DragEffect;

    // ---- 確定（Mediator が DockLayout の internal メソッドを呼ぶ）----
    public sealed record CommitSplit(int LeafId, SplitPreview Preview) : DragEffect;
    public sealed record CommitJoin(int SurvivorLeafId) : DragEffect;
    public sealed record CommitMove(string TabKind, int TargetLeafId) : DragEffect;
    public sealed record CommitSplitThenMove(string TabKind, int LeafId, SplitPreview Preview) : DragEffect;
    public sealed record CommitFloat(string TabKind, Point AtDip) : DragEffect;
    public sealed record CommitResize(int SplitId, double Change, double Total) : DragEffect;
    public sealed record CommitRowOrder(int From, int To) : DragEffect;
    public sealed record RestoreRowOrder(int To) : DragEffect;

    /// <summary>settings.json への書き戻し。1 つの Intent の裁定の最後に 1 回だけ出す。</summary>
    public sealed record Persist : DragEffect;

    /// <summary>入れ子を 1 段に潰して束ねる。None は省き、1 つなら素のまま、0 なら None.Instance。</summary>
    public static DragEffect Combine(params DragEffect?[] effects)
    {
        var flat = new List<DragEffect>();
        foreach (var e in effects) Flatten(e, flat);
        return flat.Count switch
        {
            0 => None.Instance,
            1 => flat[0],
            _ => new Many(flat),
        };
    }

    private static void Flatten(DragEffect? effect, List<DragEffect> into)
    {
        switch (effect)
        {
            case null or None:
                return;
            case Many many:
                foreach (var e in many.Effects) Flatten(e, into);
                return;
            default:
                into.Add(effect);
                return;
        }
    }

    /// <summary>束ねた副作用を 1 つずつ並べる（Many は開く、None は何も出さない）。</summary>
    public IEnumerable<DragEffect> Flat()
    {
        var list = new List<DragEffect>();
        Flatten(this, list);
        return list;
    }
}
