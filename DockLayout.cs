using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>枠の割り方。Columns は左右に、Rows は上下に並べる。</summary>
public enum DockAxis { Columns, Rows }

/// <summary>
/// これから割る位置の下見。角を掴んで動かしている間だけ枠が持ち、離した時点で実際の分割になる。
/// <c>Ratio</c> は枠の左端（上端）からの割合、<c>NewIsSecond</c> は
/// 新しい空の枠が右（下）側にできるか。
/// </summary>
public sealed record SplitPreview(DockAxis Axis, double Ratio, bool NewIsSecond);

/// <summary>
/// 画面の割り付けの節。葉（<see cref="DockLeaf"/>）がタブ束ひとつ、
/// 節（<see cref="DockSplit"/>）が「2 つに割った境目」を表す。Blender の画面分割と同じ入れ子で、
/// 上下左右の決め打ちを持たない。
/// </summary>
public abstract class DockNode : ViewModelBase
{
    /// <summary>この節を含む親。根なら null。木を組み替える側（<see cref="DockLayout"/>）だけが書く。</summary>
    public DockSplit? Parent { get; internal set; }

    /// <summary>属する割り付け。ビュー側はここから分割・結合・保存を呼ぶ。</summary>
    public DockLayout? Owner { get; internal set; }

    /// <summary>この節にぶら下がる葉。自分が葉ならば自分ひとつ。</summary>
    public abstract IEnumerable<DockLeaf> Leaves { get; }
}

/// <summary>
/// タブ束ひとつぶんの枠。同じ枠に入れたオーバーレイはタブで切り替える（同時には 1 枚だけ表に出る）。
/// タブを掴んでいる間はこの枠そのものがドロップ先になり、カーソルが乗ると全体が着色される。
/// </summary>
public sealed class DockLeaf : DockNode
{
    private OverlayViewModel? _selected;
    private bool _isDropTarget;
    private bool _isJoinTarget;
    private SplitPreview? _preview;

    public DockLeaf(int id)
    {
        Id = id;
        Items.CollectionChanged += OnItemsChanged;
    }

    /// <summary>枠の通し番号。種類ごとの「前にどこへ置いたか」を設定に覚えさせる鍵。</summary>
    public int Id { get; }

    public ObservableCollection<OverlayViewModel> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    /// <summary>分割で作ったまま、まだタブを迎えていない枠か。案内と［枠を閉じる］を出す。</summary>
    public bool IsEmpty => Items.Count == 0;

    public OverlayViewModel? Selected
    {
        get => _selected;
        set
        {
            var previous = _selected;
            if (!Set(ref _selected, value)) return;
            if (previous is not null) previous.IsActive = false;
            if (value is not null) value.IsActive = true;
        }
    }

    /// <summary>タブを運んできたカーソルが今この枠に乗っているか。真の間だけ全体を着色する。</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    /// <summary>結合したら消える側か。角を外へ引いている間、吸収される枠すべてに立つ。</summary>
    public bool IsJoinTarget
    {
        get => _isJoinTarget;
        set => Set(ref _isJoinTarget, value);
    }

    /// <summary>分割の下見。null なら出していない。</summary>
    public SplitPreview? Preview
    {
        get => _preview;
        set => Set(ref _preview, value);
    }

    public override IEnumerable<DockLeaf> Leaves
    {
        get { yield return this; }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Raise(nameof(HasItems));
        Raise(nameof(IsEmpty));
        // 表に出していたタブが消えたら、残っているうち一番新しいものに移る。
        if (Selected is null || !Items.Contains(Selected)) Selected = Items.LastOrDefault();
    }
}

/// <summary>
/// 枠を 2 つに割った境目。<see cref="Ratio"/> は First 側の取り分で、境目のつまみを引くと動く。
/// Grid の行・列そのものを組み替えずに済むよう、割り付けに要る長さと位置をここから配る。
/// </summary>
public sealed class DockSplit : DockNode
{
    /// <summary>境目のつまみの太さ。左右・上下どちらでも同じ。</summary>
    public const double GripThickness = 5;

    /// <summary>割ったあとに残す最小の幅・高さ。これ以下には縮められない。</summary>
    public const double MinLeafSize = 140;

    private double _ratio;

    public DockSplit(DockAxis axis, DockNode first, DockNode second, double ratio)
    {
        Axis = axis;
        First = first;
        Second = second;
        _ratio = Math.Clamp(ratio, 0.05, 0.95);
        first.Parent = this;
        second.Parent = this;
    }

