using System.Windows.Input;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// ブラウザのタブ（WebView2）の状態。実際の描画と履歴は View 側の WebView2 が持つため、
/// ここではアドレス・履歴状態・開閉だけを受け持つ。ナビゲーション指示はイベントで View に渡す
/// （OverlayViewModel.RequestClose と同じ流儀）。大きさは枠の割り付けが決めるので持たない。
/// </summary>
public sealed class BrowserViewModel : ViewModelBase
{
    private const string GoogleSearch = "https://www.google.com/search?q=";

    /// <summary>設定が空のときの開始ページ。</summary>
    public const string FallbackStartUrl = "https://www.google.com/";

    private readonly AppSettings _settings;

    private bool _isOpen;
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

        NavigateCommand = new RelayCommand(Navigate);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(), () => CanGoBack);
        ForwardCommand = new RelayCommand(() => ForwardRequested?.Invoke(), () => CanGoForward);
        ReloadCommand = new RelayCommand(() => ReloadRequested?.Invoke());
    }

    public ICommand NavigateCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand ReloadCommand { get; }

    /// <summary>View 側の WebView2 への指示。初期化前の要求は View 側で保持して実行する。</summary>
    public event Action? InitializeRequested;
    public event Action<string>? NavigateRequested;
    public event Action? BackRequested;
    public event Action? ForwardRequested;
    public event Action? ReloadRequested;

    /// <summary>設定された開始 URL。空なら既定の検索ページ。</summary>
    public string StartUrl => string.IsNullOrWhiteSpace(_settings.BrowserStartUrl)
        ? FallbackStartUrl
        : _settings.BrowserStartUrl.Trim();

    /// <summary>タブが開いているか。開閉そのものは MainViewModel が行い、ここは記憶だけ持つ。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (!Set(ref _isOpen, value)) return;
            _settings.BrowserVisible = value;
            _settings.Save();
            Raise(nameof(ToggleLabel));
        }
    }

    public string ToggleLabel => IsOpen ? "ブラウザを閉じる" : "ブラウザ";

    /// <summary>タブを開いたときに呼ぶ。初回はここで WebView2 の初期化と最初のページ表示が走る
    /// （起動を重くしないため遅延させている）。</summary>
    public void Activate()
    {
        IsOpen = true;
        InitializeRequested?.Invoke();
    }

    /// <summary>タブを閉じたときに呼ぶ。次の起動で開き直すかどうかの記憶だけを落とす。</summary>
    public void Deactivate() => IsOpen = false;

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

    private void Navigate()
    {
        var url = NormalizeInput(Address);
        Address = url;
        NavigateRequested?.Invoke(url);
    }

    /// <summary>アドレス欄の文字を URL に寄せる。scheme 無しは https、語句だけなら検索にする。</summary>
    public static string NormalizeInput(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return FallbackStartUrl;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return s;

        // 「動詞 変換」のような語句と「dict.example.com」を区別する。
        if (s.Any(char.IsWhiteSpace) || (!s.Contains('.') && !s.Contains('/')))
            return GoogleSearch + Uri.EscapeDataString(s);

        return "https://" + s;
    }

    /// <summary>設定ダイアログの適用時。表示中のページは切り替えず、開始 URL だけ反映する。</summary>
    public void SyncWithSettings()
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
