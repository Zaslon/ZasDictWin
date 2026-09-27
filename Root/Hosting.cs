using System.Windows;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Root;

public enum HostRole { Shell, Floating, Settings, Stream, Count }

/// <summary>
/// Root 配下に置かれるウィンドウ。WPF は Window ごとに Visual Tree が分かれるため、
/// 論理的な単一 Root はこの契約で束ねる（別 HWND であること自体が OBS 前提の仕様）。
/// </summary>
public interface IUiHost
{
    Guid HostId { get; }
    HostRole Role { get; }
    /// <summary>この窓が描く割り付けの根。Shell と Floating 以外は null。</summary>
    DockNode? DockRoot { get; }
    /// <summary>Root の裁定で閉じる（利用者が閉じたのではない閉じ方）。</summary>
    void CloseFromRoot();
    void FocusFromRoot();
    /// <summary>画面上の位置（デバイスピクセル）がこの窓の枠に乗っているか。
    /// 窓には乗っているが枠の外（ヘッダ・フッタ）なら false を返し、leafId は -1。
    /// tabStripHeight は枠の上端に並ぶタブ列の高さ（無ければ 0）、tabSlot はタブ列の上にいるときの
    /// 差し込み位置（何番目のタブの手前か。末尾なら並びの数。タブ列の外なら -1）。</summary>
    bool TryHitLeaf(Point screen, out int leafId, out Size leafSize, out Point leafLocal, out double tabStripHeight, out int tabSlot);
    /// <summary>画面上の位置がこの窓の中か（枠に乗っていなくても真）。
    /// まだ描かれていない（HWND を持たない）窓は常に false。</summary>
    bool ContainsScreenPoint(Point screen);
    /// <summary>窓の位置と大きさ（DIP）。</summary>
    Rect BoundsDip { get; }
    bool IsActiveHost { get; }
}

public readonly record struct HitLeaf(
    IUiHost Host, int LeafId, Size LeafSize, Point LeafLocal, double TabStripHeight = 0, int TabSlot = -1)
{
    public bool HasLeaf => LeafId >= 0;
}

/// <summary>Mediator の裁定を受けて View 層が実行する副作用。</summary>
public abstract record HostCommand
{
    public sealed record OpenFloating(DockFloat Host) : HostCommand;
    public sealed record CloseHost(Guid HostId) : HostCommand;
    public sealed record FocusHost(Guid HostId) : HostCommand;
    public sealed record OpenSettings(SettingsViewModel State) : HostCommand;
    public sealed record ToggleStream(bool Open) : HostCommand;
    public sealed record ToggleCount(bool Open) : HostCommand;
    /// <summary>AspectRatio が null なら比率固定を解除する。Width / Height が NaN なら大きさは変えない。
    /// Maximize は起動時の復元で最大化するときだけ真。</summary>
    public sealed record ApplyShellSize(double Width, double Height, double? AspectRatio, bool Maximize = false) : HostCommand;
    public sealed record ShowDragGhost(string Title, Point AtDip) : HostCommand;
    public sealed record HideDragGhost : HostCommand;
    /// <summary>AdornerDecorator の一覧が開いた／閉じた。WebView2 は airspace で手前に出るため隠す。</summary>
    public sealed record SetPopupLayerOpen(bool Open) : HostCommand;
    /// <summary>開いている一覧（DropDown / MenuButton）を畳む。</summary>
    public sealed record ClosePopupLayer : HostCommand;
    /// <summary>検索欄 ⇄ 結果一覧のフォーカス移動。WPF の API 制約で View にしか実行できない。</summary>
    public sealed record MoveFocus(FocusTarget Target) : HostCommand;
    /// <summary>配信用ウィンドウの設定（背景色・倍率・最前面）を張り直す。</summary>
    public sealed record RefreshStreamHosts : HostCommand;
    /// <summary>ステータス文言が変わったことを一瞬光らせて知らせる（Storyboard の再生）。</summary>
    public sealed record FlashStatus : HostCommand;
}

public enum FocusTarget { QueryBox, ResultList, WordEditForm }

public interface IHostFactory
{
    IUiHost OpenFloating(DockFloat host);
    IUiHost OpenSettings(SettingsViewModel state);
    IUiHost OpenStream();
    IUiHost OpenCount();
    void Close(IUiHost host);
}