    public DockAxis Axis { get; }

    public DockNode First { get; private set; }

    public DockNode Second { get; private set; }

    public double Ratio => _ratio;

    /// <summary>左右に並べるか。偽なら上下。</summary>
    public bool IsColumns => Axis == DockAxis.Columns;

    // 割り付けは 3 行 3 列の Grid ひとつで賄う。使わない側は 0 にして畳む。
    public GridLength Column0 => IsColumns ? Star(_ratio) : Star(1);
    public GridLength Column1 => IsColumns ? new GridLength(GripThickness) : new GridLength(0);
    public GridLength Column2 => IsColumns ? Star(1 - _ratio) : new GridLength(0);
    public GridLength Row0 => IsColumns ? Star(1) : Star(_ratio);
    public GridLength Row1 => IsColumns ? new GridLength(0) : new GridLength(GripThickness);
    public GridLength Row2 => IsColumns ? new GridLength(0) : Star(1 - _ratio);

    public int GripRow => IsColumns ? 0 : 1;
    public int GripColumn => IsColumns ? 1 : 0;
    public int SecondRow => IsColumns ? 0 : 2;
    public int SecondColumn => IsColumns ? 2 : 0;

    public Cursor GripCursor => IsColumns ? Cursors.SizeWE : Cursors.SizeNS;

    public override IEnumerable<DockLeaf> Leaves => First.Leaves.Concat(Second.Leaves);

    /// <summary>相方の節。結合で吸収する相手を引くのに使う。</summary>
    public DockNode Other(DockNode child) => ReferenceEquals(child, First) ? Second : First;

    /// <summary>境目のつまみのドラッグ量を取り分に反映する。<paramref name="total"/> は割り付け全体の実寸。</summary>
    public void Resize(double change, double total)
    {
        if (total <= MinLeafSize * 2) return;
        var margin = MinLeafSize / total;
        var next = Math.Clamp(_ratio + change / total, margin, 1 - margin);
        if (Math.Abs(next - _ratio) < 0.0005) return;
        _ratio = next;
        RaiseLengths();
    }

    /// <summary>子を差し替える。木の組み替えは <see cref="DockLayout"/> だけが行う。</summary>
    internal void Replace(DockNode old, DockNode fresh)
    {
        if (ReferenceEquals(old, First))
        {
            First = fresh;
            Raise(nameof(First));
        }
        else
        {
            Second = fresh;
            Raise(nameof(Second));
        }
        fresh.Parent = this;
    }

    private void RaiseLengths()
    {
        Raise(nameof(Ratio));
        Raise(nameof(Column0));
        Raise(nameof(Column2));
        Raise(nameof(Row0));
        Raise(nameof(Row2));
    }

    private static GridLength Star(double value) => new(Math.Max(value, 0.01), GridUnitType.Star);
}

/// <summary>
/// 画面の割り付け全体。根の節ひとつと、種類ごとの「前にどこへ置いたか」を持つ。
/// 分割・結合・タブの移動はここだけが行い、そのたびに settings.json へ書き戻す。
/// </summary>
public sealed class DockLayout : ViewModelBase
{
    private const int MaxDepth = 8;
    private const int MaxLeaves = 24;

    private readonly AppSettings _settings;

    /// <summary>種類名 → 前に置いた枠の番号。閉じたタブの行き先もここで覚えておく。</summary>
    private readonly Dictionary<string, int> _homes = new();

    private DockNode _root;
    private int _nextId = 1;

    public DockLayout(AppSettings settings)
    {
        _settings = settings;
        _root = _settings.Layout is { } saved ? Build(saved, 0) ?? Fresh() : Fresh();
        _root.Owner = this;
    }

    /// <summary>割り付けの根。ビューはこれ 1 つを描き、あとは節ごとの入れ子に任せる。</summary>
    public DockNode Root
    {
        get => _root;
        private set
        {
            value.Parent = null;
            Set(ref _root, value);
        }
    }

    /// <summary>行き先を覚えていない種類が出る枠。単語詳細のいる枠を既定とする。</summary>
    public DockLeaf Main
        => Root.Leaves.FirstOrDefault(l => l.Items.Any(i => i is WordDetailViewModel)) ?? Root.Leaves.First();

    public IEnumerable<DockLeaf> AllLeaves => Root.Leaves;

