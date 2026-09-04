using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// 階層メニューの 1 項目。呼び出し側（MainWindow）がコマンドを直接詰めて使う想定で、
/// XAML の Binding には頼らない（DataContext 継承が届かない場所に置かれても確実に動くように）。
/// </summary>
public sealed class MenuAction : DependencyObject
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(MenuAction), new PropertyMetadata(""));

    public static readonly DependencyProperty ToolTipProperty =
        DependencyProperty.Register(nameof(ToolTip), typeof(string), typeof(MenuAction), new PropertyMetadata(""));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(MenuAction));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(MenuAction));

    public static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.Register(nameof(IsVisible), typeof(bool), typeof(MenuAction), new PropertyMetadata(true));

    /// <summary>一覧の中でもとくに主用途の項目（保存など）を強調する。</summary>
    public static readonly DependencyProperty IsPrimaryProperty =
        DependencyProperty.Register(nameof(IsPrimary), typeof(bool), typeof(MenuAction), new PropertyMetadata(false));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string ToolTip { get => (string)GetValue(ToolTipProperty); set => SetValue(ToolTipProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public bool IsVisible { get => (bool)GetValue(IsVisibleProperty); set => SetValue(IsVisibleProperty, value); }
    public bool IsPrimary { get => (bool)GetValue(IsPrimaryProperty); set => SetValue(IsPrimaryProperty, value); }
}

/// <summary>
/// コマンドを並べた階層メニューのボタン。標準の Menu／ComboBox を使わないのは DropDown と同じ理由
/// （一覧が Popup＝別 HWND になると OBS のウィンドウキャプチャに映らない）で、開いた一覧は
/// ウィンドウ最上段の AdornerLayer に描く。一覧は DropDown と同じく、開くたびに使い捨てで組み直す。
/// </summary>
public class MenuButton : Control
{
    private const double Gap = 2;

    /// <summary>開けるのは同時にひとつ。Esc 処理（MainWindow）からも参照する。</summary>
    private static MenuButton? _current;

    private Border? _panel;
    private ItemsControl? _list;
    private MenuAdorner? _adorner;
    private AdornerLayer? _layer;
    private Window? _window;

    static MenuButton() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MenuButton), new FrameworkPropertyMetadata(typeof(MenuButton)));

    public MenuButton() => Unloaded += (_, _) => Close();

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(MenuButton), new PropertyMetadata(""));

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(MenuButton),
            new PropertyMetadata(null, (d, _) => ((MenuButton)d).Close()));

    private static readonly DependencyPropertyKey IsOpenKey =
        DependencyProperty.RegisterReadOnly(nameof(IsOpen), typeof(bool), typeof(MenuButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsOpenProperty = IsOpenKey.DependencyProperty;

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    public bool IsOpen => (bool)GetValue(IsOpenProperty);

    /// <summary>開く直前。中身を持たせる側はここで Items を詰める。
    /// 行を使い回す一覧（仮想化）に置くと、生成時に詰める作りでは中身が入らない
    /// （生成された時点のボタンには DataContextChanged が上がらない）うえ、
    /// 使い回しで別の行のものが残るため、開くたびに組み直す。</summary>
    public event EventHandler? Opening;

    /// <summary>開いているメニューがあれば閉じ、閉じたかどうかを返す。Esc がオーバーレイ全体の
    /// 閉じる操作に食われないよう、ウィンドウ側の Esc 処理から先に呼ぶ。</summary>
    public static bool CloseCurrent()
    {
        if (_current is null) return false;
        _current.Close();
        return true;
    }

    public void Open()
    {
        if (IsOpen) return;
        Opening?.Invoke(this, EventArgs.Empty);
        // 中身が無いなら開かない。空の器だけが出ても畳む以外にできることが無い。
        if (IsEmpty(Items)) return;

        _layer = DropDown.TopLayer(this);
        if (_layer is null) return;

        // 値選択のプルダウンと階層メニューは見た目も役割も別だが、同時に開いていると紛らわしいので
        // どちらか一方だけにする。
        DropDown.CloseCurrent();
        _current?.Close();

        _list = new ItemsControl
        {
            Style = TryFindResource("MenuActionList") as Style,
            ItemTemplate = TryFindResource("MenuActionTemplate") as DataTemplate,
            ItemsSource = Items
        };
        // Click は既定で Handled にならず上まで届くので、押した項目を実行させたあとここで畳む。
        // 無効な項目（CanExecute=false）はそもそもヒットテストされず Click が上がらないので、
        // ここで一律に閉じてよい。
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

        _adorner = new MenuAdorner(this, _panel);
        _layer.Add(_adorner);

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewMouseDown += OnWindowMouseDown;
            _window.Deactivated += OnWindowDeactivated;
        }

        SetValue(IsOpenKey, true);
        _current = this;
        DropDown.EnterOverlay();
    }

    public void Close()
    {
        if (!IsOpen) return;

        _list?.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnItemClick));
        if (_adorner is not null) _layer?.Remove(_adorner);
        if (_window is not null)
        {
            _window.PreviewMouseDown -= OnWindowMouseDown;
            _window.Deactivated -= OnWindowDeactivated;
        }

        _list = null;
        _panel = null;
        _adorner = null;
        _layer = null;
        _window = null;

        SetValue(IsOpenKey, false);
        if (_current == this) _current = null;
        DropDown.ExitOverlay();
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
        if (e.Key is not (Key.Enter or Key.Space or Key.Down or Key.F4)) return;
        Open();
        e.Handled = true;
    }

    private void OnItemClick(object sender, RoutedEventArgs e) => Close();

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

    /// <summary>一覧そのもの。配置ロジックは DropDown.ListAdorner と同じ
    /// （本体の下、入り切らなければ上へ）。</summary>
    private sealed class MenuAdorner : Adorner
    {
        private readonly UIElement _child;

        public MenuAdorner(UIElement owner, UIElement child) : base(owner)
        {
            _child = child;
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
            var size = _child.DesiredSize;
            var y = finalSize.Height + Gap;

            if (VisualTreeHelper.GetParent(this) is UIElement layer)
            {
                var top = AdornedElement.TranslatePoint(new Point(0, 0), layer).Y;
                if (top + y + size.Height > layer.RenderSize.Height && top - size.Height - Gap >= 0)
                    y = -size.Height - Gap;
            }

            _child.Arrange(new Rect(new Point(0, y), new Size(Math.Max(finalSize.Width, size.Width), size.Height)));
            return finalSize;
        }
    }
}
