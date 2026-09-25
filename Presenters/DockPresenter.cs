using System.Windows;
using System.Windows.Controls;
using ZasDictWin.Mediator;
using ZasDictWin.ViewModels;
using ZasDictWin.Views;

namespace ZasDictWin.Presenters;

/// <summary>
/// ドラッグの副作用のうち「描画パラメータの更新」だけを割り付けの枠と一覧の行へ写す。
/// 木の組み替え（Commit*）と書き戻し（Persist）は AppMediator が DockLayout へ直接適用するので、
/// ここには届かない（二重に適用しないための分担）。
/// </summary>
public sealed class DockPresenter
{
    private readonly AppMediator _mediator;
    private readonly DockLayout _layout;
    private readonly HashSet<DockLeaf> _joinTargets = new();

    private FrameworkElement? _draggedRow;
    private object? _draggedItem;

    public DockPresenter(AppMediator mediator, DockLayout layout)
    {
        _mediator = mediator;
        _layout = layout;
        mediator.DragEffectIssued += Apply;
    }

    private void Apply(DragEffect effect)
    {
        switch (effect)
        {
            case DragEffect.ShowSplitPreview e when _layout.LeafById(e.LeafId) is { } leaf:
                leaf.Preview = e.Preview;
                break;
            case DragEffect.ClearSplitPreview e when _layout.LeafById(e.LeafId) is { } leaf:
                leaf.Preview = null;
                break;
            case DragEffect.ShowDropTarget e when _layout.LeafById(e.LeafId) is { } leaf:
                leaf.IsDropTarget = true;
                break;
            case DragEffect.ClearDropTarget e when _layout.LeafById(e.LeafId) is { } leaf:
                leaf.IsDropTarget = false;
                break;
            case DragEffect.ShowJoinTargets e:
                var targets = e.LeafIds.Select(_layout.LeafById).OfType<DockLeaf>().ToHashSet();
                foreach (var gone in _joinTargets.Where(l => !targets.Contains(l)).ToList())
                {
                    gone.IsJoinTarget = false;
                    _joinTargets.Remove(gone);
                }
                foreach (var leaf in targets)
                {
                    leaf.IsJoinTarget = true;
                    _joinTargets.Add(leaf);
                }
                break;
            case DragEffect.ClearJoinTargets:
                foreach (var leaf in _joinTargets) leaf.IsJoinTarget = false;
                _joinTargets.Clear();
                break;
            case DragEffect.SetRowDragging e:
                SetRowDragging(e.RowIndex, e.Dragging);
                break;
            case DragEffect.CommitRowOrder e when _mediator.RowDragSource is ItemsControl list:
                RowDrag.ApplyOrder(list, e.From, e.To);
                break;
            case DragEffect.RestoreRowOrder e when _mediator.RowDragSource is ItemsControl list && _draggedItem is not null:
                var from = list.Items.IndexOf(_draggedItem);
                if (from >= 0 && from != e.To) RowDrag.ApplyOrder(list, from, e.To);
                break;
        }
    }

    /// <summary>掴んでいる間は運んでいる行そのものを薄くして見せる。行は並べ替えで位置が変わるので、
    /// 掴んだ時点の行（コンテナ）を覚えておき、離したときにその行を元に戻す。</summary>
    private void SetRowDragging(int index, bool dragging)
    {
        if (dragging)
        {
            if (_mediator.RowDragSource is not ItemsControl list || index < 0 || index >= list.Items.Count) return;
            _draggedItem = list.Items[index];
            _draggedRow = list.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (_draggedRow is not null) _draggedRow.Opacity = 0.6;
            return;
        }
        if (_draggedRow is not null) _draggedRow.Opacity = 1;
        _draggedRow = null;
        _draggedItem = null;
    }
}
