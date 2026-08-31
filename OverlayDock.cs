using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>オーバーレイの居場所。Floating は画面中央に重ねるモーダル表示。</summary>
public enum DockSide { Floating, Left, Right, Top, Bottom }

/// <summary>
/// ドッキング先の辺と大きさの共有状態。オーバーレイの DataContext は MainViewModel ではないため、
/// FontScaleState などと同じく {x:Static} で引ける singleton にしてある。
/// 設定ファイルへの書き戻しは <see cref="Persist"/> 経由で MainViewModel に任せる。
/// </summary>
public sealed class OverlayDockState : ViewModelBase
{
    public const double MinWidth = 320;
    public const double MaxWidth = 1000;
    public const double MinHeight = 220;
    public const double MaxHeight = 720;

    public static OverlayDockState Instance { get; } = new();

    private DockSide _side = DockSide.Floating;
    private double _width = 520;
    private double _height = 340;
    private bool _isDragging;
    private DockSide _hoverSide = DockSide.Floating;

    /// <summary>辺と大きさを settings.json に書き戻す。ドラッグ中は呼ばず、確定時だけ呼ぶ。</summary>
    public Action? Persist { get; set; }

    public DockSide Side
    {
        get => _side;
        private set
        {
            if (!Set(ref _side, value)) return;
            Raise(nameof(IsDocked));
            Raise(nameof(PanelDock));
            Raise(nameof(GripDock));
            Raise(nameof(GripCursor));
            RaiseSize();
        }
    }

    public bool IsDocked => Side != DockSide.Floating;

    /// <summary>ドッキングした本体を DockPanel のどちら側に付けるか。</summary>
    public Dock PanelDock => Side switch
    {
        DockSide.Left => Dock.Left,
        DockSide.Top => Dock.Top,
        DockSide.Bottom => Dock.Bottom,
        _ => Dock.Right,
    };

    /// <summary>大きさを変えるつまみは本体の内側の辺に置く。</summary>
    public Dock GripDock => Side switch
    {
        DockSide.Left => Dock.Right,
        DockSide.Top => Dock.Bottom,
        DockSide.Bottom => Dock.Top,
        _ => Dock.Left,
    };

    public Cursor GripCursor => Side is DockSide.Top or DockSide.Bottom ? Cursors.SizeNS : Cursors.SizeWE;

    /// <summary>左右ドッキングのときだけ幅を固定する。上下では NaN（＝Auto）を返して横いっぱいに広げる。</summary>
    public double PanelWidth => Side is DockSide.Left or DockSide.Right ? _width : double.NaN;

    public double PanelHeight => Side is DockSide.Top or DockSide.Bottom ? _height : double.NaN;

    public double GripWidth => Side is DockSide.Left or DockSide.Right ? 5 : double.NaN;

    public double GripHeight => Side is DockSide.Top or DockSide.Bottom ? 5 : double.NaN;

    /// <summary>ドラッグ中か。真の間だけドロップ先の候補を描く。</summary>
    public bool IsDragging
    {
        get => _isDragging;
        private set => Set(ref _isDragging, value);
    }

    /// <summary>今カーソルが乗っているドロップ先。離せばここに収まる。</summary>
    public DockSide HoverSide
    {
        get => _hoverSide;
        set => Set(ref _hoverSide, value);
    }

    public void BeginDrag()
    {
        HoverSide = Side;
        IsDragging = true;
    }

    public void CompleteDrag(DockSide side)
    {
        IsDragging = false;
        Side = side;
        Persist?.Invoke();
    }

    public void CancelDrag() => IsDragging = false;

    /// <summary>つまみのドラッグ量を大きさに反映する。付いている辺と反対の向きに広がる。</summary>
    public void Resize(double horizontalChange, double verticalChange)
    {
        switch (Side)
        {
            case DockSide.Left: _width = Math.Clamp(_width + horizontalChange, MinWidth, MaxWidth); break;
            case DockSide.Right: _width = Math.Clamp(_width - horizontalChange, MinWidth, MaxWidth); break;
            case DockSide.Top: _height = Math.Clamp(_height + verticalChange, MinHeight, MaxHeight); break;
            case DockSide.Bottom: _height = Math.Clamp(_height - verticalChange, MinHeight, MaxHeight); break;
            default: return;
        }
        RaiseSize();
    }

    public void Restore(AppSettings settings)
    {
        _width = Math.Clamp(settings.OverlayDockWidth, MinWidth, MaxWidth);
        _height = Math.Clamp(settings.OverlayDockHeight, MinHeight, MaxHeight);
        Side = settings.OverlayDock;
        RaiseSize();
    }

    public void SaveTo(AppSettings settings)
    {
        settings.OverlayDock = Side;
        settings.OverlayDockWidth = _width;
        settings.OverlayDockHeight = _height;
    }

    private void RaiseSize()
    {
        Raise(nameof(PanelWidth));
        Raise(nameof(PanelHeight));
        Raise(nameof(GripWidth));
        Raise(nameof(GripHeight));
    }
}
