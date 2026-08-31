using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// オーバーレイの行き先。Center は検索欄の右（単語詳細と同じ枠）で、確認ダイアログを重ねる層とは別。
/// settings.json には数値で入るので、並べ替えると記憶した行き先がずれる。
/// </summary>
public enum DockSide { Left, Right, Top, Bottom, Center }

/// <summary>
/// 行き先ひとつぶんのタブ束。同じ場所に寄せたオーバーレイはタブで切り替える（同時には 1 枚だけ見える）。
/// 大きさは場所ごとに 1 つで、タブを跨いでも変わらない。中央だけは大きさを持たず、
/// 検索欄との仕切りが決めた残りを埋める。
/// タブを掴んでいる間は、この束そのものがドロップ先になる（別に候補の升目は描かない）。
/// </summary>
public sealed class DockGroup : ViewModelBase
{
    public const double MinWidth = 320;
    public const double MaxWidth = 1000;
    public const double MinHeight = 220;
    public const double MaxHeight = 720;

    /// <summary>空の辺がドラッグ中だけ見せる受け皿の厚み。既存の枠を押し退けすぎない大きさにする。</summary>
    private const double PlaceholderWidth = 108;
    private const double PlaceholderHeight = 76;

    private OverlayViewModel? _selected;
    private double _size;
    private bool _isDropTarget;

    public DockGroup(DockSide side, double size)
    {
        Side = side;
        _size = Clamp(size);
        Items.CollectionChanged += OnItemsChanged;
        // 空の辺は普段畳まれていて掴んだタブを落とす先が無いので、ドラッグ中だけ受け皿として現れる。
        OverlayDragState.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayDragState.IsDragging)) RaiseShape();
        };
    }

    public DockSide Side { get; }

    public ObservableCollection<OverlayViewModel> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    /// <summary>今カーソルが乗っているドロップ先か。真の間だけ枠全体を着色する。</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    /// <summary>タブが 1 枚も無いまま、掴んだタブの受け皿として出ている状態か。</summary>
    public bool IsDropPlaceholder
        => !HasItems && Side != DockSide.Center && OverlayDragState.Instance.IsDragging;

    /// <summary>枠を出すか。タブが入っているか、受け皿として出ているかのどちらか。</summary>
    public bool IsShown => HasItems || IsDropPlaceholder;

    /// <summary>受け皿として出ている間の見出し。空の枠に何を落とせるのかはこれだけで伝える。</summary>
    public string PlaceholderLabel => Side switch
    {
        DockSide.Left => "左にドッキング",
        DockSide.Right => "右にドッキング",
        DockSide.Top => "上にドッキング",
        DockSide.Bottom => "下にドッキング",
        _ => "中央にドッキング",
    };

    /// <summary>左右の辺か。幅で伸縮するか高さで伸縮するかがこれで決まる。</summary>
    public bool IsHorizontal => Side is DockSide.Left or DockSide.Right;

    /// <summary>辺の大きさを設定に書き戻す。ドラッグ中は呼ばず、離した時点だけ呼ぶ。</summary>
    public Action? Persist { get; set; }

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

    public double Size => _size;

    /// <summary>
    /// 付いている向きの辺だけ大きさを固定し、反対側は NaN（＝Auto）で目一杯に広げる。
    /// 中央は両方 NaN。ここで固定すると検索欄との仕切りが効かなくなる。
    /// 受け皿として出ている間は覚えている大きさではなく、細い厚みで出す。
    /// </summary>
    public double Width => Side is DockSide.Left or DockSide.Right
        ? (HasItems ? _size : PlaceholderWidth)
        : double.NaN;

    public double Height => Side is DockSide.Top or DockSide.Bottom
        ? (HasItems ? _size : PlaceholderHeight)
        : double.NaN;

    /// <summary>
    /// 大きさを変えるつまみを持つか。中央は仕切りが別にあるので持たず、
    /// 受け皿として出ているだけの空の枠も、伸縮させても意味が無いので持たない。
    /// </summary>
    public bool HasGrip => Side != DockSide.Center && HasItems;

    /// <summary>大きさを変えるつまみは辺の内側に置く。</summary>
    public Dock GripDock => Side switch
    {
        DockSide.Left => Dock.Right,
        DockSide.Right => Dock.Left,
        DockSide.Top => Dock.Bottom,
        _ => Dock.Top,
    };

    public double GripWidth => IsHorizontal ? 5 : double.NaN;

    public double GripHeight => IsHorizontal ? double.NaN : 5;

    public Cursor GripCursor => IsHorizontal ? Cursors.SizeWE : Cursors.SizeNS;

    /// <summary>つまみのドラッグ量を大きさに反映する。付いている辺と反対の向きに広がる。</summary>
    public void Resize(double horizontalChange, double verticalChange)
    {
        var next = Clamp(Side switch
        {
            DockSide.Left => _size + horizontalChange,
            DockSide.Right => _size - horizontalChange,
            DockSide.Top => _size + verticalChange,
            _ => _size - verticalChange,
        });
        if (Math.Abs(next - _size) < 0.01) return;
        _size = next;
        Raise(nameof(Size));
        Raise(nameof(Width));
        Raise(nameof(Height));
    }

    private double Clamp(double value) => IsHorizontal
        ? Math.Clamp(value, MinWidth, MaxWidth)
        : Math.Clamp(value, MinHeight, MaxHeight);

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Raise(nameof(HasItems));
        RaiseShape();
        // 選択していたタブが消えたら、残っているうち一番新しいものに移る。
        if (Selected is null || !Items.Contains(Selected)) Selected = Items.LastOrDefault();
    }

    /// <summary>枠を出すかどうかと、その厚みに関わるものをまとめて配り直す。</summary>
    private void RaiseShape()
    {
        Raise(nameof(IsDropPlaceholder));
        Raise(nameof(IsShown));
        Raise(nameof(HasGrip));
        Raise(nameof(Width));
        Raise(nameof(Height));
    }
}

