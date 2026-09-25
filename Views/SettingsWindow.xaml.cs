using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 設定ポップアップ。中身はタブ束に混ぜず中央モーダルとして 1 枚だけ出す方針は変わらないが、
/// 器は本体（MainWindow）とは別の独立ウィンドウにしてある。閉じ方（キャンセル・適用・×・Esc）は
/// すべて Intent にして根へ上げ、実際に閉じるのは根の裁定（CloseFromRoot）。
/// </summary>
public partial class SettingsWindow : Window, IUiHost
{
    private bool _closingFromRoot;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        AppRoot.Current.Attach(this, this);
        Closed += (_, _) => AppRoot.Current.Detach(this);
    }

    // ---- IUiHost ----------------------------------------------------------------

    public Guid HostId { get; } = Guid.NewGuid();

    public HostRole Role => HostRole.Settings;

    public DockNode? DockRoot => null;

    public Rect BoundsDip => new(Left, Top, Width, Height);

    public bool IsActiveHost => IsActive;

    public void CloseFromRoot()
    {
        _closingFromRoot = true;
        Close();
    }

    public void FocusFromRoot() => Activate();

    // 枠を持たないので、タブの運び先の当たり判定には入らない。
    public bool TryHitLeaf(Point screen, out int leafId, out Size leafSize, out Point leafLocal)
    {
        leafId = -1;
        leafSize = default;
        leafLocal = default;
        return false;
    }

    public bool ContainsScreenPoint(Point screen) => WindowHitTest.Contains(this, screen);

    // ---- 入力 → Intent -----------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        this.RaiseIntent(IntentKind.CancelRequested);
        e.Handled = true;
    }

    // 文字サイズ倍率はアプリ全体で 1 つなので、この窓で回しても本体と同じ値を動かす。
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        this.RaiseIntent(IntentKind.ZoomFontRequested, Math.Sign(e.Delta));
        e.Handled = true;
    }

    // 標準の枠が無いので、ヘッダの余白をドラッグでの移動に使う（MainWindow.Header_MouseLeftButtonDown と同じ理由）。
    // 設定は大きさが決め打ちの用途ではないため最大化トグルは持たせていない。
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Close_Click(object sender, RoutedEventArgs e) => this.RaiseIntent(IntentKind.WindowCloseRequested);

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