    public IEnumerable<OverlayViewModel> Overlays => Root.Leaves.SelectMany(l => l.Items);

    /// <summary>タブが表に出た（開いた・選び直した）ことの通知。Esc の行き先を追うのに使う。</summary>
    public event Action<OverlayViewModel?>? Touched;

    public DockLeaf? LeafOf(OverlayViewModel vm) => Root.Leaves.FirstOrDefault(l => l.Items.Contains(vm));

    /// <summary>覚えている枠、無ければ既定の枠にタブを足して表に出す。</summary>
    public void Add(OverlayViewModel vm)
    {
        var leaf = _homes.TryGetValue(vm.Kind, out var id)
            ? Root.Leaves.FirstOrDefault(l => l.Id == id) ?? Main
            : Main;
        leaf.Items.Add(vm);
        leaf.Selected = vm;
        Remember(vm, leaf);
        Save();
    }

    public void Remove(OverlayViewModel vm)
    {
        if (LeafOf(vm) is not { } leaf) return;
        leaf.Items.Remove(vm);
        // 最後の 1 枚を閉じた枠は隣に吸収させる。分割で作った空の枠だけが残る形にする。
        DissolveIfEmpty(leaf);
        Save();
    }

    /// <summary>タブを別の枠へ運ぶ。空になった運び元は隣に吸収される。</summary>
    public void Move(OverlayViewModel vm, DockLeaf target)
    {
        var source = LeafOf(vm);
        if (source is null || ReferenceEquals(source, target)) return;
        source.Items.Remove(vm);
        target.Items.Add(vm);
        target.Selected = vm;
        Remember(vm, target);
        DissolveIfEmpty(source);
        Save();
    }

    /// <summary>枠を 2 つに割る。新しくできる側は空のままで、タブを運び込むまで案内を出す。</summary>
    public void Split(DockLeaf leaf, DockAxis axis, double ratio, bool newIsSecond)
    {
        if (Root.Leaves.Count() >= MaxLeaves) return;
        // DockSplit のコンストラクタは leaf.Parent をこの新しい節へ即座に付け替えるので、
        // 差し込み先を ReplaceNode に探させる（leaf.Parent を読む）前に元の親を控えておく。
        var parent = leaf.Parent;
        var fresh = NewLeaf();
        var split = newIsSecond
            ? new DockSplit(axis, leaf, fresh, ratio)
            : new DockSplit(axis, fresh, leaf, ratio);
        split.Owner = this;
        fresh.Owner = this;
        if (parent is not null) parent.Replace(leaf, split);
        else Root = split;
        Save();
    }

    /// <summary>
    /// 隣の枠を吸収して 1 つに戻す。吸収される側のタブは残る側へ移すので、結合で画面は消えない。
    /// </summary>
    public void Join(DockLeaf survivor)
    {
        if (survivor.Parent is not { } parent) return;
        var victim = parent.Other(survivor);
        foreach (var item in victim.Leaves.SelectMany(l => l.Items).ToList())
        {
            LeafOf(item)?.Items.Remove(item);
            survivor.Items.Add(item);
            Remember(item, survivor);
        }
        survivor.Selected ??= survivor.Items.LastOrDefault();
        ReplaceNode(parent, survivor);
        Save();
    }

    /// <summary>枠そのものを畳む。中のタブは隣の枠へ移す（空の枠を閉じる操作もここを通る）。</summary>
    public void Dissolve(DockLeaf leaf)
    {
        DissolveCore(leaf);
        Save();
    }

    private void DissolveCore(DockLeaf leaf)
    {
        if (leaf.Parent is not { } parent) return;   // 根が 1 枚だけのときは畳まない
        var sibling = parent.Other(leaf);
        if (sibling.Leaves.FirstOrDefault() is { } host)
        {
            foreach (var item in leaf.Items.ToList())
            {
                leaf.Items.Remove(item);
                host.Items.Add(item);
                Remember(item, host);
            }
            host.Selected ??= host.Items.LastOrDefault();
        }
        ReplaceNode(parent, sibling);
    }

    /// <summary>割り付けと行き先の記憶を settings.json に書き戻す。</summary>
    public void Save()
    {
        _settings.Layout = Write(Root);
        _settings.Save();
    }

    private void DissolveIfEmpty(DockLeaf leaf)
    {
        if (leaf.IsEmpty && leaf.Parent is not null) DissolveCore(leaf);
    }

