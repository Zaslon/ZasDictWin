using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZasDictWin.Views;

/// <summary>
/// 選択肢をプルダウンで選ばせるコントロール。
///
/// ComboBox を使えない事情がふたつある。ひとつは一覧が Popup（別 HWND）になり、
/// OBS のウィンドウキャプチャに映らないこと。もうひとつは既定のダークテーマ化が効かないこと。
/// そのため一覧は自前でウィンドウ最上段の AdornerDecorator に載せる。ScrollViewer の内側にも
/// AdornerLayer があるが、そちらはビューポートで切り取られるため使わない。
///
/// 表示文字はつねに SelectedItem.ToString()。一覧に無い値でもそのまま出すので、
/// 自由入力の TextBox と組み合わせる欄（関連語の関係名）にも置ける。
/// </summary>
public class DropDown : Control
{
    /// <summary>本体と一覧のすき間。</summary>
    private const double Gap = 2;

    /// <summary>開けるのは同時にひとつ。Esc 処理（MainWindow）からも参照する。</summary>
    private static DropDown? _current;

    private Border? _panel;
    private ListBox? _list;
    private ListAdorner? _adorner;
    private AdornerLayer? _layer;
    private Window? _window;
    private ScrollViewer? _scroller;
    private bool _syncing;

    static DropDown() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DropDown), new FrameworkPropertyMetadata(typeof(DropDown)));

    public DropDown() => Unloaded += (_, _) => Close();

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(DropDown),
            new PropertyMetadata(null, (d, _) => ((DropDown)d).Close()));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(DropDown),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((DropDown)d).UpdateSelectionText()));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(DropDown),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, _) => ((DropDown)d).OnTextChanged()));

    public static readonly DependencyProperty IsEditableProperty =
        DependencyProperty.Register(nameof(IsEditable), typeof(bool), typeof(DropDown),
            new PropertyMetadata(false));

    // XAML の設定順は保証されないので、SelectedItem が先に入っていても描き直せるようにする。
    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(DropDown),
            new PropertyMetadata("", (d, _) => ((DropDown)d).UpdateSelectionText()));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(DropDown),
            new PropertyMetadata(""));

    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(nameof(MaxDropDownHeight), typeof(double), typeof(DropDown),
            new PropertyMetadata(300d));

    private static readonly DependencyPropertyKey IsOpenKey =
        DependencyProperty.RegisterReadOnly(nameof(IsOpen), typeof(bool), typeof(DropDown),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsOpenProperty = IsOpenKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectionTextKey =
        DependencyProperty.RegisterReadOnly(nameof(SelectionText), typeof(string), typeof(DropDown),
            new PropertyMetadata(""));

    public static readonly DependencyProperty SelectionTextProperty = SelectionTextKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasSelectionKey =
        DependencyProperty.RegisterReadOnly(nameof(HasSelection), typeof(bool), typeof(DropDown),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasSelectionProperty = HasSelectionKey.DependencyProperty;

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>選択値の文字列表現。SelectedItem と双方向に同期する。
    /// IsEditable のときはここに直接打ち込めるので、一覧に無い値の入り口になる。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>本体を入力欄にして、一覧に無い値も打てるようにする。
    /// 打った文字はそのまま SelectedItem になるため、選択肢が文字列の欄でのみ使う。</summary>
    public bool IsEditable
    {
        get => (bool)GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    /// <summary>選択肢が文字列でないときに、表示に使うプロパティ名（入れ子は辿らない）。
    /// 空なら ToString。ListBox の同名プロパティにもそのまま渡す。</summary>
    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>未選択のときだけ出す案内文字。</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public bool IsOpen => (bool)GetValue(IsOpenProperty);

    /// <summary>本体に描く文字。テンプレートから参照する。</summary>
    public string SelectionText => (string)GetValue(SelectionTextProperty);

    /// <summary>案内文字と選択値のどちらを出すかの判定に使う。空文字は未選択として扱う。</summary>
    public bool HasSelection => (bool)GetValue(HasSelectionProperty);

    /// <summary>開いているプルダウンがあれば閉じ、閉じたかどうかを返す。
    /// Esc がオーバーレイ全体の閉じる操作に食われないよう、ウィンドウ側の Esc 処理から先に呼ぶ。</summary>
    public static bool CloseCurrent()
    {
        if (_current is null) return false;
        _current.Close();
        return true;
    }

    public void Open()
    {
        if (IsOpen) return;
        _layer = TopLayer(this);
        if (_layer is null) return;

        _current?.Close();

        _list = new ListBox
        {
            Style = TryFindResource("DropDownList") as Style,
            ItemsSource = ItemsSource,
            SelectedItem = SelectedItem,
            DisplayMemberPath = DisplayMemberPath,
            MaxHeight = MaxDropDownHeight
        };
        _list.PreviewMouseLeftButtonUp += OnListMouseUp;
        _list.KeyDown += OnListKeyDown;

        _panel = new Border
        {
            Style = TryFindResource("DropDownPanel") as Style,
            MinWidth = ActualWidth,
            Child = _list
        };

        _adorner = new ListAdorner(this, _panel);
        _layer.Add(_adorner);

        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewMouseDown += OnWindowMouseDown;
            _window.Deactivated += OnWindowDeactivated;
        }

        // 一覧は本体に貼り付いて動くわけではないので、下地がスクロールしたら閉じる。
        _scroller = Ancestor<ScrollViewer>(this);
        if (_scroller is not null) _scroller.ScrollChanged += OnScrolled;

        SetValue(IsOpenKey, true);
        _current = this;

        // 行のコンテナは Add した直後にはまだ生成されていないため、生成後に選択行へ移す。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!IsOpen || _list is null) return;
            if (_list.SelectedItem is { } sel)
            {
                _list.ScrollIntoView(sel);
                (_list.ItemContainerGenerator.ContainerFromItem(sel) as ListBoxItem)?.Focus();
            }
            else _list.Focus();
        });
    }

    public void Close()
    {
        if (!IsOpen) return;

        if (_list is not null)
        {
            _list.PreviewMouseLeftButtonUp -= OnListMouseUp;
            _list.KeyDown -= OnListKeyDown;
        }
        if (_adorner is not null) _layer?.Remove(_adorner);
        if (_window is not null)
        {
            _window.PreviewMouseDown -= OnWindowMouseDown;
            _window.Deactivated -= OnWindowDeactivated;
        }
        if (_scroller is not null) _scroller.ScrollChanged -= OnScrolled;

        _list = null;
        _panel = null;
        _adorner = null;
        _layer = null;
        _window = null;
        _scroller = null;

        SetValue(IsOpenKey, false);
        if (_current == this) _current = null;
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

    private void UpdateSelectionText()
    {
        var text = DisplayOf(SelectedItem);
        SetValue(SelectionTextKey, text);
        SetValue(HasSelectionKey, text.Length > 0);
        Sync(() => Text = text);
    }

    private void OnTextChanged() => Sync(() => SelectedItem = Text);

    private string DisplayOf(object? item)
    {
        if (item is null) return "";
        if (DisplayMemberPath.Length == 0) return item.ToString() ?? "";
        return item.GetType().GetProperty(DisplayMemberPath)?.GetValue(item)?.ToString() ?? "";
    }

    /// <summary>Text と SelectedItem は互いを書き換えるので、片方向ずつ折り返しを止める。</summary>
    private void Sync(Action assign)
    {
        if (_syncing) return;
        _syncing = true;
        try { assign(); }
        finally { _syncing = false; }
    }

    private void Choose(object? item)
    {
        SelectedItem = item;
        Close();
        Focus();
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_list is null) return;
        var container = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container is null) return;
        Choose(_list.ItemContainerGenerator.ItemFromContainer(container));
        e.Handled = true;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                Choose(_list?.SelectedItem);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        // 本体の上なら閉じない。直後に届く MouseLeftButtonDown が開閉を切り替える。
        if (IsInside(source, _panel) || IsInside(source, this)) return;
        Close();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Close();

    private void OnScrolled(object sender, ScrollChangedEventArgs e) => Close();

    /// <summary>MenuButton も同じ「外側クリックで閉じる」判定に使うため internal にしてある。</summary>
    internal static bool IsInside(DependencyObject? node, DependencyObject? root)
    {
        if (root is null) return false;
        for (; node is not null; node = ParentOf(node))
            if (node == root) return true;
        return false;
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        for (; node is not null; node = ParentOf(node))
            if (node is T hit) return hit;
        return null;
    }

    /// <summary>テンプレート内の要素から親をたどれるよう、視覚ツリーが切れたら論理ツリーに逃がす。</summary>
    private static DependencyObject? ParentOf(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    /// <summary>ウィンドウ最上段の AdornerDecorator の層。ScrollViewer が持つ層に載せると
    /// ビューポートの外に出た部分が切り取られるため、いちばん外側まで登り切る。
    /// MenuButton も同じ層を使うため internal にしてある。</summary>
    internal static AdornerLayer? TopLayer(Visual element)
    {
        AdornerDecorator? outermost = null;
        for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is AdornerDecorator decorator) outermost = decorator;
        return outermost?.AdornerLayer;
    }

    /// <summary>一覧そのもの。Adorner は既定で被装飾要素と同じ大きさに配置されるので、
    /// 中身は本体の下（入り切らなければ上）へ自分でずらす。</summary>
    private sealed class ListAdorner : Adorner
    {
        private readonly UIElement _child;

        public ListAdorner(UIElement owner, UIElement child) : base(owner)
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
