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
        Close();
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
            this.RaiseIntent(IntentKind.WindowCloseRequested);
            return;
        }
        base.OnClosing(e);
    }
}
