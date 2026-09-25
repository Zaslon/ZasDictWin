using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Media;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>確認ダイアログの選択肢 1 つ。押されたら <see cref="Index"/> を ConfirmChoiceSelected で返す。</summary>
public sealed record ChoiceItem(string Label, bool IsDanger, int Index);

/// <summary>確認ダイアログ用の汎用オーバーレイ。中央固定のカードで、答えるまで先へ進めない問いに使う。</summary>
public sealed class ChoiceViewModel : OverlayViewModel
{
    public ChoiceViewModel(ConfirmSpec spec)
    {
        Title = spec.Title;
        Message = spec.Message;
        Choices = new ObservableCollection<ChoiceItem>(spec.Choices.Select((c, i) => new ChoiceItem(c.Label, c.IsDanger, i)));
    }

    public string Message { get; }

    public ObservableCollection<ChoiceItem> Choices { get; }
}

/// <summary>
/// 中央に据え置く単語詳細のタブ。選択中の単語や参照例文は MainViewModel がそのまま持つので、
/// ここはタブ束に並ぶための器で、中身は <see cref="Main"/> を DataContext にして描く。
/// </summary>
public sealed class WordDetailViewModel : OverlayViewModel
{
    public WordDetailViewModel(MainViewModel main)
    {
        Main = main;
        Title = Strings.Detail_Title;
    }

    public MainViewModel Main { get; }
}

/// <summary>
/// 検索と一覧のタブ。単語詳細と同じく本体（<see cref="Main"/>）を DataContext にして描く器で、
/// 検索条件も一覧の中身も MainViewModel がそのまま持つ。閉じられないが、枠は自由に選べる。
/// </summary>
public sealed class SearchViewModel : OverlayViewModel
{
    public SearchViewModel(MainViewModel main)
    {
        Main = main;
        Title = Strings.Search_Title;
    }

    public MainViewModel Main { get; }
}

/// <summary>
/// ブラウザ（WebView2）のタブ。中身は <see cref="Browser"/> が持ち、ここは枠に並べるための器。
/// 窓全体を覆う確認ダイアログの間だけ中身を隠す（airspace で WebView2 が上に出てしまうため）。
/// </summary>
public sealed class BrowserTabViewModel : OverlayViewModel
{
    public BrowserTabViewModel(MainViewModel main, BrowserViewModel browser)
    {
        Main = main;
        Browser = browser;
        Title = Strings.Browser_Title;
        Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBrowserShown)) Raise(nameof(IsContentShown));
        };
    }

    public MainViewModel Main { get; }

    public BrowserViewModel Browser { get; }

    /// <summary>中身（WebView2）を出してよいか。確認ダイアログが出ている間は隠す。</summary>
    public bool IsContentShown => Main.IsBrowserShown;
}

/// <summary>
/// 設定ウィンドウ（SettingsWindow）の入力欄の入れ物。タブ束にもオーバーレイ層にも入らない。
/// 開いた時点の設定値を写し取っておき、［適用］で AppMediator がまとめて書き戻す。
/// </summary>
public sealed class SettingsViewModel : OverlayViewModel
{
    public SettingsViewModel(AppSettings settings, OtmDocument? doc,
                             IEnumerable<KeyValuePair<string, string>> relations, bool hasGitHubToken)
    {
        Title = Strings.Settings_Title;

        Language = settings.Language;
        SortOrder = settings.SortOrder;
        FontScale = settings.FontScale;
        AutoSave = settings.AutoSave;
        WindowWidth = settings.WindowWidth;
        WindowHeight = settings.WindowHeight;
        WindowAspectLocked = settings.WindowAspectLocked;
        HeksaEnabled = settings.HeksaEnabled;
        HeksaFontPath = settings.HeksaFontPath ?? "";
        ReciprocalText = FormatReciprocal(relations);

        _mode = settings.Mode;
        GitHubOwner = settings.GitHubOwner ?? "";
        GitHubRepo = settings.GitHubRepo ?? "";
        GitHubBranch = string.IsNullOrWhiteSpace(settings.GitHubBranch) ? "main" : settings.GitHubBranch;
        GitHubJsonPath = settings.GitHubJsonPath ?? "";
        GitHubChangelogPath = settings.GitHubChangelogPath ?? "";
        _hasGitHubToken = hasGitHubToken;

        StreamBackground = settings.StreamBackground;
        StreamFontScale = settings.StreamFontScale;
        StreamTopmost = settings.StreamWindowTopmost;
        StreamShowTranslations = settings.StreamShowTranslations;
        StreamShowContents = settings.StreamShowContents;

        BrowserVisible = settings.BrowserVisible;
        BrowserStartUrl = settings.BrowserStartUrl;

        HasDictionary = doc is not null;
        if (doc is not null)
        {
            Punctuations = string.Concat(
                (doc.ZpdicOnline["punctuations"] as JsonArray)?
                    .Select(n => n?.GetValue<string>() ?? "") ?? Array.Empty<string>());
            IgnoredPattern = doc.ZpdicOnline["ignoredPattern"]?.GetValue<string>() ?? "";
        }
    }

