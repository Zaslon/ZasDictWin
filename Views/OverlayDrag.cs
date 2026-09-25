using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 「ここで離すと独立ウィンドウになる」ことを示す、カーソルに付いてくる小さな影。
/// 窓の外には枠が無く着色する相手がいないので、分割・結合と同じ「離す前に結果が分かる」を
/// これで賄う。掴みを取られないよう、出しても前に出ない窓（ShowActivated=false）にしてある。
/// 枠の当たり判定は Root に登録した窓（本体と独立ウィンドウ）だけを見るので、この窓は邪魔をしない。
/// </summary>
internal sealed class DragGhost : Window
{
    public DragGhost(string title)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0.9;

        var scale = FontScaleState.Instance.Scale;
        var stack = new StackPanel();
        // タブの見出しは UI 文字列（検索・設定など）なので、枠側の見出し（DockGroupPanel.xaml）と
        // 同じく [en] を解いて描く。
        var heading = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 12 * scale,
            Foreground = Brush("Text"),
        };
        LocalizedText.SetText(heading, title);
        stack.Children.Add(heading);
        var hint = new TextBlock
        {
            FontSize = 11 * scale,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Brush("Muted"),
        };
        LocalizedText.SetText(hint, Strings.Overlay_DropToFloatHint);
        stack.Children.Add(hint);

        // 影は本体と同じ書体で出す（コードで組むので、XAML の既定は効かない）。
        FontFamily = new FontFamily("Yu Gothic UI");
        Content = new Border
        {
            Background = Brush("Raised"),
            BorderBrush = Brush("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = stack,
        };
    }

    /// <summary>カーソルの右下に添える。<paramref name="at"/> は画面上の位置（DIP）。</summary>
    public void MoveTo(Point at)
    {
        Left = at.X + 14;
        Top = at.Y + 16;
    }

    private static Brush Brush(string key)
        => Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>
/// タブを掴んで別の枠へ運ばせる添付ビヘイビア。付ける相手の DataContext が
/// <see cref="OverlayViewModel"/> であることが前提（タブの見出しに付ける）。
/// 運び先は窓をまたげる。本体の窓でも独立ウィンドウでも同じように落とせて、
/// アプリのどの窓にも乗っていない場所で離すとそのタブが独立ウィンドウになる。
/// ここは掴みの取得・解放と Intent の発火だけを受け持ち、当たり判定と行き先の判断は根（AppMediator）が行う。
/// 落とす枠が無いときは、先に角を引いて枠を増やす（<see cref="AreaDrag"/>）。
/// </summary>
public static class OverlayDrag
{
    public static readonly DependencyProperty IsGripProperty = DependencyProperty.RegisterAttached(
        "IsGrip", typeof(bool), typeof(OverlayDrag), new PropertyMetadata(false, OnIsGripChanged));

    public static void SetIsGrip(DependencyObject o, bool value) => o.SetValue(IsGripProperty, value);

    public static bool GetIsGrip(DependencyObject o) => (bool)o.GetValue(IsGripProperty);

    private static void OnIsGripChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;

        element.MouseLeftButtonDown -= OnMouseDown;
        element.MouseMove -= OnMouseMove;
        element.MouseLeftButtonUp -= OnMouseUp;
        element.LostMouseCapture -= OnLostCapture;

        // 掴めなくなった要素に「動かせる」カーソルを残さない。
        if (e.NewValue is not true)
        {
            element.ClearValue(FrameworkElement.CursorProperty);
            return;
        }

        // タブに載せた［✕］は Cursor=Hand を自前で持つので、ここで掴める見た目にしても潰さない。
        element.Cursor = Cursors.SizeAll;
        element.MouseLeftButtonDown += OnMouseDown;
        element.MouseMove += OnMouseMove;
        element.MouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostCapture;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        // タブに載っている［✕］が先に処理した押下は掴みに使わない。
        if (e.Handled || sender is not FrameworkElement { DataContext: OverlayViewModel vm } element) return;
        var args = new UiIntentEventArgs(IntentKind.TabGripPressed) { ScreenPoint = PointerCapture.ScreenOf(element, e) };
        args.Context.TabKind = vm.Kind;
        element.RaiseIntent(args);
        element.CaptureMouse();
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { IsMouseCaptured: true } element) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var screen = PointerCapture.ScreenOf(element, e);
        // 窓の外で離したときの置き場所と影の位置は DIP で要る。換算は掴んでいる窓の拡大率でしかできない。
        element.RaiseIntent(IntentKind.PointerMoved, screen, PointerCapture.ToDip(element, screen));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { IsMouseCaptured: true } element) return;
        var screen = PointerCapture.ScreenOf(element, e);
        PointerCapture.Release(element);
        element.RaiseIntent(IntentKind.PointerReleased, screen);
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element || PointerCapture.IsReleasing(element)) return;
        element.RaiseIntent(IntentKind.PointerCaptureLost);
    }
}