/// <summary>
/// 中央と 4 辺ぶんのタブ束と、種類ごとの行き先の記憶。オーバーレイは種類ごとに 1 枚までなので、
/// 「前にどこへ置いたか」を種類名で覚えておけば次に開いたときも同じ場所に出せる。
/// </summary>
public sealed class DockGroups
{
    private readonly AppSettings _settings;

    public DockGroups(AppSettings settings)
    {
        _settings = settings;
        Left = Make(DockSide.Left, settings.DockLeftWidth);
        Right = Make(DockSide.Right, settings.DockRightWidth);
        Top = Make(DockSide.Top, settings.DockTopHeight);
        Bottom = Make(DockSide.Bottom, settings.DockBottomHeight);
        // 中央の大きさは Grid の列と仕切りが決めるので、ここで渡す値は使われない。
        Center = Make(DockSide.Center, 0);
    }

    public DockGroup Left { get; }
    public DockGroup Right { get; }
    public DockGroup Top { get; }
    public DockGroup Bottom { get; }

    /// <summary>検索欄の右。単語詳細が据え置きのタブとして必ず 1 枚入っている。</summary>
    public DockGroup Center { get; }

    public IReadOnlyList<DockGroup> All => new[] { Left, Right, Top, Bottom, Center };

    public DockGroup this[DockSide side] => side switch
    {
        DockSide.Left => Left,
        DockSide.Top => Top,
        DockSide.Bottom => Bottom,
        DockSide.Center => Center,
        _ => Right,
    };

    public IEnumerable<OverlayViewModel> Overlays => All.SelectMany(g => g.Items);

    public void Add(OverlayViewModel vm)
    {
        // 覚えていない種類は右に出す。検索と一覧を潰さない側で、既定のレイアウトに一番馴染む。
        vm.Side = _settings.OverlayDocks.TryGetValue(vm.Kind, out var side) ? side : DockSide.Right;
        var group = this[vm.Side];
        group.Items.Add(vm);
        group.Selected = vm;
    }

    /// <summary>閉じずに中央へ据え置くタブ。行き先を設定に覚えさせず、常に先頭へ置く。</summary>
    public void Pin(OverlayViewModel vm)
    {
        vm.Side = DockSide.Center;
        Center.Items.Insert(0, vm);
    }

    public void Remove(OverlayViewModel vm) => this[vm.Side].Items.Remove(vm);

    public void Move(OverlayViewModel vm, DockSide side)
    {
        if (vm.Side == side) return;
        this[vm.Side].Items.Remove(vm);
        vm.Side = side;
        var group = this[side];
        group.Items.Add(vm);
        group.Selected = vm;
        _settings.OverlayDocks[vm.Kind] = side;
        _settings.Save();
    }

    private void SaveSizes()
    {
        _settings.DockLeftWidth = Left.Size;
        _settings.DockRightWidth = Right.Size;
        _settings.DockTopHeight = Top.Size;
        _settings.DockBottomHeight = Bottom.Size;
        _settings.Save();
    }

    private DockGroup Make(DockSide side, double size)
        => new(side, size) { Persist = SaveSizes };
}

/// <summary>
/// タブを掴んでドロップ先を選んでいる間だけの状態。オーバーレイ側の DataContext は MainViewModel では
/// ないため、FontScaleState などと同じく {x:Static} で引ける singleton にしてある。
/// ドロップ先は画面に描いた候補の升目ではなく、実際に並んでいる枠（<see cref="DockGroup"/>）そのもの。
/// 実際に動かすのは <see cref="Move"/>（MainViewModel が差し込む）。
/// </summary>
public sealed class OverlayDragState : ViewModelBase
{
    public static OverlayDragState Instance { get; } = new();

    private bool _isDragging;
    private DockGroup? _hoverGroup;

    public Action<OverlayViewModel, DockSide>? Move { get; set; }

    public OverlayViewModel? Dragged { get; private set; }

    /// <summary>ドラッグ中か。真の間だけ空の辺が受け皿として現れる。</summary>
    public bool IsDragging
    {
        get => _isDragging;
        private set => Set(ref _isDragging, value);
    }

    /// <summary>
    /// 今カーソルが乗っている枠。乗っている枠が着色され、離せばそこへ移る。
    /// どの枠にも乗っていなければ null で、離しても動かさない。
    /// </summary>
    public DockGroup? HoverGroup
    {
        get => _hoverGroup;
        set
        {
            var previous = _hoverGroup;
            if (!Set(ref _hoverGroup, value)) return;
            if (previous is not null) previous.IsDropTarget = false;
            if (value is not null) value.IsDropTarget = true;
        }
    }

    public void BeginDrag(OverlayViewModel vm)
    {
        Dragged = vm;
        HoverGroup = null;
        IsDragging = true;
    }

    /// <summary>乗っている枠へ移す。枠の外で離したときは何もしない。</summary>
    public void CompleteDrag()
    {
        var vm = Dragged;
        var side = HoverGroup?.Side;
        Cancel();
        if (vm is not null && side is { } target) Move?.Invoke(vm, target);
    }

    public void Cancel()
    {
        // 着色を先に解く。IsDragging を落とすと空の受け皿が畳まれ、着色したまま消えてしまう。
        HoverGroup = null;
        IsDragging = false;
        Dragged = null;
    }
}