    private void Remember(OverlayViewModel vm, DockLeaf leaf) => _homes[vm.Kind] = leaf.Id;

    private void ReplaceNode(DockNode old, DockNode fresh)
    {
        if (old.Parent is { } parent) parent.Replace(old, fresh);
        else Root = fresh;
    }

    private DockLeaf NewLeaf(int? id = null)
    {
        var leaf = new DockLeaf(id ?? _nextId++) { Owner = this };
        if (id is { } given && given >= _nextId) _nextId = given + 1;
        leaf.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DockLeaf.Selected) && s is DockLeaf l) Touched?.Invoke(l.Selected);
        };
        return leaf;
    }

    /// <summary>設定が無い・壊れているときの既定。左に検索、右に単語詳細の 2 枠。</summary>
    private DockNode Fresh()
    {
        _homes.Clear();
        var search = NewLeaf();
        var detail = NewLeaf();
        _homes[nameof(SearchViewModel)] = search.Id;
        _homes[nameof(WordDetailViewModel)] = detail.Id;
        return new DockSplit(DockAxis.Columns, search, detail, 0.31) { Owner = this };
    }

    private DockNode? Build(DockNodeSettings node, int depth)
    {
        if (depth > MaxDepth) return null;

        if (node.Axis is { } axis && node.First is { } first && node.Second is { } second)
        {
            var a = Build(first, depth + 1);
            var b = Build(second, depth + 1);
            if (a is null || b is null) return a ?? b;   // 片方だけ読めたらそれで代える
            var split = new DockSplit(
                axis == nameof(DockAxis.Rows) ? DockAxis.Rows : DockAxis.Columns, a, b, node.Ratio);
            split.Owner = this;
            return split;
        }

        // 番号を持たない（＝古い設定や壊れた設定の）枠は新しく振り直す。番号が重なると行き先が混ざる。
        var leaf = NewLeaf(node.Id > 0 ? node.Id : null);
        foreach (var kind in node.Tabs) _homes[kind] = leaf.Id;
        return leaf;
    }

    private DockNodeSettings Write(DockNode node)
    {
        if (node is DockSplit split)
        {
            return new DockNodeSettings
            {
                Axis = split.Axis.ToString(),
                Ratio = split.Ratio,
                First = Write(split.First),
                Second = Write(split.Second),
            };
        }

        var leaf = (DockLeaf)node;
        // 今並んでいるタブが先。閉じているだけの種類も、次に開いたとき同じ枠へ出すために残す。
        var kinds = leaf.Items.Select(i => i.Kind).ToList();
        kinds.AddRange(_homes
            .Where(h => h.Value == leaf.Id && !kinds.Contains(h.Key))
            .Select(h => h.Key));
        return new DockNodeSettings { Id = leaf.Id, Tabs = kinds };
    }
}

/// <summary>
/// タブを掴んで別の枠へ運んでいる間だけの状態。オーバーレイ側の DataContext は MainViewModel では
/// ないため、FontScaleState などと同じく {x:Static} で引ける singleton にしてある。
/// 実際に動かすのは <see cref="Move"/>（MainViewModel が差し込む）。
/// </summary>
public sealed class OverlayDragState : ViewModelBase
{
    public static OverlayDragState Instance { get; } = new();

    private DockLeaf? _hoverLeaf;
    private OverlayViewModel? _dragged;

    public Action<OverlayViewModel, DockLeaf>? Move { get; set; }

    /// <summary>
    /// 今カーソルが乗っている枠。乗っている枠が着色され、離せばそこへ移る。
    /// どの枠にも乗っていなければ null で、離しても動かさない。
    /// </summary>
    public DockLeaf? HoverLeaf
    {
        get => _hoverLeaf;
        set
        {
            var previous = _hoverLeaf;
            if (!Set(ref _hoverLeaf, value)) return;
            if (previous is not null) previous.IsDropTarget = false;
            if (value is not null) value.IsDropTarget = true;
        }
    }

    public void BeginDrag(OverlayViewModel vm)
    {
        _dragged = vm;
        HoverLeaf = null;
    }

    /// <summary>乗っている枠へ移す。枠の外で離したときは何もしない。</summary>
    public void CompleteDrag()
    {
        var vm = _dragged;
        var leaf = HoverLeaf;
        Cancel();
        if (vm is not null && leaf is not null) Move?.Invoke(vm, leaf);
    }

    public void Cancel()
    {
        HoverLeaf = null;
        _dragged = null;
    }
}
