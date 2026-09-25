using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// ブラウザのタブ（WebView2）の描画パラメータ。実際の描画と履歴は View 側の WebView2 が持つため、
/// ここではアドレス・履歴状態・開閉だけを受け持つ。ナビゲーション指示は AppMediator が
/// Request* を呼び、イベントで View に渡す（WebView2 は 1 つのパネルに閉じているので直に届ける）。
/// 大きさは枠の割り付けが決めるので持たない。
/// </summary>
public sealed class BrowserViewModel : ViewModelBase
{
    /// <summary>設定が空のときの開始ページ。</summary>
    public const string FallbackStartUrl = "https://www.google.com/";

    private readonly AppSettings _settings;

    private bool _isOpen;
    private bool _isOverlayOpen;
    private string _address = "";
    private string _title = "";
    private string _status = "";
    private string _errorText = "";
    private bool _isBusy;
    private bool _canGoBack;
    private bool _canGoForward;

    public BrowserViewModel(AppSettings settings)
    {
        _settings = settings;
        _isOpen = settings.BrowserVisible;
        _address = StartUrl;
    }

    /// <summary>View 側の WebView2 への指示。初期化前の要求は View 側で保持して実行する。</summary>
    public event Action? InitializeRequested;
    public event Action<string>? NavigateRequested;
    public event Action? BackRequested;
    public event Action? ForwardRequested;
    public event Action? ReloadRequested;

    internal void RequestInitialize() => InitializeRequested?.Invoke();
    internal void RequestNavigate(string url) => NavigateRequested?.Invoke(url);
    internal void RequestBack() => BackRequested?.Invoke();
    internal void RequestForward() => ForwardRequested?.Invoke();
    internal void RequestReload() => ReloadRequested?.Invoke();

    /// <summary>設定された開始 URL。空なら既定の検索ページ。</summary>
    public string StartUrl => string.IsNullOrWhiteSpace(_settings.BrowserStartUrl)
        ? FallbackStartUrl
        : _settings.BrowserStartUrl.Trim();

    /// <summary>タブが開いているか。開閉と、次の起動で開き直すかどうかの記憶は AppMediator が書く。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        internal set => Set(ref _isOpen, value);
    }

    /// <summary>プルダウンや階層メニューの一覧が開いているか。真の間 View は WebView2 を隠す
    /// （airspace で WebView2 が WPF 描画より手前に出るため、一覧を重ねても隠れない）。</summary>
    public bool IsOverlayOpen
    {
        get => _isOverlayOpen;
        internal set => Set(ref _isOverlayOpen, value);
    }

    /// <summary>アドレス欄の編集値。View のナビゲーションでも更新される。</summary>
    public string Address { get => _address; set => Set(ref _address, value); }

    public string Title { get => _title; private set => Set(ref _title, value); }
    public bool HasTitle => !string.IsNullOrEmpty(_title);

    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>WebView2 が使えないときのエラー文言。</summary>
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    public bool HasError => !string.IsNullOrEmpty(_errorText);

    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool CanGoBack { get => _canGoBack; private set => Set(ref _canGoBack, value); }
    public bool CanGoForward { get => _canGoForward; private set => Set(ref _canGoForward, value); }

    /// <summary>設定ダイアログの適用時。表示中のページは切り替えず、開始 URL だけ反映する。</summary>
    internal void SyncWithSettings()
    {
        if (!IsOpen) Address = StartUrl;
    }

    // ---- View からの状態報告 ------------------------------------------------

    public void ReportAddress(string? url)
    {
        if (!string.IsNullOrEmpty(url)) Address = url;
    }

    public void ReportTitle(string? title)
    {
        Title = title ?? "";
        Raise(nameof(HasTitle));
    }

    public void ReportStatus(string text) => Status = text;

    /// <summary>エラー文言を設定（null でクリア）して表示可否を更新する。</summary>
    public void ReportError(string? message)
    {
        ErrorText = message ?? "";
        Raise(nameof(HasError));
    }

    public void ReportBusy(bool busy) => IsBusy = busy;

    /// <summary>CoreWebView2 には履歴変化のイベントが無いため、ナビゲーションの節目で呼ばれる。</summary>
    public void ReportHistoryState(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }
}
