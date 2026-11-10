using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZasDictWin.Mediator;
using ZasDictWin.Root;

namespace ZasDictWin.Views;

/// <summary>
/// 階層メニューのボタン。項目は <see cref="MenuActionSpec"/> の列で、押された項目の Intent を上げるだけ。
/// 標準の Menu／ComboBox を使わないのは DropDown と同じ理由（一覧が Popup＝別 HWND になると
/// OBS のウィンドウキャプチャに映らない）で、開いた一覧はウィンドウ最上段の AdornerLayer に描く。
/// 一覧は DropDown と同じく、開くたびに使い捨てで組み直す。開けるのは同時にひとつで、その管理は根が行う。
/// メニューバー（<see cref="IsMenuBarProperty"/> を付けた並び）に置いたものは、標準のメニューバーと同じく
/// 左右の矢印キーで隣の見出しへ移り、開いた一覧の中は上下の矢印キーとアクセスキーで選ぶ。
/// </summary>
public class MenuButton : Control, IPopupLayer
{
    /// <summary>メニューバーの並び（StackPanel 等）に付ける。中の MenuButton が隣どうしで行き来する。</summary>
    public static readonly DependencyProperty IsMenuBarProperty = DependencyProperty.RegisterAttached(
        "IsMenuBar", typeof(bool), typeof(MenuButton), new PropertyMetadata(false));

    public static bool GetIsMenuBar(DependencyObject o) => (bool)o.GetValue(IsMenuBarProperty);

    public static void SetIsMenuBar(DependencyObject o, bool value) => o.SetValue(IsMenuBarProperty, value);

    public static readonly DependencyProperty AccessKeyProperty =
        DependencyProperty.Register(nameof(AccessKey), typeof(string), typeof(MenuButton),
            new PropertyMetadata("", (d, _) => ((MenuButton)d).RaiseLabel()));

    /// <summary>メニューバーで Alt と組み合わせて開く文字。見出しに「ファイル(F)」のように添える。</summary>
    public string AccessKey { get => (string)GetValue(AccessKeyProperty); set => SetValue(AccessKeyProperty, value); }

