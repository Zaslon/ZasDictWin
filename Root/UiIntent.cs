using System.Windows;
using System.Windows.Controls.Primitives;

namespace ZasDictWin.Root;

/// <summary>枠の四隅のどれを掴んだか。引いた向きから割り方を決めるのに使う。</summary>
public enum AreaCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Root へ届く入力の種別。裁定は AppMediator が行い、View はどれを上げるかだけを知る。</summary>
public enum IntentKind
{
    // ---- ポインタ（ドラッグの入口。掴んだ要素がキャプチャを持つ）----
    AreaGripPressed, TabGripPressed, RowGripPressed, SplitGripPressed,
    PointerMoved, PointerReleased, PointerCaptureLost,

    // ---- キー・ホイール ----
    CancelRequested, ZoomFontRequested,

    // ---- タブと枠 ----
    TabSelectRequested, TabCloseRequested, AreaCloseRequested,

    // ---- 画面を開く・前に出す ----
    OpenScreenRequested, FocusScreenRequested,

    // ---- 辞書 ----
    OpenDictionaryRequested, NewDictionaryRequested, SaveRequested, SaveAsRequested,

    // ---- 単語 ----
    NewWordRequested, EditWordRequested, DuplicateWordRequested, DeleteWordRequested,
    SelectWordRequested, FollowRelationRequested, WordCommitted, WordEditCancelled,

    // ---- 例文 ----
    ShowExamplesRequested, NewExampleRequested, EditExampleRequested,
    DeleteExampleRequested, ExampleCommitted, ExampleEditCancelled,
    ExampleOfferFetchRequested, ZpdicApiKeySaveRequested,

    // ---- 検索 ----
    QueryChanged, SearchModeChanged, SearchScopeChanged, ClearQueryRequested,
    FocusResultListRequested, FocusQueryBoxRequested,

    // ---- ブラウザ ----
    BrowserNavigateRequested, BrowserBackRequested, BrowserForwardRequested, BrowserReloadRequested,

    // ---- 更新履歴 ----
    ChangelogExportRequested, ChangelogRelinkRequested,

    // ---- GitHub ----
    GitHubLoadRequested, GitHubCommitRequested, GitHubCommitConfirmed,
    GitHubTokenSaveRequested, GitHubTokenDeleteRequested,

    // ---- モーダル（確認ダイアログ）----
    ConfirmChoiceSelected, ModalDismissRequested,

    // ---- 設定 ----
    SettingsRequested, SettingsApplyRequested, PickHeksaFontRequested,
    SettingsModeSelected, SettingsReciprocalResetRequested,

    // ---- 編集フォームの行（書きかけの入れ物を触るだけ。辞書へは保存まで書かない）----
    TranslationRowAddRequested, TranslationRowRemoveRequested,
    ContentRowAddRequested, ContentRowRemoveRequested,
    VariationRowAddRequested, VariationRowRemoveRequested,
    RelationRowAddRequested, RelationRowRemoveRequested,
    ExampleWordAddRequested, ExampleWordRemoveRequested,

    // ---- ウィンドウ ----
    WindowCloseRequested, WindowBoundsChanged, WindowActivated, WindowStateChanged,
    StreamWindowToggleRequested, CountWindowToggleRequested,

    // ---- AdornerDecorator に描く一覧（DropDown / MenuButton）----
    PopupLayerOpened, PopupLayerClosed,

    // ---- 一覧の行メニューを開く前（項目の構成を Mediator に作らせる）----
    RowMenuOpening,

    // ---- 例外 ----
    UnhandledExceptionRaised,
}

