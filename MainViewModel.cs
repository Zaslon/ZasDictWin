using System.Collections.ObjectModel;
using System.IO;
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

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        _search = new SearchService(_text, null);
        _relations = new RelationService(Choices.Current.Relations);
        Browser = new BrowserViewModel(Settings);

        // サイドバーの開閉はオーバーレイとの排他表示（IsBrowserShown）に効く。
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserViewModel.IsOpen)) Raise(nameof(IsBrowserShown));
        };

        Docks = new DockGroups(Settings);
        OverlayDragState.Instance.Move = Docks.Move;
        foreach (var group in Docks.All)
        {
            group.PropertyChanged += (s, e) =>
            {
                // タブを選び直すのも「触った」うち。Esc はここで一番新しいものを閉じる。
                if (e.PropertyName == nameof(DockGroup.Selected) && s is DockGroup g) Touch(g.Selected);
            };
        }
        // 単語詳細は中央の据え置きタブ。中央へ運んだオーバーレイはこの隣に並ぶ。
        Docks.Pin(new WordDetailViewModel(this));

        // 編集中に辞書を差し替えると、保存の宛先だけが入れ替わって別の辞書に書き込まれる。
        OpenCommand = new RelayCommand(OpenDictionary, () => !IsEditorOpen);
        NewDictionaryCommand = new RelayCommand(NewDictionary, () => !IsEditorOpen);
        SaveCommand = new RelayCommand(() => Save(false), () => _doc is not null);
        SaveAsCommand = new RelayCommand(() => Save(true), () => _doc is not null);
        // 同じ画面は 1 枚まで。開いている種類のボタンは無効にして、書きかけの入力が
        // 差し替えで飛ぶのを防ぐ（ショートカットはオーバーレイの上からでも届くため）。
        NewWordCommand = new RelayCommand(NewWord, () => _doc is not null && CanOpen<WordEditViewModel>());
        EditWordCommand = new RelayCommand(o => EditWord(o as Word ?? SelectedWord), _ => SelectedWord is not null && CanOpen<WordEditViewModel>());
        DuplicateWordCommand = new RelayCommand(o => DuplicateWord(o as Word ?? SelectedWord), _ => SelectedWord is not null && CanOpen<WordEditViewModel>());
        // 編集中の単語を消せると、開いたままのエディタが宙に浮く。
        DeleteWordCommand = new RelayCommand(o => ConfirmDeleteWord(o as Word ?? SelectedWord), _ => SelectedWord is not null && CanOpen<WordEditViewModel>());
        WordActionsCommand = new RelayCommand(o => ShowWordActions(o as Word ?? SelectedWord), _ => SelectedWord is not null && CanOpen<WordEditViewModel>());
        ShowToolsCommand = new RelayCommand(() => ShowOverlay(new ToolsViewModel(SelectedWord?.Form)), CanOpen<ToolsViewModel>);
        ShowSettingsCommand = new RelayCommand(ShowSettings, CanOpen<SettingsViewModel>);
        ShowInfoCommand = new RelayCommand(ShowInfo, CanOpen<InfoViewModel>);
        ShowExamplesCommand = new RelayCommand(() => ShowExamples(), () => _doc is not null && CanOpen<ExamplesViewModel>());
        EditExampleCommand = new RelayCommand(
            o => { if (o is Example e) ShowExampleEditor(e); },
            _ => CanOpen<ExampleEditViewModel>());
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

    /// <summary>右サイドバーのブラウザ（WebView2）の状態。</summary>
    public BrowserViewModel Browser { get; }

    /// <summary>中央と 4 辺ぶんのタブ束。オーバーレイはここに入って初めて画面に出る。</summary>
    public DockGroups Docks { get; }

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
            return $"{name}{(IsDirty ? " *" : "")} — ZasDict for Windows";
        }
    }

    public string DictionaryName => _doc?.Name ?? "辞書なし";

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string CountLabel => _doc is null ? "" : $"{FilteredWords.Count} / {_doc.Words.Count} 語";

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

    /// <summary>ブラウザサイドバーの実表示。窓全体を覆う確認ダイアログの間は
    /// airspace（WebView2 が WPF 描画より手前に出る）を避けるため強制的に false にする。
    /// ドッキングしたオーバーレイとはサイドバーと場所を取り合わないので出したままにする。</summary>
    public bool IsBrowserShown => Browser.IsOpen && ModalOverlay is null;

    public double BaseFontSize => 14 * Settings.FontScale;
    public double HeadwordFontSize => 30 * Settings.FontScale;

    public double StreamHeadwordSize => 30 * Settings.StreamFontScale;
    public double StreamBodySize => 15 * Settings.StreamFontScale;
    public double StreamLabelSize => 12 * Settings.StreamFontScale;
    public double StreamPlaceholderSize => 22 * Settings.StreamFontScale;

    public ICommand OpenCommand { get; }
    public ICommand NewDictionaryCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand NewWordCommand { get; }
    public ICommand EditWordCommand { get; }
    public ICommand DuplicateWordCommand { get; }
    public ICommand DeleteWordCommand { get; }
    public ICommand WordActionsCommand { get; }
    public ICommand ShowToolsCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowInfoCommand { get; }
    public ICommand ShowExamplesCommand { get; }
    public ICommand EditExampleCommand { get; }
    public ICommand SetModeCommand { get; }
    public ICommand SetScopeCommand { get; }
    public ICommand FollowRelationCommand { get; }
    public ICommand ClearQueryCommand { get; }

    // ---- overlays -------------------------------------------------------

    /// <summary>Esc で閉じる相手。触った順に前へ来る（開いた・タブを選んだ順）。</summary>
    private readonly List<OverlayViewModel> _recent = new();

    public OverlayViewModel? ActiveOverlay => _recent.Count > 0 ? _recent[0] : null;

    /// <summary>その種類がまだ開いていないか。確認ダイアログ中はどれも開かせない。</summary>
    public bool CanOpen<T>() where T : OverlayViewModel
        => ModalOverlay is null && !Docks.Overlays.Any(o => o is T);

    /// <summary>編集中は辞書の差し替えを止める。保存の宛先だけが入れ替わるのを防ぐため。</summary>
    private bool IsEditorOpen
        => ModalOverlay is not null
           || Docks.Overlays.Any(o => o is WordEditViewModel or ExampleEditViewModel);

    public void ShowOverlay(OverlayViewModel vm)
    {
        if (!vm.IsDockable)
        {
            vm.RequestClose = () => ModalOverlay = null;
            ModalOverlay = vm;
            return;
        }
        vm.RequestClose = () => CloseOverlay(vm);
        Docks.Add(vm);
        Touch(vm);
    }

    public void CloseOverlay(OverlayViewModel vm)
    {
        Docks.Remove(vm);
        _recent.Remove(vm);
        Raise(nameof(ActiveOverlay));
    }

    public void CloseModal() => ModalOverlay = null;

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

    private void ShowWordActions(Word? w)
    {
        if (w is null) return;
        ShowOverlay(new ChoiceViewModel(w.DisplayForm, w.TranslationSummary)
            .Add("編集", () => EditWord(w))
            .Add("複製", () => DuplicateWord(w))
            .Add("削除", () => ConfirmDeleteWord(w), isDanger: true)
            .AddCancel("閉じる"));
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
            foreach (var list in Docks.Overlays.OfType<ExamplesViewModel>().ToList()) list.Refresh();
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

    // ---- settings / info -------------------------------------------------

    private void ShowInfo()
    {
        // legend が Markdown 文字列ならそのまま描画に渡す。構造化 JSON の場合は
        // 従来どおり整形済み JSON をテキスト表示する（Markdown として見劣りしない範囲で）。
        var legend = _doc is null ? "" : _doc.Legend switch
        {
            JsonValue lv when lv.TryGetValue<string>(out var ls) => ls,
            var other => OtmJsonIo.PrettyPrint(other ?? _doc.Root["zpdicOnline"]),
        };
        var csv = _doc?.Path is null
            ? (Settings.ChangelogPath ?? "")
            : (Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path));
        // CSV にまだフラッシュしていない保留履歴は、行頭（timestamp）に * を付けて後続表示する。
        var rows = new List<string[]>(csv.Length > 0 ? ChangelogService.Read(csv) : Array.Empty<string[]>());
        foreach (var e in _pendingChanges)
            rows.Add(new[] { "*" + e.At.ToString("yyyy-MM-dd"), e.Operation, e.Form, e.Detail });

        InfoViewModel? vm = null;
        vm = new InfoViewModel(_doc, legend, rows, csv,
            new RelayCommand(() => ExportChangelog(csv)),
            // CSV を選び直すと一覧の中身が変わるので、この画面はいったん閉じて開き直させる。
            new RelayCommand(() => { if (RelinkChangelog() && vm is not null) CloseOverlay(vm); }));
        ShowOverlay(vm);
    }

    private void ExportChangelog(string csv)
    {
        if (!File.Exists(csv)) { Status = "書き出せる更新履歴がありません。"; return; }
        var dlg = new SaveFileDialog { Title = "更新履歴を書き出す", Filter = "CSV (*.csv)|*.csv", FileName = Path.GetFileName(csv) };
        if (dlg.ShowDialog() != true) return;
        File.Copy(csv, dlg.FileName, overwrite: true);
        Status = $"{dlg.FileName} に書き出しました。";
    }

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
