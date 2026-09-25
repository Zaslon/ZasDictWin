using System.ComponentModel;
using System.Windows;
using ZasDictWin.Presenters;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 単語ウィンドウ。Owner を持たない独立した HWND で、OBS で個別のキャプチャソースに選ばせる。
/// テーマを当てないのは背景色がクロマキー用の固定値になるから。
/// </summary>
public partial class StreamWindow : Window, IUiHost
{
    private bool _closingFromRoot;

    public StreamWindow(StreamViewState state)
    {
        InitializeComponent();
        DataContext = state;
        AppRoot.Current.Attach(this, this);
        Closed += (_, _) => AppRoot.Current.Detach(this);
    }

    public Guid HostId { get; } = Guid.NewGuid();

    public HostRole Role => HostRole.Stream;

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
            // ［ウィンドウ］メニューの開く／閉じるの表示を合わせるため、閉じるのも根の裁定に回す。
            e.Cancel = true;
            this.RaiseIntent(IntentKind.WindowCloseRequested);
            return;
        }
        base.OnClosing(e);
    }
}