/// <summary>
/// Intent が根へ上がる途中で、通過したコンポーネントが「自分しか知らない事実」を書き込む入れ物。
/// 判断は一切せず、Mediator が裁定に使う材料だけを足す（Chain of Responsibility の
/// 「処理できなければ上へ送る」に相当。この実装では必ず上へ送り、裁定は根で 1 回だけ行う）。
/// </summary>
public sealed class IntentContext
{
    public int? LeafId { get; set; }
    public int? SplitId { get; set; }
    /// <summary>タブの種類（OverlayViewModel.Kind）。どのタブを運んでいるかの識別子。</summary>
    public string? TabKind { get; set; }
    public Guid? HostId { get; set; }
    public AreaCorner? Corner { get; set; }
    /// <summary>枠の実寸。座標から比率への換算は Mediator が行う。</summary>
    public Size? LeafSize { get; set; }
    /// <summary>枠を基準にしたポインタ位置。</summary>
    public Point? LeafLocalPoint { get; set; }
    /// <summary>境目のつまみの移動量と、割り付け全体の実寸。</summary>
    public double? DragChange { get; set; }
    public double? DragTotal { get; set; }
    /// <summary>一覧の行の並べ替え。落とし先の行番号は View が算出して渡す（行の実寸は View しか知らない）。</summary>
    public int? RowIndex { get; set; }
}

public sealed class UiIntentEventArgs : RoutedEventArgs
{
    public UiIntentEventArgs(IntentKind kind, object? payload = null)
        : base(UiIntent.UiIntentEvent) { Kind = kind; Payload = payload; }

    public IntentKind Kind { get; }
    public object? Payload { get; }
    /// <summary>画面上のポインタ位置（デバイスピクセル）。ポインタ系 Intent だけが持つ。</summary>
    public Point? ScreenPoint { get; set; }
    public IntentContext Context { get; } = new();
}

public static class UiIntent
{
    public static readonly RoutedEvent UiIntentEvent = EventManager.RegisterRoutedEvent(
        "UiIntent", RoutingStrategy.Bubble,
        typeof(EventHandler<UiIntentEventArgs>), typeof(UiIntent));

    /// <summary>Passive View が持つ唯一の「動作」。判断はせず、上へ送るだけ。</summary>
    public static void RaiseIntent(this UIElement source, IntentKind kind, object? payload = null)
        => source.RaiseEvent(new UiIntentEventArgs(kind, payload));

    /// <summary>画面上の位置を伴う Intent（ドラッグ）。</summary>
    public static void RaiseIntent(this UIElement source, IntentKind kind, Point screenPoint, object? payload = null)
        => source.RaiseEvent(new UiIntentEventArgs(kind, payload) { ScreenPoint = screenPoint });

    /// <summary>IntentContext を先に埋めたい場合はこちら。</summary>
    public static void RaiseIntent(this UIElement source, UiIntentEventArgs args) => source.RaiseEvent(args);

    public static void AddIntentHandler(this UIElement element, EventHandler<UiIntentEventArgs> handler)
        => element.AddHandler(UiIntentEvent, handler);

    public static void RemoveIntentHandler(this UIElement element, EventHandler<UiIntentEventArgs> handler)
        => element.RemoveHandler(UiIntentEvent, handler);
}

/// <summary>
/// XAML から Intent を上げるための添付プロパティ。ButtonBase.Click を拾って RaiseIntent する。
/// ボタンから判断を持つ相手（ICommand）へ直結させないための手段で、プロジェクト内でこれ 1 つに統一する。
/// </summary>
public static class IntentTrigger
{
    public static readonly DependencyProperty OnClickProperty = DependencyProperty.RegisterAttached(
        "OnClick", typeof(IntentKind?), typeof(IntentTrigger), new PropertyMetadata(null, OnOnClickChanged));

    public static readonly DependencyProperty PayloadProperty = DependencyProperty.RegisterAttached(
        "Payload", typeof(object), typeof(IntentTrigger), new PropertyMetadata(null));

    public static void SetOnClick(DependencyObject o, IntentKind? value) => o.SetValue(OnClickProperty, value);

    public static IntentKind? GetOnClick(DependencyObject o) => (IntentKind?)o.GetValue(OnClickProperty);

    public static void SetPayload(DependencyObject o, object? value) => o.SetValue(PayloadProperty, value);

    public static object? GetPayload(DependencyObject o) => o.GetValue(PayloadProperty);

    private static void OnOnClickChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ButtonBase button) return;
        button.Click -= OnClick;
        if (e.NewValue is not null) button.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || GetOnClick(button) is not { } kind) return;
        button.RaiseIntent(kind, GetPayload(button));
    }
}
