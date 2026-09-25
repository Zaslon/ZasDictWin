using System.Collections.ObjectModel;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// 本体の窓（と検索・単語詳細のタブ）の描画パラメータの入れ物。書き込むのは AppMediator と Presenter だけで、
/// View はここを読むか、入力欄の値を TwoWay で預けるだけ（判断を持たない）。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private OtmDocument? _document;
    private Word? _selected;
    private string _query = "";
    private SearchMode _mode = SearchMode.Forward;
    private SearchScope _scope = SearchScope.Both;
    private bool _isDirty;
    private OverlayViewModel? _modal;
    private string _status = Strings.Main_OpenDictionaryPrompt;
    private bool _isGitHubBusy;
    private bool _isGitHubSynced;

    /// <param name="saveSettings">設定を書き出す手段。既定は settings.json への保存。</param>
    public MainViewModel(AppSettings settings, Action? saveSettings = null)
    {
        Settings = settings;
        Browser = new BrowserViewModel(settings);

        // タブの開閉は確認ダイアログとの排他表示（IsBrowserShown）に効く。
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserViewModel.IsOpen)) Raise(nameof(IsBrowserShown));
        };

        Layout = new DockLayout(settings, saveSettings);
    }

    public AppSettings Settings { get; }

    /// <summary>ブラウザのタブ（WebView2）の状態。</summary>
    public BrowserViewModel Browser { get; }

    /// <summary>画面の割り付け。オーバーレイはここのどれかの枠に入って初めて画面に出る。</summary>
    public DockLayout Layout { get; }

    /// <summary>開いている辞書。表示用に持つだけで、読み書きは AppMediator が行う。</summary>
    public OtmDocument? Document
    {
        get => _document;
        internal set
        {
            if (!Set(ref _document, value)) return;
            Raise(nameof(AllWords));
            Raise(nameof(WindowTitle));
            Raise(nameof(DictionaryName));
            RaiseCounts();
        }
    }

    public ObservableCollection<Word> FilteredWords { get; } = new();

    public ObservableCollection<Word> AllWords => _document?.Words ?? EmptyWords;

    private static readonly ObservableCollection<Word> EmptyWords = new();

    /// <summary>選択中の単語。一覧（ListBox）が TwoWay で書き込み、選び直しは SelectWordRequested で AppMediator に伝わる。</summary>
    public Word? SelectedWord
    {
        get => _selected;
        set { if (Set(ref _selected, value)) Raise(nameof(HasSelection)); }
    }

    public bool HasSelection => SelectedWord is not null;

    /// <summary>選択中の単語を参照している例文。詳細欄の「参照例文」に出す。</summary>
    public ObservableCollection<Example> RelatedExamples { get; } = new();

    /// <summary>検索欄の文字列。絞り込みは QueryChanged を受けた AppMediator が行う。</summary>
    public string Query
    {
        get => _query;
        set => Set(ref _query, value);
    }

    public SearchMode SearchMode
    {
        get => _mode;
        internal set => Set(ref _mode, value);
    }

    public SearchScope SearchScope
    {
        get => _scope;
        internal set => Set(ref _scope, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        internal set { if (Set(ref _isDirty, value)) Raise(nameof(WindowTitle)); }
    }

    public string WindowTitle
    {
        get
        {
            var name = _document?.Name ?? Strings.Main_NoDictionaryName;
            return $"ZasDict for Windows: {name}{(IsDirty ? " *" : "")}";
        }
    }

    public string DictionaryName => _document?.Name ?? Strings.Main_NoDictionaryName;

    public string Status
    {
        get => _status;
        internal set => Set(ref _status, value);
    }

    public string CountLabel => _document is null ? "" : string.Format(Strings.Main_CountLabel, FilteredWords.Count, _document.Words.Count);

    /// <summary>単語数ウィンドウに出す総語数。絞り込みには連動させない（配信で見せるのは辞書の規模）。</summary>
    public string WordCountLabel => string.Format(Strings.Main_WordCountLabel, _document?.Words.Count ?? 0);

    /// <summary>AssemblyVersion（csproj の ApplyVersionPatch が組み立てる）をそのまま表示する。</summary>
    public string VersionLabel => string.Format(Strings.Main_VersionLabel, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

    public bool IsGitHubMode => Settings.Mode == EditMode.GitHub;

    /// <summary>GitHubへの通信中。連打で二重コミットにならないよう、この間はボタンを無効にする。</summary>
    public bool IsGitHubBusy
    {
        get => _isGitHubBusy;
        internal set => Set(ref _isGitHubBusy, value);
    }

    /// <summary>直近のGitHub読み込み・コミット以降、辞書を編集していないか。真ならコミットしても
    /// GitHub側と差が出ないので、コミットボタンはこれで無効化する。実際の内容を毎回比較するのではなく
    /// 「読み込み／コミット直後」を基点に、そこから編集が入ったかどうかで判定する（＝厳密な差分検知
    /// ではなく、その近似）。アプリ起動直後や手元のファイルを直接開いた場合は基点が無いので false
    /// （＝差があるかもしれない扱い）から始まる。</summary>
    public bool IsGitHubSynced
    {
        get => _isGitHubSynced;
        internal set => Set(ref _isGitHubSynced, value);
    }

    /// <summary>確認ダイアログ専用の層。窓全体を覆い、ドッキング中の画面を閉じずに上へ重ねる。</summary>
    public OverlayViewModel? ModalOverlay
    {
        get => _modal;
        internal set
        {
            if (!Set(ref _modal, value)) return;
            Raise(nameof(IsBrowserShown));
        }
    }

    /// <summary>ブラウザのタブの中身を出してよいか。窓全体を覆う確認ダイアログの間は
    /// airspace（WebView2 が WPF 描画より手前に出る）を避けるため強制的に false にする。</summary>
    public bool IsBrowserShown => Browser.IsOpen && ModalOverlay is null;

    public double BaseFontSize => 14 * Settings.FontScale;
    public double HeadwordFontSize => 30 * Settings.FontScale;

    // ---- ボタンの有効・無効（ShellPresenter が Mediator の裁定を写す）----------------

    private bool _canOpen, _canNewDictionary, _canSave, _canSaveAs, _canGitHubLoad, _canGitHubCommit;
    private bool _canNewWord, _canEditWord, _canDuplicateWord, _canDeleteWord;
    private bool _canShowBrowser, _canShowSettings, _canShowExamples, _canEditExample;
    private bool _canShowDialectTool, _canShowIpaTool, _canShowStats, _canShowLegend, _canShowChangelog;

    public bool CanOpen { get => _canOpen; internal set => Set(ref _canOpen, value); }
    public bool CanNewDictionary { get => _canNewDictionary; internal set => Set(ref _canNewDictionary, value); }
    public bool CanSave { get => _canSave; internal set => Set(ref _canSave, value); }
    public bool CanSaveAs { get => _canSaveAs; internal set => Set(ref _canSaveAs, value); }
    public bool CanGitHubLoad { get => _canGitHubLoad; internal set => Set(ref _canGitHubLoad, value); }
    public bool CanGitHubCommit { get => _canGitHubCommit; internal set => Set(ref _canGitHubCommit, value); }
    public bool CanNewWord { get => _canNewWord; internal set => Set(ref _canNewWord, value); }
    public bool CanEditWord { get => _canEditWord; internal set => Set(ref _canEditWord, value); }
    public bool CanDuplicateWord { get => _canDuplicateWord; internal set => Set(ref _canDuplicateWord, value); }
    public bool CanDeleteWord { get => _canDeleteWord; internal set => Set(ref _canDeleteWord, value); }
    public bool CanShowBrowser { get => _canShowBrowser; internal set => Set(ref _canShowBrowser, value); }
    public bool CanShowSettings { get => _canShowSettings; internal set => Set(ref _canShowSettings, value); }
    public bool CanShowExamples { get => _canShowExamples; internal set => Set(ref _canShowExamples, value); }
    public bool CanEditExample { get => _canEditExample; internal set => Set(ref _canEditExample, value); }
    public bool CanShowDialectTool { get => _canShowDialectTool; internal set => Set(ref _canShowDialectTool, value); }
    public bool CanShowIpaTool { get => _canShowIpaTool; internal set => Set(ref _canShowIpaTool, value); }
    public bool CanShowStats { get => _canShowStats; internal set => Set(ref _canShowStats, value); }
    public bool CanShowLegend { get => _canShowLegend; internal set => Set(ref _canShowLegend, value); }
    public bool CanShowChangelog { get => _canShowChangelog; internal set => Set(ref _canShowChangelog, value); }

    // ---- ヘッダの階層メニュー（ShellPresenter が Mediator の組んだ項目を写す）----------

    private IReadOnlyList<MenuActionSpec> _fileMenuItems = Array.Empty<MenuActionSpec>();
    private IReadOnlyList<MenuActionSpec> _toolsMenuItems = Array.Empty<MenuActionSpec>();
    private IReadOnlyList<MenuActionSpec> _windowMenuItems = Array.Empty<MenuActionSpec>();

    public IReadOnlyList<MenuActionSpec> FileMenuItems { get => _fileMenuItems; internal set => Set(ref _fileMenuItems, value); }
    public IReadOnlyList<MenuActionSpec> ToolsMenuItems { get => _toolsMenuItems; internal set => Set(ref _toolsMenuItems, value); }
    public IReadOnlyList<MenuActionSpec> WindowMenuItems { get => _windowMenuItems; internal set => Set(ref _windowMenuItems, value); }

    /// <summary>検索欄 ⇄ 結果一覧のフォーカス移動の指示。フォーカスは WPF の API でしか動かせないので、
    /// 検索パネル（View）がこれを受けて実行する。</summary>
    public event Action<FocusTarget>? FocusRequested;

    internal void RequestFocus(FocusTarget target) => FocusRequested?.Invoke(target);

    /// <summary>フッタの件数と単語数ウィンドウの語数。総語数しか変わらない場面でも 2 つまとめて出し直す。</summary>
    internal void RaiseCounts()
    {
        Raise(nameof(CountLabel));
        Raise(nameof(WordCountLabel));
    }

    /// <summary>辞書の中身（語数・名前・保存先）が変わったときに表示を出し直す。</summary>
    internal void RaiseDocumentChanged()
    {
        Raise(nameof(AllWords));
        Raise(nameof(WindowTitle));
        Raise(nameof(DictionaryName));
        RaiseCounts();
    }

    /// <summary>設定（文字サイズ倍率・編集モード）から決まる表示を出し直す。</summary>
    internal void RaiseSettingsChanged()
    {
        Raise(nameof(BaseFontSize));
        Raise(nameof(HeadwordFontSize));
        Raise(nameof(IsGitHubMode));
    }
}
