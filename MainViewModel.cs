using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Microsoft.Win32;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly List<ChangeEntry> _pendingChanges = new();

    private OtmDocument? _doc;
    private TextProcessor _text = new(TextProcessor.DefaultSortOrder, "");
    private SearchService _search;
    private RelationService _relations;

    private Word? _selected;
    private string _query = "";
    private SearchMode _mode = SearchMode.Forward;
    private SearchScope _scope = SearchScope.Both;
    private bool _isDirty;
    private OverlayViewModel? _modal;
    private string _status = "辞書を開いてください。";
    /// <summary>例文一覧の絞り込み文字列。編集に入って戻ってきても打ち直さずに済むよう覚えておく。</summary>
    private string _exampleQuery = "";

    // ---- GitHub モード ------------------------------------------------------
    private bool _isGitHubBusy;

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        _search = new SearchService(_text, null);
        _relations = new RelationService(Choices.Current.Relations);
        Browser = new BrowserViewModel(Settings);

        // タブの開閉は確認ダイアログとの排他表示（IsBrowserShown）に効く。
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserViewModel.IsOpen)) Raise(nameof(IsBrowserShown));
        };

        Layout = new DockLayout(Settings);
        OverlayDragState.Instance.Move = Layout.Move;
        // 窓の外へ落としたタブは独立ウィンドウにする。窓そのものの開け閉めは MainWindow が受け持つ。
        OverlayDragState.Instance.FloatOut = (vm, at) => Layout.Float(vm, at);
        // タブを選び直すのも「触った」うち。Esc はここで一番新しいものを閉じる。
        Layout.Touched += Touch;

        // 検索も単語詳細も、他のオーバーレイと同じ 1 枚のタブ。閉じられないだけで枠は自由に選べる。
        Layout.Add(new SearchViewModel(this));
        Layout.Add(new WordDetailViewModel(this));
        if (Browser.IsOpen) OpenBrowserTab();

        // 編集中に辞書を差し替えると、保存の宛先だけが入れ替わって別の辞書に書き込まれる。
        OpenCommand = new RelayCommand(OpenDictionary, () => !IsEditorOpen);
        NewDictionaryCommand = new RelayCommand(NewDictionary, () => !IsEditorOpen);
        SaveCommand = new RelayCommand(() => Save(false), () => _doc is not null);
        SaveAsCommand = new RelayCommand(() => Save(true), () => _doc is not null);
        // GitHubモードだけの操作。編集画面が開いている間は辞書の差し替えを止める（開く／新規辞書と同じ理由）。
        LoadFromGitHubCommand = new RelayCommand(async () => await LoadFromGitHubAsync(), () => IsGitHubMode && !IsGitHubBusy && !IsEditorOpen);
        CommitToGitHubCommand = new RelayCommand(ShowCommitDialog, () => IsGitHubMode && !IsGitHubBusy && _doc is not null);
        // 同じ画面は 1 枚まで。開いている種類のボタンは無効にして、書きかけの入力が
        // 差し替えで飛ぶのを防ぐ（ショートカットはオーバーレイの上からでも届くため）。
        NewWordCommand = new RelayCommand(NewWord, () => _doc is not null && CanOpen<WordEditViewModel>());
        // ツールバー・ショートカットは選択中の単語を、行のメニューは渡された単語を見る
        // （どちらか一方だけ null でも動くように、実行側と同じ落とし先を判定にも使う）。
        EditWordCommand = new RelayCommand(o => EditWord(o as Word ?? SelectedWord), o => (o as Word ?? SelectedWord) is not null && CanOpen<WordEditViewModel>());
        DuplicateWordCommand = new RelayCommand(o => DuplicateWord(o as Word ?? SelectedWord), o => (o as Word ?? SelectedWord) is not null && CanOpen<WordEditViewModel>());
        // 編集中の単語を消せると、開いたままのエディタが宙に浮く。
        DeleteWordCommand = new RelayCommand(o => ConfirmDeleteWord(o as Word ?? SelectedWord), o => (o as Word ?? SelectedWord) is not null && CanOpen<WordEditViewModel>());
        // 他のタブ系ボタン（ツール・凡例・統計など）と同じく、開いている間は押し直せないよう
        // グレーアウトする。閉じる操作はタブの ✕ に一本化し、ボタン自体はトグルにしない。
        ShowBrowserCommand = new RelayCommand(OpenBrowserTab, CanOpen<BrowserTabViewModel>);
        ShowSettingsCommand = new RelayCommand(ShowSettings, CanOpen<SettingsViewModel>);
        ShowExamplesCommand = new RelayCommand(() => ShowExamples(), () => _doc is not null && CanOpen<ExamplesViewModel>());
        EditExampleCommand = new RelayCommand(
            o => { if (o is Example e) ShowExampleEditor(e); },
            _ => CanOpen<ExampleEditViewModel>());
        // ツール類は他のタブと違い、すでに開いていればそのタブを表に出すだけ（ボタンは殺さない）。
        // 独立ウィンドウで開いている場合はその窓を前に出す。
        ShowDialectToolCommand = new RelayCommand(
            () => ShowTool<DialectToolViewModel>(() => new DialectToolViewModel(SelectedWord?.Form)), NoModal);
        ShowIpaToolCommand = new RelayCommand(() => ShowTool<IpaToolViewModel>(() => new IpaToolViewModel()), NoModal);
        ShowStatsCommand = new RelayCommand(() => ShowTool<StatsViewModel>(() => new StatsViewModel(_doc)), NoModal);
        ShowLegendCommand = new RelayCommand(
            () => ShowTool<LegendViewModel>(() => new LegendViewModel(BuildLegendMarkdown())), NoModal);
        ShowChangelogCommand = new RelayCommand(() => ShowTool<ChangelogViewModel>(BuildChangelog), NoModal);
        SetModeCommand = new RelayCommand(o => { if (o is string s) SearchMode = Enum.Parse<SearchMode>(s); });
        SetScopeCommand = new RelayCommand(o => { if (o is string s) SearchScope = Enum.Parse<SearchScope>(s); });
        FollowRelationCommand = new RelayCommand(o => FollowRelation(o as Relation));
        ClearQueryCommand = new RelayCommand(() => Query = "");

        ApplySettings();

        if (Settings.LastDictionaryPath is { } last && File.Exists(last))
            LoadDictionary(last);
    }

    public AppSettings Settings { get; }

    public ObservableCollection<Word> FilteredWords { get; } = new();

    public ObservableCollection<Word> AllWords => _doc?.Words ?? EmptyWords;

    /// <summary>ブラウザのタブ（WebView2）の状態。</summary>
    public BrowserViewModel Browser { get; }

    /// <summary>画面の割り付け。オーバーレイはここのどれかの枠に入って初めて画面に出る。</summary>
    public DockLayout Layout { get; }

    private static readonly ObservableCollection<Word> EmptyWords = new();

    public Word? SelectedWord
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(HasSelection));
            RefreshRelatedExamples();
        }
    }

    public bool HasSelection => SelectedWord is not null;

    /// <summary>選択中の単語を参照している例文。詳細欄の「参照例文」に出す。</summary>
    public ObservableCollection<Example> RelatedExamples { get; } = new();

    private void RefreshRelatedExamples()
    {
        RelatedExamples.Clear();
        if (_doc is null || SelectedWord is null) return;
        _doc.ResolveExampleForms();
        foreach (var e in _doc.ExamplesFor(SelectedWord.Id)) RelatedExamples.Add(e);
    }

    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value)) ApplyFilter(); }
    }

    public SearchMode SearchMode
    {
        get => _mode;
        set { if (Set(ref _mode, value)) ApplyFilter(); }
    }

    public SearchScope SearchScope
    {
        get => _scope;
        set { if (Set(ref _scope, value)) ApplyFilter(); }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set { if (Set(ref _isDirty, value)) Raise(nameof(WindowTitle)); }
    }

    public string WindowTitle
    {
        get
        {
            var name = _doc?.Name ?? "辞書なし";
            return $"ZasDict for Windows: {name}{(IsDirty ? " *" : "")}";
        }
    }

    public string DictionaryName => _doc?.Name ?? "辞書なし";

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string CountLabel => _doc is null ? "" : $"{FilteredWords.Count} / {_doc.Words.Count} 語";

    public bool IsGitHubMode => Settings.Mode == EditMode.GitHub;

    /// <summary>GitHubへの通信中。連打で二重コミットにならないよう、この間はボタンを無効にする。</summary>
    public bool IsGitHubBusy
    {
        get => _isGitHubBusy;
        private set => Set(ref _isGitHubBusy, value);
    }

    /// <summary>確認ダイアログ専用の層。窓全体を覆い、ドッキング中の画面を閉じずに上へ重ねる。</summary>
    public OverlayViewModel? ModalOverlay
    {
        get => _modal;
        private set
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

    /// <summary>Ctrl＋ホイールひと目盛りぶん文字サイズを増減する。動かす値は設定画面の倍率そのもの
    /// なので、上下限も設定画面（SettingsViewModel.ApplyAll）と揃えてある。
    /// ApplySettings() は通さない。あちらは Heksa フォントをファイルから読み直すため、
    /// ホイールの連打で毎回走らせるには重い。</summary>
    public void ZoomFont(int steps)
    {
        var scale = Math.Clamp(Math.Round(Settings.FontScale + steps * 0.1, 1), 0.6, 3.0);
        if (Math.Abs(scale - Settings.FontScale) < 0.001) return;

        Settings.FontScale = scale;
        Settings.Save();
        FontScaleState.Instance.Scale = scale;
        Raise(nameof(BaseFontSize));
        Raise(nameof(HeadwordFontSize));
        Status = $"文字サイズ {scale * 100:0} %";
    }

    public double StreamHeadwordSize => 30 * Settings.StreamFontScale;
    public double StreamBodySize => 15 * Settings.StreamFontScale;
    public double StreamLabelSize => 12 * Settings.StreamFontScale;
    public double StreamPlaceholderSize => 22 * Settings.StreamFontScale;

    public ICommand OpenCommand { get; }
    public ICommand NewDictionaryCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand LoadFromGitHubCommand { get; }
    public ICommand CommitToGitHubCommand { get; }
    public ICommand NewWordCommand { get; }
    public ICommand EditWordCommand { get; }
    public ICommand DuplicateWordCommand { get; }
    public ICommand DeleteWordCommand { get; }
    public ICommand ShowBrowserCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowExamplesCommand { get; }
    public ICommand EditExampleCommand { get; }
    public ICommand ShowDialectToolCommand { get; }
    public ICommand ShowIpaToolCommand { get; }
    public ICommand ShowStatsCommand { get; }
    public ICommand ShowLegendCommand { get; }
    public ICommand ShowChangelogCommand { get; }
    public ICommand SetModeCommand { get; }
    public ICommand SetScopeCommand { get; }
    public ICommand FollowRelationCommand { get; }
    public ICommand ClearQueryCommand { get; }

    // ---- overlays -------------------------------------------------------

    /// <summary>Esc で閉じる相手。触った順に前へ来る（開いた・タブを選んだ順）。</summary>
    private readonly List<OverlayViewModel> _recent = new();

    /// <summary>本体の窓で Esc が閉じる相手。持ち出したタブはその窓の Esc が閉じるので飛ばす。</summary>
    public OverlayViewModel? ActiveOverlay => _recent.FirstOrDefault(vm => Layout.FloatOf(vm) is null);

    /// <summary>その種類がまだ開いていないか。確認ダイアログ中はどれも開かせない。</summary>
    public bool CanOpen<T>() where T : OverlayViewModel
        => ModalOverlay is null && !Layout.Overlays.Any(o => o is T);

    /// <summary>確認ダイアログを開いている間は、先にそれへ答えてもらう。</summary>
    private bool NoModal() => ModalOverlay is null;

    /// <summary>このタブを表に出したい、という合図。それがいる窓を前に出すのはビューの役目。</summary>
    public event Action<OverlayViewModel>? OverlayFocused;

    /// <summary>
    /// ツール類を開く。すでに開いていれば作り直さず、そのタブを表に出す（見ている位置や
    /// 打ちかけの入力を捨てないため）。独立ウィンドウにいるならその窓を前に出す。
    /// </summary>
    private void ShowTool<T>(Func<T> create) where T : OverlayViewModel
    {
        if (Layout.Overlays.OfType<T>().FirstOrDefault() is not { } open)
        {
            open = create();
            ShowOverlay(open);
        }
        else if (Layout.LeafOf(open) is { } leaf) leaf.Selected = open;
        OverlayFocused?.Invoke(open);
    }

    /// <summary>編集中は辞書の差し替えを止める。保存の宛先だけが入れ替わるのを防ぐため。</summary>
    private bool IsEditorOpen
        => ModalOverlay is not null
           || Layout.Overlays.Any(o => o is WordEditViewModel or ExampleEditViewModel);

    public void ShowOverlay(OverlayViewModel vm)
    {
        if (!vm.IsDockable)
        {
            vm.RequestClose = () => ModalOverlay = null;
            ModalOverlay = vm;
            return;
        }
        vm.RequestClose = () => CloseOverlay(vm);
        Layout.Add(vm);
        Touch(vm);
    }

    public void CloseOverlay(OverlayViewModel vm)
    {
        // ブラウザは次の起動で開き直すかどうかを覚えているので、タブを閉じたことを本体にも伝える。
        if (vm is BrowserTabViewModel) Browser.Deactivate();
        Layout.Remove(vm);
        _recent.Remove(vm);
        Raise(nameof(ActiveOverlay));
    }

    public void CloseModal() => ModalOverlay = null;

    /// <summary>
    /// ブラウザのタブを開く。中身の WebView2 は初期化を遅らせてあるので、
    /// 枠に並べてから <see cref="BrowserViewModel.Activate"/> で起こす。
    /// </summary>
    private void OpenBrowserTab()
    {
        ShowOverlay(new BrowserTabViewModel(this, Browser));
        Browser.Activate();
    }

    private void Touch(OverlayViewModel? vm)
    {
        // 据え置きのタブは閉じられないので、Esc の行き先（＝最後に触ったタブ）にも入れない。
        if (vm is null || vm.IsPinned) return;
        _recent.Remove(vm);
        _recent.Insert(0, vm);
        Raise(nameof(ActiveOverlay));
    }

    // ---- dictionary -----------------------------------------------------

    private void NewDictionary()
    {
        _doc = OtmJsonIo.CreateEmpty();
        Settings.LastDictionaryPath = null;
        SelectedWord = null;
        RebuildIndex();
        IsDirty = true;
        Status = "空の辞書を作成しました。保存時にファイル名を指定します。";
        RaiseDocumentChanged();
    }

    private void OpenDictionary()
    {
        // ファイルダイアログだけは OS のウィンドウ。OBS ではこの瞬間だけ映らない。
        var dlg = new OpenFileDialog
        {
            Title = "OTM-JSON 辞書を開く",
            Filter = "OTM-JSON (*.json)|*.json|すべてのファイル (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        LoadDictionary(dlg.FileName);
    }

    private void LoadDictionary(string path)
    {
        try
        {
            _doc = OtmJsonIo.Load(path);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"辞書の読み込み ({path})", ex);
            Status = $"読み込みに失敗しました: {ex.Message}";
            ShowOverlay(new ChoiceViewModel("読み込めません", $"{path}{Environment.NewLine}{ex.Message}").AddCancel("閉じる"));
            return;
        }

        Settings.LastDictionaryPath = path;
        Settings.Save();
        SelectedWord = null;
        _pendingChanges.Clear();
        _exampleQuery = "";
        ApplySettings();
        RebuildIndex();
        IsDirty = false;
        Status = $"{Path.GetFileName(path)} を読み込みました。";
        RaiseDocumentChanged();
    }

    private void Save(bool forceNewPath)
    {
        if (_doc is null) return;
        var path = _doc.Path;
        if (forceNewPath || path is null)
        {
            var dlg = new SaveFileDialog
            {
                Title = "辞書を保存",
                Filter = "OTM-JSON (*.json)|*.json",
                FileName = path is null ? "dictionary.json" : Path.GetFileName(path)
            };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }

        try
        {
            OtmJsonIo.Save(_doc, path);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"辞書の保存 ({path})", ex);
            Status = $"保存に失敗しました: {ex.Message}";
            ShowOverlay(new ChoiceViewModel("保存できません", ex.Message).AddCancel("閉じる"));
            return;
        }

        FlushChangelog();
        Settings.LastDictionaryPath = path;
        Settings.Save();
        IsDirty = false;
        Status = $"{Path.GetFileName(path)} に保存しました。";
        Raise(nameof(WindowTitle));
        Raise(nameof(DictionaryName));
    }

    private void FlushChangelog()
    {
        if (_doc?.Path is null || _pendingChanges.Count == 0) return;
        var csv = Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path);
        try
        {
            ChangelogService.Append(csv, _pendingChanges);
            _pendingChanges.Clear();
        }
        catch (IOException ex)
        {
            // 追記に失敗しても未保存の履歴は捨てず、次回保存で再試行する。
            ErrorLog.Write($"更新履歴の追記 ({csv})", ex);
            Status = $"更新履歴の追記に失敗しました: {ex.Message}";
        }
    }

    /// <summary>保留中の更新履歴にエントリを追加する。同じ見出し語の直近エントリが ADD / CHANGE
    /// の場合に CHANGE を重ねても追記しない（保存までの「追加→編集」「編集→編集」は 1 行に集約）。
    /// DELETE の後の CHANGE や、別の見出し語への CHANGE は普通に追記する。</summary>
    private void AddPendingChange(ChangeEntry entry)
    {
        if (entry.Operation == "CHANGE")
        {
            for (int i = _pendingChanges.Count - 1; i >= 0; i--)
            {
                if (_pendingChanges[i].Form != entry.Form) continue;
                if (_pendingChanges[i].Operation is "ADD" or "CHANGE") return;
                break;
            }
        }
        _pendingChanges.Add(entry);
    }

    // ---- GitHub モード ----------------------------------------------------

    private readonly record struct GitHubConfig(string Owner, string Repo, string Branch, string JsonPath, string ChangelogPath, string Token);

    private bool TryGetGitHubConfig(out GitHubConfig cfg, out string error)
    {
        var owner = Settings.GitHubOwner?.Trim() ?? "";
        var repo = Settings.GitHubRepo?.Trim() ?? "";
        var branch = string.IsNullOrWhiteSpace(Settings.GitHubBranch) ? "main" : Settings.GitHubBranch.Trim();
        var jsonPath = Settings.GitHubJsonPath?.Trim() ?? "";
        var changelogPath = Settings.GitHubChangelogPath?.Trim() ?? "";
        var token = GitHubApi.LoadToken();

        if (owner.Length == 0 || repo.Length == 0 || jsonPath.Length == 0)
        {
            cfg = default;
            error = "設定 → GitHub でリポジトリと辞書ファイルのパスを入力してください。";
            return false;
        }
        if (token is null)
        {
            cfg = default;
            error = "設定 → GitHub でアクセストークンを保存してください。";
            return false;
        }

        cfg = new GitHubConfig(owner, repo, branch, jsonPath, changelogPath, token);
        error = "";
        return true;
    }

    /// <summary>GitHub から取得した辞書の置き場所。owner/repo/branch ごとに分けておき、
    /// 別のリポジトリへ切り替えても前のローカルコピーを踏まないようにする。</summary>
    private static string GitHubLocalCachePath(GitHubConfig cfg)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin", "github",
            $"{SanitizeFileName(cfg.Owner)}__{SanitizeFileName(cfg.Repo)}__{SanitizeFileName(cfg.Branch)}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Path.GetFileName(cfg.JsonPath));
    }

    private static string SanitizeFileName(string s)
        => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private Task LoadFromGitHubAsync()
    {
        if (!TryGetGitHubConfig(out var cfg, out var error)) { Status = error; return Task.CompletedTask; }

        // 押し間違いでローカルの内容を消さないよう、保存済みかどうかに関わらず確認を挟む。
        ShowOverlay(new ChoiceViewModel("GitHubから読み込み",
                "ローカルの変更を破棄してGitHubから再読み込みをしますか？")
            .Add("読み込む", () => _ = LoadFromGitHubCoreAsync(cfg), isDanger: true)
            .AddCancel("やめる"));
        return Task.CompletedTask;
    }

    private async Task LoadFromGitHubCoreAsync(GitHubConfig cfg)
    {
        IsGitHubBusy = true;
        Status = "GitHubから読み込み中…";
        try
        {
            var jsonResult = await GitHubApi.GetFileAsync(cfg.Owner, cfg.Repo, cfg.JsonPath, cfg.Branch, cfg.Token).ConfigureAwait(true);
            if (!jsonResult.Ok)
            {
                if (jsonResult.AuthFailed) GitHubApi.DeleteToken();
                Status = $"GitHubからの読み込みに失敗しました: {jsonResult.Message}";
                return;
            }

            var localPath = GitHubLocalCachePath(cfg);
            File.WriteAllText(localPath, jsonResult.Content, new UTF8Encoding(false));

            if (cfg.ChangelogPath.Length > 0)
            {
                var csvResult = await GitHubApi.GetFileAsync(cfg.Owner, cfg.Repo, cfg.ChangelogPath, cfg.Branch, cfg.Token).ConfigureAwait(true);
                if (csvResult.Ok)
                {
                    File.WriteAllText(ChangelogService.DefaultPathFor(localPath), csvResult.Content, new UTF8Encoding(true));
                    Settings.ChangelogPath = ChangelogService.DefaultPathFor(localPath);
                }
                else if (!csvResult.NotFound)
                {
                    // 辞書自体は読み込めたので続行する。履歴だけ最初のコミットで新規作成させる。
                    Status = $"更新履歴の読み込みに失敗しました: {csvResult.Message}";
                }
            }

            LoadDictionary(localPath);
            Status = $"GitHubから読み込みました（{cfg.Owner}/{cfg.Repo} @ {cfg.Branch}）。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("GitHubから取得したファイルの書き込み", ex);
            Status = $"ローカルへの書き込みに失敗しました: {ex.Message}";
        }
        finally
        {
            IsGitHubBusy = false;
        }
    }

    private void ShowCommitDialog()
    {
        if (!TryGetGitHubConfig(out var cfg, out var error)) { Status = error; return; }
        if (_doc is null) { Status = "辞書を開いてください。"; return; }

        var forms = _pendingChanges.Select(e => e.Form).Distinct().ToList();
        var summary = _pendingChanges.Count == 0
            ? "保留中の更新はありません。辞書ファイルの現在の内容をそのままコミットします。"
            : string.Join(Environment.NewLine, _pendingChanges.Select(e => $"{e.Operation} {e.Form}"));
        var defaultMessage = _pendingChanges.Count == 0
            ? "ZasDict: 更新"
            : $"ZasDict: {string.Join(", ", forms.Take(5))}{(forms.Count > 5 ? " ほか" : "")}";

        ShowOverlay(new CommitViewModel(summary, defaultMessage, message => _ = CommitToGitHubAsync(cfg, message)));
    }

    private async Task CommitToGitHubAsync(GitHubConfig cfg, string message)
    {
        if (_doc is null) return;

        // コミットの対象は常にローカルの最新内容。保存は自動保存（または手動保存）が担うが、
        // それがオフの環境でも古い内容をコミットしないよう、念のためここでも確定させる。
        if (IsDirty) Save(false);
        if (_doc.Path is null) { Status = "保存先が決まっていません。"; return; }

        IsGitHubBusy = true;
        Status = "コミット中…";
        try
        {
            var files = new List<GitHubFileChange> { new(cfg.JsonPath, File.ReadAllText(_doc.Path)) };

            var csvPath = Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path);
            if (cfg.ChangelogPath.Length > 0 && File.Exists(csvPath))
            {
                var csvText = File.ReadAllText(csvPath);
                if (!csvText.StartsWith(string.Join(',', ChangelogService.DefaultHeader)))
                    csvText = string.Join(',', ChangelogService.DefaultHeader) + "\n" + csvText;
                files.Add(new GitHubFileChange(cfg.ChangelogPath, csvText));
            }

            // 辞書と更新履歴は Git Data API で 1 コミットにまとめる（Contents API のように
            // ファイルごとの sha は要らない。ブランチ先端から毎回作り直すため）。
            var result = await GitHubApi.CommitFilesAsync(cfg.Owner, cfg.Repo, cfg.Branch, cfg.Token, files, message).ConfigureAwait(true);
            if (!result.Ok)
            {
                if (result.AuthFailed) GitHubApi.DeleteToken();
                Status = $"コミットに失敗しました: {result.Message}";
                return;
            }

            Status = "GitHubへコミットしました。";
        }
        catch (IOException ex)
        {
            ErrorLog.Write("コミット対象ファイルの読み込み", ex);
            Status = $"コミットに失敗しました: {ex.Message}";
        }
        finally
        {
            IsGitHubBusy = false;
        }
    }

    // ---- indexing / filtering -------------------------------------------

    private void ApplySettings()
    {
        var punctuations = "";
        var ignoredPattern = (string?)null;
        if (_doc is not null)
        {
            punctuations = string.Concat(
                (_doc.ZpdicOnline["punctuations"] as JsonArray)?
                    .Select(n => n?.GetValue<string>() ?? "") ?? Array.Empty<string>());
            ignoredPattern = _doc.ZpdicOnline["ignoredPattern"]?.GetValue<string>();
        }

        _text = new TextProcessor(Settings.SortOrder, punctuations);
        _search = new SearchService(_text, ignoredPattern);
        _relations = new RelationService(Choices.Current.Relations);

        var heksa = Settings.HeksaEnabled ? HeadwordFontState.Load(Settings.HeksaFontPath) : null;
        HeadwordFontState.Instance.Family = heksa ?? HeadwordFontState.Fallback;
        FontScaleState.Instance.Scale = Settings.FontScale;
        Browser.SyncWithSettings();   // 幅と開始 URL。表示中のページは切り替えない

        Raise(nameof(BaseFontSize));
        Raise(nameof(HeadwordFontSize));
        Raise(nameof(StreamHeadwordSize));
        Raise(nameof(StreamBodySize));
        Raise(nameof(StreamLabelSize));
        Raise(nameof(StreamPlaceholderSize));
        Raise(nameof(IsGitHubMode));
    }

    public void RebuildIndex()
    {
        if (_doc is null) { FilteredWords.Clear(); Raise(nameof(CountLabel)); return; }

        var sorted = _doc.Words.OrderBy(w => w, _text.WordComparer).ToList();
        TextProcessor.AssignHomonymIndexes(sorted);

        _doc.Words.Clear();
        foreach (var w in sorted) _doc.Words.Add(w);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredWords.Clear();
        if (_doc is null) { Raise(nameof(CountLabel)); return; }
        foreach (var w in _search.Filter(_doc.Words, Query, SearchMode, SearchScope))
            FilteredWords.Add(w);
        Raise(nameof(CountLabel));
    }

    private void RaiseDocumentChanged()
    {
        RefreshRelatedExamples();
        Raise(nameof(AllWords));
        Raise(nameof(WindowTitle));
        Raise(nameof(DictionaryName));
        Raise(nameof(CountLabel));
    }

    // ---- word operations -------------------------------------------------

    /// <summary>検索欄の文字列を見出し語の初期値にする（ZasDictAndroid の FAB と同じ挙動）。</summary>
    private void NewWord()
    {
        if (_doc is null) return;
        ShowOverlay(new WordEditViewModel(null, _doc.Words, _relations, _search, CommitEdit, Query));
    }

    private void EditWord(Word? w)
    {
        if (_doc is null || w is null) return;
        ShowOverlay(new WordEditViewModel(w, _doc.Words, _relations, _search, CommitEdit));
    }

    /// <summary>検索結果のダブルクリックから呼ぶ。「同じ画面は 1 枚まで」（<see cref="EditWordCommand"/> 等）
    /// の縛りはそのままに、単語編集を開いたまま別の単語をダブルクリックしたときだけ、
    /// そちらへ差し替える。差し替え前の内容が変わっていれば確認する（同じ単語なら前に出すだけ）。</summary>
    public void RequestEditWord(Word? w)
    {
        if (_doc is null || w is null || ModalOverlay is not null) return;

        var current = Layout.Overlays.OfType<WordEditViewModel>().FirstOrDefault();
        if (current is null) { EditWord(w); return; }
        if (current.Source == w)
        {
            if (Layout.LeafOf(current) is { } leaf) leaf.Selected = current;
            return;
        }

        if (!current.HasChanges) { CloseOverlay(current); EditWord(w); return; }

        ShowOverlay(new ChoiceViewModel("保存されていない変更があります",
                $"「{current.Form}」の編集内容を保存せずに閉じますか？")
            .Add("閉じる", () => { CloseOverlay(current); EditWord(w); }, isDanger: true)
            .AddCancel("やめる"));
    }

    private void CommitEdit(WordEditViewModel vm)
    {
        if (_doc is null) return;

        var isNew = vm.Source is null;
        var word = vm.Source ?? Word.CreateNew(_doc.NextId());
        var oldForm = word.Form;
        var formChanged = word.Form != vm.Form.Trim();

        word.Form = vm.Form.Trim();
        word.Translations = vm.BuildTranslations();
        word.Tags = vm.BuildTags();
        word.Contents = vm.BuildContents();
        word.Variations = vm.BuildVariations();
        _relations.ApplyRelations(_doc.Words, word, vm.BuildRelations());
        word.WriteBack();
        word.NotifyChanged();

        if (isNew) _doc.Words.Add(word);
        if (formChanged && !isNew) RelationService.PropagateFormChange(_doc.Words, word);
        // 例文が持つのは id だけなので、見出し語の表示は辞書側から引き直す。
        _doc.ResolveExampleForms();

        // 更新履歴は ADD / CHANGE / DELETE。見出し語変更（リネーム）の時だけ details に旧見出し語を残す。
        var changeDetail = !isNew && formChanged ? $"旧: {oldForm}" : "";
        AddPendingChange(new ChangeEntry(DateTime.Now, isNew ? "ADD" : "CHANGE", word.Form, changeDetail));

        CloseOverlay(vm);
        RebuildIndex();
        SelectedWord = word;
        MarkDirty($"「{word.Form}」を{(isNew ? "追加" : "更新")}しました。");
    }

    private void DuplicateWord(Word? w)
    {
        if (_doc is null || w is null) return;
        var copy = w.Duplicate(_doc.NextId());
        _doc.Words.Add(copy);
        AddPendingChange(new ChangeEntry(DateTime.Now, "ADD", copy.Form, ""));
        RebuildIndex();
        SelectedWord = copy;
        MarkDirty($"「{w.Form}」を複製しました。");
        EditWord(copy);
    }

    private void ConfirmDeleteWord(Word? w)
    {
        if (w is null) return;
        ShowOverlay(new ChoiceViewModel("単語を削除", $"「{w.DisplayForm}」を削除します。この単語を指している関係も外れます。")
            .Add("削除する", () => DeleteWord(w), isDanger: true)
            .AddCancel());
    }

    private void DeleteWord(Word w)
    {
        if (_doc is null) return;
        RelationService.RemoveReferences(_doc.Words, w);
        _doc.Words.Remove(w);
        // 例文から参照を外すことはしない（消した単語を後で作り直すことがあるため）。
        // 表示は「id:12」に落ちるので、例文側で消すかどうかは書き手が決められる。
        _doc.ResolveExampleForms();
        AddPendingChange(new ChangeEntry(DateTime.Now, "DELETE", w.Form, ""));
        if (SelectedWord == w) SelectedWord = null;
        RebuildIndex();
        MarkDirty($"「{w.Form}」を削除しました。");
    }

    private void FollowRelation(Relation? r)
    {
        if (_doc is null || r is null) return;
        var target = _doc.Words.FirstOrDefault(w => w.Id == r.Id);
        if (target is null)
        {
            Status = $"関係先 id={r.Id}（{r.Form}）が見つかりません。";
            return;
        }
        SelectedWord = target;
    }

    private void MarkDirty(string status)
    {
        IsDirty = true;
        Status = status;
        // 自動保存はモードに関係なく機能する（GitHubモードでもローカルファイルへの保存は通常どおり）。
        // コミットはこの保存結果を対象にするだけで、保存自体はコミットボタンの役割ではない。
        if (Settings.AutoSave && _doc?.Path is not null) Save(false);
    }

    private void ShowSettings()
    {
        ShowOverlay(new SettingsViewModel(Settings, _doc, () =>
        {
            ApplySettings();
            RebuildIndex();
            SettingsApplied?.Invoke();
            Status = "設定を適用しました。";
        }));
    }

    public event Action? SettingsApplied;

    // ---- examples --------------------------------------------------------

    /// <summary>例文の一覧を開く。閉じて開き直したときも同じ絞り込みで始まる。</summary>
    private void ShowExamples()
    {
        if (_doc is null) { Status = "辞書を開いてください。"; return; }
        var vm = new ExamplesViewModel(_doc, _exampleQuery);
        vm.AddRequested = () => { _exampleQuery = vm.Query; ShowExampleEditor(null); };
        vm.EditRequested = e => { _exampleQuery = vm.Query; ShowExampleEditor(e); };
        ShowOverlay(vm);
    }

    /// <summary>
    /// 例文エディタを開く。一覧は別のタブとして開いたままなので、閉じれば元の並びがそのまま出る。
    /// 一覧が開いていれば中身を引き直して、追加・削除をその場で反映する。
    /// </summary>
    private void ShowExampleEditor(Example? example)
    {
        if (_doc is null) return;
        ExampleEditViewModel? vm = null;
        void Back()
        {
            if (vm is not null) CloseOverlay(vm);
            foreach (var list in Layout.Overlays.OfType<ExamplesViewModel>().ToList()) list.Refresh();
        }

        vm = new ExampleEditViewModel(example, _doc, _search, v => CommitExample(v, Back));
        vm.CancelRequested = Back;
        vm.DeleteRequested = () => ConfirmDeleteExample(vm, Back);
        ShowOverlay(vm);
    }

    private void CommitExample(ExampleEditViewModel vm, Action back)
    {
        if (_doc is null) return;

        var isNew = vm.Source is null;
        var example = vm.Source ?? Example.CreateNew(_doc.NextExampleId());
        vm.ApplyTo(example);
        if (isNew) _doc.Examples.Add(example);

        RefreshRelatedExamples();
        back();
        MarkDirty($"例文を{(isNew ? "追加" : "更新")}しました。");
    }

    private void ConfirmDeleteExample(ExampleEditViewModel vm, Action back)
    {
        if (vm.Source is not { } example) return;
        var preview = example.SentencePreview;
        // 確認は編集画面とは別の層に出るので、やめたときは畳むだけで書きかけの入力はそのまま残る。
        ShowOverlay(new ChoiceViewModel("例文を削除", $"「{preview}」を削除します。")
            .Add("削除する", () => DeleteExample(example, back), isDanger: true)
            .AddCancel("やめる"));
    }

    private void DeleteExample(Example example, Action back)
    {
        if (_doc is null) return;
        _doc.Examples.Remove(example);
        RefreshRelatedExamples();
        back();
        MarkDirty("例文を削除しました。");
    }

    // ---- tools / info（ツールメニューの画面。既定は独立ウィンドウ、運べばタブ） ----------------

    /// <summary>
    /// 更新履歴の画面を組み立てる。CSV を選び直すと中身が丸ごと変わるので、そのときは
    /// 閉じて開き直す。行き先は種類ごとに覚えているので、タブでも独立ウィンドウでも同じ場所に戻る。
    /// </summary>
    private ChangelogViewModel BuildChangelog()
    {
        ChangelogViewModel? vm = null;
        var relink = new RelayCommand(() =>
        {
            if (!RelinkChangelog() || vm is null) return;
            CloseOverlay(vm);
            ShowOverlay(BuildChangelog());
        });
        var csv = ChangelogCsvPath();
        vm = new ChangelogViewModel(ReadChangelogRows(csv), csv, new RelayCommand(() => ExportChangelog(csv)), relink);
        return vm;
    }

    // legend が Markdown 文字列ならそのまま描画に渡す。構造化 JSON の場合は
    // 従来どおり整形済み JSON をテキスト表示する（Markdown として見劣りしない範囲で）。
    private string BuildLegendMarkdown() => _doc is null ? "" : _doc.Legend switch
    {
        JsonValue lv when lv.TryGetValue<string>(out var ls) => ls,
        var other => OtmJsonIo.PrettyPrint(other ?? _doc.Root["zpdicOnline"]),
    };

    private string ChangelogCsvPath() => _doc?.Path is null
        ? (Settings.ChangelogPath ?? "")
        : (Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path));

    private IReadOnlyList<string[]> ReadChangelogRows(string csv)
    {
        // CSV にまだフラッシュしていない保留履歴は、行頭（timestamp）に * を付けて後続表示する。
        var rows = new List<string[]>(csv.Length > 0 ? ChangelogService.Read(csv) : Array.Empty<string[]>());
        foreach (var e in _pendingChanges)
            rows.Add(new[] { "*" + e.At.ToString("yyyy-MM-dd"), e.Operation, e.Form, e.Detail });
        return rows;
    }

    private void ExportChangelog(string csv)
    {
        if (!File.Exists(csv)) { Status = "書き出せる更新履歴がありません。"; return; }
        var dlg = new SaveFileDialog { Title = "更新履歴を書き出す", Filter = "CSV (*.csv)|*.csv", FileName = Path.GetFileName(csv) };
        if (dlg.ShowDialog() != true) return;
        File.Copy(csv, dlg.FileName, overwrite: true);
        Status = $"{dlg.FileName} に書き出しました。";
    }

    /// <summary>CSV を選び直す。戻り値は選び直せたか（成功したときだけ画面を作り直す）。</summary>
    private bool RelinkChangelog()
    {
        var dlg = new OpenFileDialog { Title = "更新履歴 CSV を選択", Filter = "CSV (*.csv)|*.csv|すべてのファイル (*.*)|*.*" };
        if (dlg.ShowDialog() != true) return false;
        Settings.ChangelogPath = dlg.FileName;
        Settings.Save();
        Status = $"更新履歴を {Path.GetFileName(dlg.FileName)} に連携しました。";
        return true;
    }

    public bool ConfirmCloseIfDirty(Action proceed)
    {
        if (!IsDirty) return true;
        ShowOverlay(new ChoiceViewModel("未保存の変更があります", $"{DictionaryName} の変更が保存されていません。")
            .Add("保存して終了", () => { Save(false); proceed(); })
            .Add("保存せず終了", proceed, isDanger: true)
            .AddCancel("編集を続ける"));
        return false;
    }
}
