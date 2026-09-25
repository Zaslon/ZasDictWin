using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using ZasDictWin.Mediator;
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
        internal set
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
        internal set => Set(ref _isDropTarget, value);
    }

    /// <summary>結合したら消える側か。角を外へ引いている間、吸収される枠すべてに立つ。</summary>
    public bool IsJoinTarget
    {
        get => _isJoinTarget;
        internal set => Set(ref _isJoinTarget, value);
    }

    /// <summary>分割の下見。null なら出していない。</summary>
    public SplitPreview? Preview
    {
        get => _preview;
        internal set => Set(ref _preview, value);
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

    /// <summary>境目の通し番号。つまみの操作（Intent）がどの境目のものかを割り付けへ伝える鍵。
    /// 設定には書かないので、起動のたびに振り直される。</summary>
    public int Id { get; internal set; }

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
    internal void Resize(double change, double total)
    {
        if (total <= 0) return;
        var next = LayoutRules.ClampRatio(_ratio, _ratio + change / total, total, MinLeafSize);
        if (next.Equals(_ratio)) return;
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
/// 窓の外へ持ち出した枠ひとつぶん＝独立ウィンドウ 1 枚。中身は本体と同じ節の入れ子なので、
/// 窓の中でもさらに枠を割ったり結合したりできる。
/// 中身が空の間は窓を出さず、「その種類を前にどこへ置いたか」の記憶としてだけ残る。
/// </summary>
public sealed class DockFloat : ViewModelBase
{
    private DockNode _root;

    public DockFloat(DockNode root, Rect bounds)
    {
        _root = root;
        root.Parent = null;
        Bounds = bounds;
    }

    /// <summary>この浮き枠を描く窓の識別子。窓（IUiHost）の HostId にそのまま使う。</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public DockNode Root
    {
        get => _root;
        internal set
        {
            value.Parent = null;
            Set(ref _root, value);
        }
    }

    /// <summary>窓の位置と大きさ（DIP）。窓を動かす・大きさを変えるたびに控え、終了時に設定へ書き出す。</summary>
    public Rect Bounds { get; internal set; }

    public IEnumerable<DockLeaf> Leaves => Root.Leaves;

    public IEnumerable<OverlayViewModel> Items => Leaves.SelectMany(l => l.Items);

    /// <summary>窓を出してよいか。空の浮き枠は記憶だけの存在で、画面には現れない。</summary>
    public bool HasItems => Items.Any();

    /// <summary>窓の題。表に出ているタブの名前を並べる（OBS のウィンドウ一覧で見分ける手掛かり）。</summary>
    public string Title => string.Join(" / ", Leaves
        .Select(l => l.Selected?.Title)
        .Where(t => !string.IsNullOrEmpty(t)));

    internal void Refresh()
    {
        Raise(nameof(Title));
        Raise(nameof(HasItems));
    }
}

/// <summary>
/// 画面の割り付け全体。本体の窓の根ひとつと、外へ持ち出した浮き枠、
/// それに種類ごとの「前にどこへ置いたか」を持つ。
/// 木を組み替えるメソッドは AppMediator だけが呼び、settings.json への書き戻し（<see cref="Persist"/>）は
/// 1 つの Intent の裁定の最後に 1 回だけ行われる。
/// </summary>
public sealed class DockLayout : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly Action _save;

    /// <summary>種類名 → 前に置いた枠の番号。閉じたタブの行き先もここで覚えておく。</summary>
    private readonly Dictionary<string, int> _homes = new();

    private readonly List<DockFloat> _floats = new();

    private DockNode _root;
    private int _nextId = 1;
    private int _nextSplitId = 1;

    /// <param name="save">設定を書き出す手段。既定は settings.json への保存。</param>
    public DockLayout(AppSettings settings, Action? save = null)
    {
        _settings = settings;
        _save = save ?? settings.Save;
        _root = _settings.Layout is { } saved ? Build(saved, 0) ?? Fresh() : Fresh();
        foreach (var host in _settings.Floats)
        {
            if (host.Node is not { } node || Build(node, 0) is not { } built) continue;
            _floats.Add(new DockFloat(built, new Rect(host.Left ?? double.NaN, host.Top ?? double.NaN, host.Width, host.Height)));
        }
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

    /// <summary>本体の窓から持ち出した枠。中身のあるものだけがウィンドウとして現れる。</summary>
    public IReadOnlyList<DockFloat> Floats => _floats;

    /// <summary>行き先を覚えていない種類が出る枠。単語詳細のいる枠を既定とする。
    /// 探すのは本体の窓の中だけ（既定の行き先が独立ウィンドウになると、本体が空のまま取り残される）。</summary>
    public DockLeaf Main
        => Root.Leaves.FirstOrDefault(l => l.Items.Any(i => i is WordDetailViewModel)) ?? Root.Leaves.First();

    /// <summary>本体と独立ウィンドウ、すべての根。</summary>
    private IEnumerable<DockNode> Roots
    {
        get
        {
            yield return Root;
            foreach (var host in _floats) yield return host.Root;
        }
    }

    public IEnumerable<DockLeaf> AllLeaves => Roots.SelectMany(r => r.Leaves);

    public IEnumerable<OverlayViewModel> Overlays => AllLeaves.SelectMany(l => l.Items);

    /// <summary>種類ごとの「前にどこへ置いたか」。</summary>
    internal IReadOnlyDictionary<string, int> Homes => _homes;

    /// <summary>どこかの枠で表に出るタブが変わった（開いた・選び直した・隣へ移った）。Esc の行き先を追うのに使う。</summary>
    internal Action<OverlayViewModel?>? SelectedChanged { get; set; }

    public DockLeaf? LeafOf(OverlayViewModel vm) => AllLeaves.FirstOrDefault(l => l.Items.Contains(vm));

    /// <summary>そのタブが独立ウィンドウにいるなら、その窓ぶんの浮き枠。本体にいれば null。</summary>
    public DockFloat? FloatOf(OverlayViewModel vm) => _floats.FirstOrDefault(f => f.Items.Contains(vm));

    public DockLeaf? LeafById(int id) => AllLeaves.FirstOrDefault(l => l.Id == id);

    public DockSplit? SplitById(int id) => Roots.SelectMany(Splits).FirstOrDefault(s => s.Id == id);

    public DockFloat? FloatById(Guid id) => _floats.FirstOrDefault(f => f.Id == id);

    private static IEnumerable<DockSplit> Splits(DockNode node)
    {
        if (node is not DockSplit split) yield break;
        yield return split;
        foreach (var s in Splits(split.First)) yield return s;
        foreach (var s in Splits(split.Second)) yield return s;
    }

    /// <summary>その枠が独立ウィンドウの根なら、その窓ぶんの浮き枠。</summary>
    private DockFloat? HostOf(DockNode node) => _floats.FirstOrDefault(f => ReferenceEquals(f.Root, node));

    /// <summary>覚えている枠、無ければ既定の枠にタブを足して表に出す。
    /// 行き先を覚えていない種類のうち独立ウィンドウ向きのもの（ツール類）は、新しい窓を 1 枚こしらえて出す。</summary>
    internal void Add(OverlayViewModel vm)
    {
        var existing = AllLeaves.Select(l => l.Id).ToHashSet();
        switch (LayoutRules.ResolvePlacement(vm.Kind, vm.PrefersFloating, _homes, existing, out var id))
        {
            case LayoutRules.PlaceTarget.RememberedLeaf when LeafById(id) is { } home:
                Place(vm, home);
                return;
            case LayoutRules.PlaceTarget.NewFloat when Float(vm, null) is not null:
                return;
            default:
                Place(vm, Main);
                return;
        }
    }

    internal void Place(OverlayViewModel vm, DockLeaf leaf)
    {
        leaf.Items.Add(vm);
        leaf.Selected = vm;
        Remember(vm, leaf);
    }

    internal void Remove(OverlayViewModel vm)
    {
        if (LeafOf(vm) is not { } leaf) return;
        leaf.Items.Remove(vm);
        // 最後の 1 枚を閉じた枠は隣に吸収させる。分割で作った空の枠だけが残る形にする。
        DissolveIfEmpty(leaf);
    }

    /// <summary>
    /// タブを窓の外へ持ち出す。新しい独立ウィンドウを 1 枚こしらえ、そこへ移す。
    /// <paramref name="atDip"/> は窓の左上に置きたい位置（DIP）で、null なら本体の中央に出す。
    /// </summary>
    internal DockFloat? Float(OverlayViewModel vm, Point? atDip)
    {
        var source = LeafOf(vm);
        var sourceIsFloatRoot = source is not null && ReferenceEquals(HostOf(source)?.Root, source);
        if (!LayoutRules.CanFloat(AllLeaves.Count(), source?.Items.Count ?? 0, sourceIsFloatRoot)) return null;

        var leaf = NewLeaf();
        var size = vm.FloatSize;
        var host = new DockFloat(leaf, new Rect(atDip?.X ?? double.NaN, atDip?.Y ?? double.NaN, size.Width, size.Height));
        _floats.Add(host);
        source?.Items.Remove(vm);
        leaf.Items.Add(vm);
        leaf.Selected = vm;
        Remember(vm, leaf);
        if (source is not null) DissolveIfEmpty(source);
        return host;
    }

    /// <summary>独立ウィンドウを割り付けから外す。窓を手で閉じたときに、中身を始末した後で呼ぶ。</summary>
    internal void Discard(DockFloat host)
    {
        if (!_floats.Remove(host)) return;
        // 残っていたタブは本体へ引き取る（据え置きのタブは閉じられないので、行き場が要る）。
        // 外した後は LeafOf で辿れないため、枠から直に取り出す。
        // 行き先の記憶は消えた枠を指したままになるが、その枠はもう無いので次に開くときは既定の枠へ落ちる。
        var main = Main;
        foreach (var leaf in host.Leaves.ToList())
        {
            foreach (var item in leaf.Items.ToList())
            {
                leaf.Items.Remove(item);
                main.Items.Add(item);
                main.Selected = item;
                Remember(item, main);
            }
        }
    }

    /// <summary>タブを別の枠へ運ぶ。空になった運び元は隣に吸収される。</summary>
    internal void Move(OverlayViewModel vm, DockLeaf target)
    {
        var source = LeafOf(vm);
        if (source is null || ReferenceEquals(source, target)) return;
        source.Items.Remove(vm);
        target.Items.Add(vm);
        target.Selected = vm;
        Remember(vm, target);
        DissolveIfEmpty(source);
    }

    /// <summary>枠を 2 つに割る。新しくできる側は空のままで、タブを運び込むまで案内を出す。
    /// 上限に達していて割れなければ null（呼び出し側は割らずに済ませる）。</summary>
    internal DockLeaf? Split(DockLeaf leaf, DockAxis axis, double ratio, bool newIsSecond)
    {
        if (AllLeaves.Count() >= LayoutRules.MaxLeaves) return null;
        // DockSplit のコンストラクタは leaf.Parent をこの新しい節へ即座に付け替えるので、
        // 差し込み先を ReplaceNode に探させる（leaf.Parent を読む）前に元の親を控えておく。
        // 根を割る場合も同じで、どの窓の根だったかを先に控えておく必要がある。
        var parent = leaf.Parent;
        var host = parent is null ? HostOf(leaf) : null;
        var fresh = NewLeaf();
        var split = NewSplit(axis, newIsSecond ? leaf : fresh, newIsSecond ? fresh : leaf, ratio);
        if (parent is not null) parent.Replace(leaf, split);
        else if (host is not null) host.Root = split;
        else Root = split;
        return fresh;
    }

    /// <summary>
    /// 隣の枠を吸収して 1 つに戻す。吸収される側のタブは残る側へ移すので、結合で画面は消えない。
    /// </summary>
    internal void Join(DockLeaf survivor)
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
    }

    /// <summary>枠そのものを畳む。中のタブは隣の枠へ移す（空の枠を閉じる操作もここを通る）。
    /// 独立ウィンドウに枠が 1 つしか無ければ、畳むことはその窓ごと閉じることを意味する。</summary>
    internal void Dissolve(DockLeaf leaf)
    {
        var host = leaf.Parent is null ? HostOf(leaf) : null;
        if (LayoutRules.DissolveClosesHost(leaf.Parent is not null, host is not null))
        {
            Discard(host!);
            return;
        }
        DissolveCore(leaf);
    }

    internal void Resize(DockSplit split, double change, double total) => split.Resize(change, total);

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

    /// <summary>
    /// 割り付けと行き先の記憶を settings.json に書き戻す。独立ウィンドウの題と中身の有無もここで出し直す
    /// （窓の開け閉めはこの後で AppMediator が浮き枠の中身を見て決める）。
    /// </summary>
    internal void Persist()
    {
        // 中身も行き先の記憶も無くなった浮き枠は、覚えておく意味が無いので落とす。
        _floats.RemoveAll(f => !f.HasItems && !f.Leaves.Any(l => _homes.ContainsValue(l.Id)));
        _settings.Layout = Write(Root);
        _settings.Floats = _floats.Select(f => new DockFloatSettings
        {
            Left = double.IsNaN(f.Bounds.X) ? null : f.Bounds.X,
            Top = double.IsNaN(f.Bounds.Y) ? null : f.Bounds.Y,
            Width = f.Bounds.Width,
            Height = f.Bounds.Height,
            Node = Write(f.Root),
        }).ToList();
        _save();
        foreach (var host in _floats) host.Refresh();
    }

    private void DissolveIfEmpty(DockLeaf leaf)
    {
        if (leaf.IsEmpty && leaf.Parent is not null) DissolveCore(leaf);
    }

    private void Remember(OverlayViewModel vm, DockLeaf leaf) => _homes[vm.Kind] = leaf.Id;

    private void ReplaceNode(DockNode old, DockNode fresh)
    {
        if (old.Parent is { } parent) parent.Replace(old, fresh);
        else if (HostOf(old) is { } host) host.Root = fresh;
        else Root = fresh;
    }

    private DockLeaf NewLeaf(int? id = null)
    {
        var leaf = new DockLeaf(id ?? _nextId++);
        if (id is { } given && given >= _nextId) _nextId = given + 1;
        leaf.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(DockLeaf.Selected) || s is not DockLeaf l) return;
            SelectedChanged?.Invoke(l.Selected);
            // 独立ウィンドウの題は表に出ているタブの名前なので、選び直すたびに付け直す。
            foreach (var host in _floats) host.Refresh();
        };
        return leaf;
    }

    private DockSplit NewSplit(DockAxis axis, DockNode first, DockNode second, double ratio)
        => new(axis, first, second, ratio) { Id = _nextSplitId++ };

    /// <summary>設定が無い・壊れているときの既定。左に検索、右に単語詳細の 2 枠。</summary>
    private DockNode Fresh()
    {
        _homes.Clear();
        var search = NewLeaf();
        var detail = NewLeaf();
        _homes[nameof(SearchViewModel)] = search.Id;
        _homes[nameof(WordDetailViewModel)] = detail.Id;
        return NewSplit(DockAxis.Columns, search, detail, 0.31);
    }

    private DockNode? Build(DockNodeSettings node, int depth)
    {
        if (depth > LayoutRules.MaxDepth) return null;

        if (node.Axis is { } axis && node.First is { } first && node.Second is { } second)
        {
            var a = Build(first, depth + 1);
            var b = Build(second, depth + 1);
            if (a is null || b is null) return a ?? b;   // 片方だけ読めたらそれで代える
            return NewSplit(axis == nameof(DockAxis.Rows) ? DockAxis.Rows : DockAxis.Columns, a, b, node.Ratio);
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