    private static string FormatReciprocal(IEnumerable<KeyValuePair<string, string>> map)
        => string.Join(Environment.NewLine, map.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>［既定に戻す］。対照表の入力欄だけを既定値で書き直す（適用するまで保存はしない）。</summary>
    internal void ResetReciprocal() => ReciprocalText = FormatReciprocal(RelationService.DefaultMap);

    private string _language = Languages.Default;

    /// <summary>表示言語コード（Languages.All のいずれか）。x:Static で参照する UI 文字列は
    /// 再起動しないと切り替わらない（Strings.cs 参照）ため、ここで選んでもすぐには反映しない。
    /// プルダウンの選択表示は Mode（編集モード）と同じく PropertyChanged で追随させる必要があるので、
    /// 素の自動プロパティにしない。</summary>
    public string Language { get => _language; set => Set(ref _language, value); }

    /// <summary>設定画面の言語プルダウンの選択肢。対応言語を増やすには Languages.All を編集する。</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages => Languages.All;

    public string SortOrder { get; set; }
    public double FontScale { get; set; }
    public bool AutoSave { get; set; }

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowAspectLocked { get; set; }

    private bool _heksaEnabled;
    public bool HeksaEnabled
    {
        get => _heksaEnabled;
        set { if (Set(ref _heksaEnabled, value)) RaisePreview(); }
    }

    private string _heksaFontPath = "";
    public string HeksaFontPath
    {
        get => _heksaFontPath;
        set
        {
            if (!Set(ref _heksaFontPath, value)) return;
            _previewFont = HeadwordFontState.Load(value);
            RaisePreview();
        }
    }

    private FontFamily? _previewFont;

    /// <summary>［適用］前でも選んだ ttf をその場で確かめられるように、入力中のパスから読み直す。</summary>
    public FontFamily PreviewFontFamily => _previewFont ?? HeadwordFontState.Fallback;
    public bool HasPreviewFont => HeksaEnabled && _previewFont is not null;
    public bool IsFontMissing => HeksaEnabled && _previewFont is null;

    private void RaisePreview()
    {
        Raise(nameof(PreviewFontFamily));
        Raise(nameof(HasPreviewFont));
        Raise(nameof(IsFontMissing));
    }

    private string _reciprocalText = "";
    public string ReciprocalText { get => _reciprocalText; set => Set(ref _reciprocalText, value); }

    public string Punctuations { get; set; } = "";
    public string IgnoredPattern { get; set; } = "";
    public bool HasDictionary { get; }

    private string _streamBackground = "";

    /// <summary>
    /// 単語ウィンドウの背景色。隣の見本（Border.Background）が同じ値を見ているため、
    /// 素の自動プロパティにすると打っている最中に見本が追随しない。
    /// </summary>
    public string StreamBackground
    {
        get => _streamBackground;
        set => Set(ref _streamBackground, value);
    }

    public double StreamFontScale { get; set; }
    public bool StreamTopmost { get; set; }
    public bool StreamShowTranslations { get; set; }
    public bool StreamShowContents { get; set; }

    public bool BrowserVisible { get; set; }
    public string BrowserStartUrl { get; set; } = "";

    // ---- GitHub モード ----------------------------------------------------

    private EditMode _mode;
    public EditMode Mode
    {
        get => _mode;
        internal set
        {
            if (!Set(ref _mode, value)) return;
            Raise(nameof(IsGitHubMode));
        }
    }

    public bool IsGitHubMode => Mode == EditMode.GitHub;

    public string GitHubOwner { get; set; } = "";
    public string GitHubRepo { get; set; } = "";
    public string GitHubBranch { get; set; } = "main";
    public string GitHubJsonPath { get; set; } = "";
    public string GitHubChangelogPath { get; set; } = "";

    private string _gitHubTokenInput = "";
    /// <summary>トークンの入力欄。保存すると空に戻す（画面にキーを残さない）。</summary>
    public string GitHubTokenInput
    {
        get => _gitHubTokenInput;
        set { if (Set(ref _gitHubTokenInput, value)) Raise(nameof(CanSaveGitHubToken)); }
    }

    public bool CanSaveGitHubToken => GitHubTokenInput.Trim().Length > 0;

    private bool _hasGitHubToken;
    public bool HasGitHubToken
    {
        get => _hasGitHubToken;
        internal set { if (Set(ref _hasGitHubToken, value)) Raise(nameof(GitHubTokenHint)); }
    }

    public string GitHubTokenHint => HasGitHubToken
        ? string.Format(Strings.GitHub_TokenHintSaved, GitHubApi.TokenPath)
        : string.Format(Strings.GitHub_TokenHintMissing, GitHubApi.TokenPath);

    private string _gitHubTokenStatus = "";
    public string GitHubTokenStatus { get => _gitHubTokenStatus; internal set => Set(ref _gitHubTokenStatus, value); }
}

/// <summary>
/// GitHubへコミットする前の確認。コミットメッセージは保留中の更新履歴から自動生成した既定値を
/// 出すが、送信直前まで自由に書き換えられる。中央のモーダルとして出す。
/// </summary>
public sealed class CommitViewModel : OverlayViewModel
{
    private string _message;

    public CommitViewModel(string summary, string defaultMessage)
    {
        Title = Strings.GitHub_CommitDialogTitle;
        Summary = summary;
        DefaultMessage = defaultMessage;
        _message = defaultMessage;
    }

    /// <summary>今回コミットに含める更新の一覧（保留中の変更が無ければその旨の案内）。</summary>
    public string Summary { get; }

    /// <summary>メッセージ欄を空にして送ったときに代わりに使う文面。</summary>
    public string DefaultMessage { get; }

    public string Message { get => _message; set => Set(ref _message, value); }
}
