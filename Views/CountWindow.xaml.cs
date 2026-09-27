using System.ComponentModel;
using System.Windows;
using ZasDictWin.Presenters;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 単語数ウィンドウ。単語ウィンドウと同じく Owner を持たない独立した HWND。
/// 総語数は検索の絞り込みに連動しない（配信で見せるのは辞書の規模）。
/// </summary>
public partial class CountWindow : Window, IUiHost
{
    private bool _closingFromRoot;

    /// <summary>自分の OnClosing から閉じたい合図を上げている最中か。</summary>
    private bool _requestingClose;

    public CountWindow(StreamViewState state)
    {
        InitializeComponent();
        DataContext = state;
        AppRoot.Current.Attach(this, this);
        Closed += (_, _) => AppRoot.Current.Detach(this);
    }

    public Guid HostId { get; } = Guid.NewGuid();

    public HostRole Role => HostRole.Count;

    public DockNode? DockRoot => null;

    public Rect BoundsDip => new(Left, Top, Width, Height);

    public bool IsActiveHost => IsActive;

    public void CloseFromRoot()
    {
        _closingFromRoot = true;
        // 合図の裁定が同期で閉じると決めた場合は、進行中の Close をそのまま通す
        // （Closing の最中に Close() を呼ぶと WPF が InvalidOperationException を投げる）。
        if (!_requestingClose) Close();
    }

    public void FocusFromRoot() => Activate();

    public bool TryHitLeaf(Point screen, out int leafId, out Size leafSize, out Point leafLocal)
    {
        leafId = -1;
        leafSize = default;
        leafLocal = default;
        return false;
    }

    public bool ContainsScreenPoint(Point screen) => WindowHitTest.Contains(this, screen);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingFromRoot)
        {
            e.Cancel = true;
            _requestingClose = true;
            try { this.RaiseIntent(IntentKind.WindowCloseRequested); }
            finally { _requestingClose = false; }
            if (!_closingFromRoot) return;
            e.Cancel = false;
        }
        base.OnClosing(e);
    }
}