    private static readonly DependencyPropertyKey LabelKey =
        DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(MenuButton), new PropertyMetadata(""));

    public static readonly DependencyProperty LabelProperty = LabelKey.DependencyProperty;

    /// <summary>アクセスキーを添えた見出し。</summary>
    public string Label => (string)GetValue(LabelProperty);

    private void RaiseLabel()
        => SetValue(LabelKey, string.IsNullOrEmpty(AccessKey) ? Header : $"{Header}({AccessKey})");
    private const double Gap = 2;

    private Border? _panel;
    private ItemsControl? _list;
    private MenuAdorner? _adorner;
    private AdornerLayer? _layer;
    private Window? _window;

    static MenuButton() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuButton), new FrameworkPropertyMetadata(typeof(MenuButton)));

    public MenuButton() => Unloaded += (_, _) => Close();

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(MenuButton),
            new PropertyMetadata("", (d, _) => ((MenuButton)d).RaiseLabel()));

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(MenuButton),
            new PropertyMetadata(null, (d, _) => ((MenuButton)d).Close()));

    private static readonly DependencyPropertyKey IsOpenKey =
        DependencyProperty.RegisterReadOnly(nameof(IsOpen), typeof(bool), typeof(MenuButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsOpenProperty = IsOpenKey.DependencyProperty;

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    /// <summary>並べる項目（<see cref="MenuActionSpec"/> の列）。</summary>
    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    public bool IsOpen => (bool)GetValue(IsOpenProperty);

    /// <summary>開く直前。中身を持たせる側はここで Items を詰める。
    /// 行を使い回す一覧（仮想化）に置くと、生成時に詰める作りでは中身が入らない
    /// （生成された時点のボタンには DataContextChanged が上がらない）うえ、
    /// 使い回しで別の行のものが残るため、開くたびに組み直す。</summary>
    public event EventHandler? Opening;

    public void Open() => Open(null);

    /// <summary>キーボードで開く。一覧の最初の有効な項目へフォーカスを移す。</summary>
    public void OpenWithKeyboard()
    {
        if (!IsOpen) Open(null);
        if (!IsOpen) return;
        // 項目のボタンは一覧が並べられてから生まれるので、描画の後でフォーカスを移す。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusItem(0, 1));
    }

    /// <summary><paramref name="at"/> はこのボタン基準の位置。右クリックで開くときはポインタの位置を渡す。
    /// 行の ⋯ は行の右端にあるので、⋯ の下に出すと、行の左側を右クリックしたときに一覧がポインタから遠く離れる。</summary>
    public void Open(Point? at)
    {
        if (IsOpen) return;
        Opening?.Invoke(this, EventArgs.Empty);
        // 中身が無いなら開かない。空の器だけが出ても畳む以外にできることが無い。
        if (IsEmpty(Items)) return;

        _layer = DropDown.TopLayer(this);
        if (_layer is null) return;

        _list = new ItemsControl
        {
            Style = TryFindResource("MenuActionList") as Style,
            ItemTemplate = TryFindResource("MenuActionTemplate") as DataTemplate,
            ItemsSource = Items,
            Focusable = false
        };
        // 一覧の中は上下の矢印キーで巡回する。Tab で一覧の外へ出ないよう、中で回す。
        KeyboardNavigation.SetTabNavigation(_list, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(_list, KeyboardNavigationMode.Cycle);
        _list.PreviewKeyDown += OnListKeyDown;
        // Click は既定で Handled にならず上まで届くので、押した項目をここで拾って畳む。
        // 無効な項目（IsEnabled=false）はそもそもヒットテストされず Click が上がらない。
        _list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnItemClick));

        _panel = new Border
        {
            Style = TryFindResource("DropDownPanel") as Style,
            MinWidth = ActualWidth,
            Child = _list
        };
        // 一覧はウィンドウ最上段の AdornerLayer に描くので、ヘッダに指定した文字サイズは継承されず、
        // ウィンドウ既定（本文と同じ大きさ）に戻ってしまう。項目が親のボタンより大きく見えないよう写す。
        _panel.SetValue(TextElement.FontSizeProperty, FontSize);

        _adorner = new MenuAdorner(this, _panel, at);
        _layer.Add(_adorner);

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewMouseDown += OnWindowMouseDown;
            _window.Deactivated += OnWindowDeactivated;
        }

        SetValue(IsOpenKey, true);
        // 開いたことを根に知らせる。値選択のプルダウンと同時に開かせない裁定も根が行う。
        this.RaiseIntent(IntentKind.PopupLayerOpened, this);
    }

    public void Close()
    {
        if (!IsOpen) return;

        // 一覧の中にキーボードのフォーカスがあったなら、閉じた後は見出しへ戻す（Esc で 1 段ずつ戻れるように）。
        var hadFocus = _panel?.IsKeyboardFocusWithin == true;
        if (_list is not null) _list.PreviewKeyDown -= OnListKeyDown;
        _list?.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnItemClick));
        if (_adorner is not null) _layer?.Remove(_adorner);
        if (_window is not null)
        {
            _window.PreviewMouseDown -= OnWindowMouseDown;
            _window.Deactivated -= OnWindowDeactivated;
        }

        var window = _window;
        _list = null;
        _panel = null;
        _adorner = null;
        _layer = null;
        _window = null;

        SetValue(IsOpenKey, false);
        // 閉じるのは Unloaded（窓から外れた後）のこともあるので、開いた窓から知らせる。
        ((UIElement?)window ?? this).RaiseIntent(IntentKind.PopupLayerClosed, this);
        if (hadFocus && IsLoaded) Focus();
    }

    /// <summary>メニューバーで隣の見出し。端では反対の端へ回る。</summary>
    private MenuButton? Neighbor(int step)
    {
        if (VisualTreeHelper.GetParent(this) is not Panel bar || !GetIsMenuBar(bar)) return null;
        var menus = bar.Children.OfType<MenuButton>().Where(m => m.IsVisible && m.IsEnabled).ToList();
        var index = menus.IndexOf(this);
        if (index < 0 || menus.Count < 2) return null;
        return menus[(index + step + menus.Count) % menus.Count];
    }

    private bool InMenuBar => VisualTreeHelper.GetParent(this) is Panel bar && GetIsMenuBar(bar);

    /// <summary>メニューバーで 1 つ開いている間は、ポインタを隣の見出しへ動かすだけで開き直す（標準のメニューバーと同じ）。</summary>
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (IsOpen || VisualTreeHelper.GetParent(this) is not Panel bar || !GetIsMenuBar(bar)) return;
        if (bar.Children.OfType<MenuButton>().FirstOrDefault(m => m.IsOpen) is not { } open) return;
        var keyboard = open._panel?.IsKeyboardFocusWithin == true;
        open.Close();
        if (keyboard) { Focus(); OpenWithKeyboard(); }
        else Open();
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveFocus(1);
                e.Handled = true;
                break;
            case Key.Home:
                FocusItem(0, 1);
                e.Handled = true;
                break;
            case Key.End:
                FocusItem(ItemButtons().Count - 1, -1);
                e.Handled = true;
                break;
            case Key.Left or Key.Right when InMenuBar:
                if (Neighbor(e.Key == Key.Left ? -1 : 1) is { } next)
                {
                    Close();
                    next.Focus();
                    next.OpenWithKeyboard();
                }
                e.Handled = true;
                break;
            case >= Key.A and <= Key.Z when Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift:
                var letter = (char)('A' + (e.Key - Key.A));
                var target = ItemButtons().FirstOrDefault(b => b.IsEnabled && b.DataContext is MenuActionSpec s && char.ToUpperInvariant(s.AccessKey) == letter);
                if (target is not null)
                {
                    target.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, target));
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>一覧の項目のボタン（見えているもの）。</summary>
    private List<Button> ItemButtons()
    {
        var result = new List<Button>();
        if (_list is null) return result;
        for (var i = 0; i < _list.Items.Count; i++)
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(i) is DependencyObject container
                && FindButton(container) is { IsVisible: true } button)
                result.Add(button);
        }
        return result;
    }

    private static Button? FindButton(DependencyObject node)
    {
        if (node is Button b) return b;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindButton(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    /// <summary>start から step の向きに、最初の有効な項目へフォーカスを移す。</summary>
    private void FocusItem(int start, int step)
    {
        var buttons = ItemButtons();
        for (var i = start; i >= 0 && i < buttons.Count; i += step)
        {
            if (!buttons[i].IsEnabled) continue;
            Keyboard.Focus(buttons[i]);
            return;
        }
    }

    /// <summary>上下の矢印キー。無効の項目は飛ばし、端では反対の端へ回る。</summary>
    private void MoveFocus(int step)
    {
        var buttons = ItemButtons().Where(b => b.IsEnabled).ToList();
        if (buttons.Count == 0) return;
        var current = buttons.FindIndex(b => b.IsKeyboardFocusWithin);
        var next = current < 0 ? (step > 0 ? 0 : buttons.Count - 1) : (current + step + buttons.Count) % buttons.Count;
        Keyboard.Focus(buttons[next]);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (IsOpen) Close();
        else { Focus(); Open(); }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsOpen || e.Handled) return;
        if (InMenuBar && e.Key is Key.Left or Key.Right)
        {
            Neighbor(e.Key == Key.Left ? -1 : 1)?.Focus();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Enter or Key.Space or Key.Down or Key.F4)) return;
        OpenWithKeyboard();
        e.Handled = true;
    }

    /// <summary>押された項目の Intent は一覧（AdornerLayer）からではなくこのボタンから上げる。
    /// 一覧はウィンドウ直下に描かれているので、そこから上げると行の属する枠を通らない。</summary>
    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        var spec = (e.OriginalSource as FrameworkElement)?.DataContext as MenuActionSpec;
        Close();
        // メニューバーから選んだら、メニューバーへ移る前の要素へフォーカスを返す（標準のメニューバーと同じ）。
        if (InMenuBar && IsKeyboardFocused) LeaveMenuBar();
        if (spec is not null) this.RaiseIntent(spec.Intent, spec.Payload);
    }

    /// <summary>メニューバーへ移る前にフォーカスがあった要素。Esc や項目の実行でメニューバーを出るときに戻す。</summary>
    private static WeakReference<IInputElement>? _returnFocus;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (!InMenuBar || e.NewFocus != this) return;
        // メニューバーの中や開いた一覧の項目から移ってきたときは、戻り先を上書きしない。
        var fromMenu = e.OldFocus is MenuButton { InMenuBar: true }
                       || (e.OldFocus as FrameworkElement)?.DataContext is MenuActionSpec;
        if (!fromMenu) _returnFocus = e.OldFocus is { } old ? new WeakReference<IInputElement>(old) : null;
    }

    /// <summary>メニューバーの見出しから、メニューバーへ移る前の要素へフォーカスを返す。戻り先が無ければフォーカスを外す。</summary>
    public static void LeaveMenuBar()
    {
        if (_returnFocus?.TryGetTarget(out var target) == true && target is UIElement { IsVisible: true } element && element.Focus()) return;
        Keyboard.ClearFocus();
    }

    private static bool IsEmpty(IEnumerable? items)
    {
        if (items is null) return true;
        var walker = items.GetEnumerator();
        try { return !walker.MoveNext(); }
        finally { (walker as IDisposable)?.Dispose(); }
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        // 本体の上なら閉じない。直後に届く MouseLeftButtonDown が開閉を切り替える。
        if (DropDown.IsInside(source, _panel) || DropDown.IsInside(source, this)) return;
        Close();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Close();

    /// <summary>一覧そのもの。本体の下（位置の指定があればその点）に出し、
    /// 下に入り切らなければ上へ返し、右に入り切らなければ左へ寄せて、窓の外へはみ出させない。</summary>
    private sealed class MenuAdorner : Adorner
    {
        private readonly UIElement _child;
        private readonly Point? _at;

        public MenuAdorner(UIElement owner, UIElement child, Point? at) : base(owner)
        {
            _child = child;
            _at = at;
            AddVisualChild(child);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _child;

        protected override Size MeasureOverride(Size constraint)
        {
            _child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var width = Math.Max(_at is null ? finalSize.Width : 0, _child.DesiredSize.Width);
            var height = _child.DesiredSize.Height;
            var x = _at?.X ?? 0;
            var y = _at?.Y ?? finalSize.Height + Gap;

            if (VisualTreeHelper.GetParent(this) is UIElement layer)
            {
                // 一覧は飾る要素の座標で並べるが、窓の根に UI 倍率が掛かっていると層の座標とは倍率の分だけ違う。
                var toLayer = AdornedElement.TransformToVisual(layer);
                var origin = toLayer.Transform(new Point(0, 0));
                var scale = toLayer.TransformBounds(new Rect(0, 0, 1, 1)).Width;
                var flipped = _at is { } p ? p.Y - height : -height - Gap;
                if (origin.Y + (y + height) * scale > layer.RenderSize.Height && origin.Y + flipped * scale >= 0) y = flipped;
                var overflow = (origin.X + (x + width) * scale - layer.RenderSize.Width) / scale;
                if (overflow > 0) x = Math.Max(x - overflow, -origin.X / scale);
            }

            _child.Arrange(new Rect(new Point(x, y), new Size(width, height)));
            return finalSize;
        }
    }
}
