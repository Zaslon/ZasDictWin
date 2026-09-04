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

    public DockNode Root
    {
        get => _root;
        internal set
        {
            value.Parent = null;
            Set(ref _root, value);
        }
    }

    /// <summary>窓の位置と大きさ（DIP）。窓を動かす・大きさを変えるたびにビューが書き戻す。</summary>
    public Rect Bounds { get; set; }

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
/// 分割・結合・タブの移動はここだけが行い、そのたびに settings.json へ書き戻す。
/// </summary>
public sealed class DockLayout : ViewModelBase
{
    private const int MaxDepth = 8;
    private const int MaxLeaves = 24;

    private readonly AppSettings _settings;

    /// <summary>種類名 → 前に置いた枠の番号。閉じたタブの行き先もここで覚えておく。</summary>
    private readonly Dictionary<string, int> _homes = new();

    private readonly List<DockFloat> _floats = new();

    private DockNode _root;
    private int _nextId = 1;
    private bool _notifying;

    public DockLayout(AppSettings settings)
    {
        _settings = settings;
        _root = _settings.Layout is { } saved ? Build(saved, 0) ?? Fresh() : Fresh();
        _root.Owner = this;
        foreach (var host in _settings.Floats)
        {
            if (host.Node is not { } node || Build(node, 0) is not { } built) continue;
            built.Owner = this;
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

    /// <summary>タブが表に出た（開いた・選び直した）ことの通知。Esc の行き先を追うのに使う。</summary>
    public event Action<OverlayViewModel?>? Touched;

    /// <summary>独立ウィンドウの増減・中身の変化。実際の窓の開け閉めはビュー（MainWindow）が受け持つ。</summary>
    public event Action? FloatsChanged;

    public DockLeaf? LeafOf(OverlayViewModel vm) => AllLeaves.FirstOrDefault(l => l.Items.Contains(vm));

    /// <summary>そのタブが独立ウィンドウにいるなら、その窓ぶんの浮き枠。本体にいれば null。</summary>
    public DockFloat? FloatOf(OverlayViewModel vm) => _floats.FirstOrDefault(f => f.Items.Contains(vm));

    /// <summary>その枠が独立ウィンドウの根なら、その窓ぶんの浮き枠。</summary>
    private DockFloat? HostOf(DockNode node) => _floats.FirstOrDefault(f => ReferenceEquals(f.Root, node));

    /// <summary>覚えている枠、無ければ既定の枠にタブを足して表に出す。
    /// 行き先を覚えていない種類のうち独立ウィンドウ向きのもの（ツール類）は、新しい窓を 1 枚こしらえて出す。</summary>
    public void Add(OverlayViewModel vm)
    {
        if (_homes.TryGetValue(vm.Kind, out var id) && AllLeaves.FirstOrDefault(l => l.Id == id) is { } home)
        {
            Place(vm, home);
            return;
        }
        if (vm.PrefersFloating && Float(vm, null) is not null) return;
        Place(vm, Main);
    }

    private void Place(OverlayViewModel vm, DockLeaf leaf)
    {
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

    /// <summary>
    /// タブを窓の外へ持ち出す。新しい独立ウィンドウを 1 枚こしらえ、そこへ移す。
    /// <paramref name="at"/> は窓の左上に置きたい位置（DIP）で、null なら本体の中央に出す。
    /// すでに 1 枚きりの独立ウィンドウにいるタブは、外へ落としても同じ窓が生まれ直すだけなので動かさない。
    /// </summary>
    public DockFloat? Float(OverlayViewModel vm, Point? at)
    {
        if (AllLeaves.Count() >= MaxLeaves) return null;
        var source = LeafOf(vm);
        if (source is not null && source.Items.Count == 1 && ReferenceEquals(HostOf(source)?.Root, source)) return null;

        var leaf = NewLeaf();
        var size = vm.FloatSize;
        var host = new DockFloat(leaf, new Rect(at?.X ?? double.NaN, at?.Y ?? double.NaN, size.Width, size.Height));
        _floats.Add(host);
        source?.Items.Remove(vm);
        leaf.Items.Add(vm);
        leaf.Selected = vm;
        Remember(vm, leaf);
        if (source is not null) DissolveIfEmpty(source);
        Save();
        return host;
    }

    /// <summary>独立ウィンドウを割り付けから外す。窓を手で閉じたときに、中身を始末した後で呼ぶ。</summary>
    public void Discard(DockFloat host)
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

    /// <summary>枠を 2 つに割る。新しくできる側は空のままで、タブを運び込むまで案内を出す。
    /// 上限に達していて割れなければ null（呼び出し側は割らずに済ませる）。</summary>
    public DockLeaf? Split(DockLeaf leaf, DockAxis axis, double ratio, bool newIsSecond)
    {
        if (AllLeaves.Count() >= MaxLeaves) return null;
        // DockSplit のコンストラクタは leaf.Parent をこの新しい節へ即座に付け替えるので、
        // 差し込み先を ReplaceNode に探させる（leaf.Parent を読む）前に元の親を控えておく。
        // 根を割る場合も同じで、どの窓の根だったかを先に控えておく必要がある。
        var parent = leaf.Parent;
        var host = parent is null ? HostOf(leaf) : null;
        var fresh = NewLeaf();
        var split = newIsSecond
            ? new DockSplit(axis, leaf, fresh, ratio)
            : new DockSplit(axis, fresh, leaf, ratio);
        split.Owner = this;
        fresh.Owner = this;
        if (parent is not null) parent.Replace(leaf, split);
        else if (host is not null) host.Root = split;
        else Root = split;
        Save();
        return fresh;
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

    /// <summary>枠そのものを畳む。中のタブは隣の枠へ移す（空の枠を閉じる操作もここを通る）。
    /// 独立ウィンドウに枠が 1 つしか無ければ、畳むことはその窓ごと閉じることを意味する。</summary>
    public void Dissolve(DockLeaf leaf)
    {
        if (leaf.Parent is null && HostOf(leaf) is { } host)
        {
            Discard(host);
            return;
        }
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

    /// <summary>
    /// 割り付けと行き先の記憶を settings.json に書き戻す。組み替えはすべてここを通るので、
    /// 独立ウィンドウの開け閉めを促す通知もここから出す。
    /// </summary>
    public void Save()
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
        _settings.Save();
        NotifyFloats();
    }

    /// <summary>浮き枠の題と中身の有無を出し直す。窓の開け閉めはこの通知を受けたビューが行う。</summary>
    private void NotifyFloats()
    {
        // 窓を閉じると中のタブが動き、そこからまた Save が呼ばれる。入れ子の通知は 1 度目に任せる。
        if (_notifying) return;
        _notifying = true;
        try
        {
            foreach (var host in _floats.ToList()) host.Refresh();
            FloatsChanged?.Invoke();
        }
        finally
        {
            _notifying = false;
        }
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
        var leaf = new DockLeaf(id ?? _nextId++) { Owner = this };
        if (id is { } given && given >= _nextId) _nextId = given + 1;
        leaf.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(DockLeaf.Selected) || s is not DockLeaf l) return;
            Touched?.Invoke(l.Selected);
            // 独立ウィンドウの題は表に出ているタブの名前なので、選び直すたびに付け直す。
            foreach (var host in _floats) host.Refresh();
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
    private SplitPreview? _hoverSplit;
    private OverlayViewModel? _dragged;
    private DockLeaf? _sourceLeaf;
    private Point? _outside;

    public Action<OverlayViewModel, DockLeaf>? Move { get; set; }

    /// <summary>どの窓にも乗っていない位置で離したときの行き先。独立ウィンドウを 1 枚こしらえる。</summary>
    public Action<OverlayViewModel, Point>? FloatOut { get; set; }

    /// <summary>今カーソルが乗っている枠。どの枠にも乗っていなければ null。</summary>
    public DockLeaf? HoverLeaf => _hoverLeaf;

    /// <summary><paramref name="sourceLeaf"/> は運び出した元の枠。分割の下見はそこの端だけで出す
    /// （よそへ乗せたときは、その辺で新しく割るのではなく、そのままタブとして合流させる）。</summary>
    public void BeginDrag(OverlayViewModel vm, DockLeaf? sourceLeaf)
    {
        _dragged = vm;
        _sourceLeaf = sourceLeaf;
        SetHover(null, null);
    }

    /// <summary>
    /// ドラッグ中のカーソル位置を更新する。運び出した元の枠の端に寄せていれば分割の下見（<paramref name="split"/>）を、
    /// それ以外（よその枠、または元の枠の真ん中）は枠全体の着色（タブとして合流）を出す。
    /// 前に乗っていた枠の下見・着色はここで消す。
    /// </summary>
    public void SetHover(DockLeaf? leaf, SplitPreview? split)
    {
        // 枠に乗せ直したら「窓の外」は取り消し。下見が変わらなくても必ず消す（先に落とす）。
        _outside = null;
        // 元の枠にこのタブしか無ければ、割ってすぐ運び出したところで空になった元の枠が
        // 畳まれて元通りになるだけ（見た目は変わらず、枠番号だけ振り直る）なので下見を出さない。
        var sourceHasOthers = _sourceLeaf is { } source && source.Items.Count > 1;
        split = leaf is null || !ReferenceEquals(leaf, _sourceLeaf) || !sourceHasOthers ? null : split;
        if (ReferenceEquals(leaf, _hoverLeaf) && Equals(split, _hoverSplit)) return;

        if (_hoverLeaf is not null)
        {
            _hoverLeaf.IsDropTarget = false;
            _hoverLeaf.Preview = null;
        }
        _hoverLeaf = leaf;
        _hoverSplit = split;
        if (leaf is null) return;
        if (split is not null) leaf.Preview = split;
        else leaf.IsDropTarget = true;
    }

    /// <summary>
    /// アプリのどの窓にも乗っていないカーソル位置。ここで離せば独立ウィンドウになる。
    /// <paramref name="at"/> は画面上の位置（DIP）。乗っていた枠の下見・着色はここで消える。
    /// </summary>
    public void SetOutside(Point at)
    {
        SetHover(null, null);
        _outside = at;
    }

    /// <summary>
    /// 乗っている枠へ移す。端に寄せていたら先にそちら側を割ってから、できた新しい枠へ移す
    /// （上限で割れなければ、これまで通りその枠へタブとして合流させる）。
    /// どの窓にも乗っていなければ独立ウィンドウにする。窓の中で枠を外して離したときだけ何もしない。
    /// </summary>
    public void CompleteDrag()
    {
        var vm = _dragged;
        var leaf = _hoverLeaf;
        var split = _hoverSplit;
        var outside = _outside;
        Cancel();
        if (vm is null) return;
        if (leaf is null)
        {
            if (outside is { } at) FloatOut?.Invoke(vm, at);
            return;
        }

        var target = split is not null && leaf.Owner is { } owner
            ? owner.Split(leaf, split.Axis, split.Ratio, split.NewIsSecond) ?? leaf
            : leaf;
        Move?.Invoke(vm, target);
    }

    public void Cancel()
    {
        SetHover(null, null);
        _dragged = null;
        _sourceLeaf = null;
    }
}
