using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

[assembly: InternalsVisibleTo("ZasDictWin.Tests")]

namespace ZasDictWin.Mediator;

/// <summary>
/// 唯一の裁定者。すべての Intent はここを通り、3 つの直交するステートマシン
/// （ShellPhase / DragPhase / DocPhase）の遷移として処理される。
/// 外界（IO・OS ダイアログ）へは IAppServices 経由でしか触らない。
/// </summary>
public sealed partial class AppMediator
{
    /// <summary>タブを運んでいるとき、枠の端からこの範囲内なら合流ではなく分割の下見を出す。</summary>
    private const double EdgeZone = 48;

    private readonly IAppServices _services;
    private readonly MainViewModel _state;
    private readonly DockLayout _layout;

    /// <summary>Esc で閉じる相手。触った順に前へ来る（開いた・タブを選んだ順）。据え置きのタブは入れない。</summary>
    private readonly List<OverlayViewModel> _recent = new();

    /// <summary>窓を出している浮き枠。割り付けが正で、裁定の最後にこれと突き合わせて窓を開け閉めする。</summary>
    private readonly HashSet<DockFloat> _shownFloats = new();

    /// <summary>裁定中に溜める窓の操作。窓を閉じると中のタブが動いてまた裁定が走るので、
    /// その場で実行せず裁定の最後にまとめて出す（開け閉めの再入を構造で防ぐ）。</summary>
    private readonly List<HostCommand> _queuedHosts = new();

    private DragContext _drag = DragContext.Idle;
    private object? _rowDragSource;

    /// <summary>確認ダイアログの i 番が選ばれたら次に行う裁定。ShellPhase.Modal の間だけ有効で、畳んだら捨てる。</summary>
    private Action?[]? _pendingChoices;
    private bool _modal;
    private bool _gitHubBusy;
    private bool _closing;
    private bool _starting;
    private bool _persistRequested;
    private int _batchDepth;

    // ---- 窓の状態（Mediator が覚えておく事実）-----------------------------------
    private bool _shellReady;
    private Guid? _shellHostId;
    private WindowState _shellWindowState = WindowState.Normal;
    private WindowBounds? _shellBounds;
    private int _popupLayers;
    private bool _streamOpen;
    private bool _countOpen;
    private SettingsViewModel? _settingsState;
    private Guid? _settingsHostId;

    public AppMediator(IAppServices services, MainViewModel state, DockLayout layout)
    {
        _services = services;
        _state = state;
        _layout = layout;
        _search = new SearchService(_text, null);
        _relations = new RelationService(Choices.Current.Relations);
        // どこかの枠で表に出るタブが変わった（開いた・選び直した・隣へ移った）＝触ったうち。Esc はここで一番新しいものを閉じる。
        _layout.SelectedChanged = Touch;
    }

    public ShellPhase Shell => _closing ? ShellPhase.Closing
        : _modal ? ShellPhase.Modal
        : _gitHubBusy ? ShellPhase.ModalBusy
        : ShellPhase.Normal;

    public DragPhase Drag => _drag.Phase;

    public DocPhase Doc => _doc is null ? DocPhase.NoDocument : _state.IsDirty ? DocPhase.Dirty : DocPhase.Clean;

    /// <summary>裁定で決まった描画パラメータの更新要求。DockPresenter が受ける。</summary>
    public event Action<DragEffect>? DragEffectIssued;
    /// <summary>窓を開く・閉じる・前に出す。AppRoot.HostFactory が実行する。</summary>
    public event Action<HostCommand>? HostCommandIssued;
    /// <summary>確認ダイアログを出す／畳む（null で畳む）。ShellPresenter が受ける。</summary>
    public event Action<ConfirmSpec?>? ConfirmRequested;
    /// <summary>状態が変わった。1 つの Intent の裁定につき 1 回だけ出す。</summary>
    public event Action<StateChange>? Changed;

    /// <summary>画面上の位置（デバイスピクセル）がどの窓のどの枠に乗っているか。AppRoot が差し込む。</summary>
    public Func<Point, HitLeaf?>? HitTester { get; set; }

    /// <summary>Intent を上げた窓の引き当て。AppRoot が差し込む。</summary>
    public Func<Guid, IUiHost?>? HostLookup { get; set; }

    /// <summary>ドラッグの動き出しの閾値（DIP）。View 層の既定値（SystemParameters）を差し込む。</summary>
    public Size DragThreshold { get; set; } = new(4, 4);

    /// <summary>並べ替え中の一覧。行の実体は View にしか無いので、掴んだ Intent の Payload をそのまま預かって Presenter に渡す。</summary>
    public object? RowDragSource => _rowDragSource;

    /// <summary>起動時の割り付けと辞書の復元。Presenter の購読を張ってから 1 回だけ呼ぶ。</summary>
    public void Start() => Batch(() =>
    {
        _starting = true;
        ShowOverlay(new SearchViewModel(_state));
        ShowOverlay(new WordDetailViewModel(_state));
        if (_state.Browser.IsOpen) OpenBrowserTab();
        ApplySettings();
        if (Settings.LastDictionaryPath is { } last && _services.FileExists(last)) LoadDictionary(last);

        // 標準の枠が無いので OS は大きさを覚えてくれない。前回閉じたときの大きさをここで復元する。
        var s = Settings;
        Queue(new HostCommand.ApplyShellSize(s.WindowWidth, s.WindowHeight,
            s.WindowAspectLocked ? s.WindowWidth / s.WindowHeight : null, s.WindowMaximized));
        _starting = false;
    });

    /// <summary>唯一の入口。</summary>
    public void Dispatch(Intent intent) => Batch(() => Route(intent));

    /// <summary>ボタンの有効・無効。ShellPresenter が Can* を出し直すときに引く。</summary>
    public bool CanExecute(AppCommand command) => CommandGate.CanExecute(command, Gate(null));

    private bool CanExecute(AppCommand command, Word? rowItem) => CommandGate.CanExecute(command, Gate(rowItem));

    /// <summary>ツールバーとショートカットは選択中の単語を、行のメニューは渡された単語を見る。</summary>
    private GateContext Gate(Word? rowItem) => new(
        Shell, Doc, _state.IsGitHubMode, _gitHubBusy, _state.IsGitHubSynced, EditorOpen,
        _layout.Overlays.Select(o => o.Kind).ToHashSet(), (rowItem ?? _state.SelectedWord) is not null);

    /// <summary>編集中は辞書の差し替えを止める。保存の宛先だけが入れ替わるのを防ぐため。</summary>
    private bool EditorOpen => _layout.Overlays.Any(o => ScreenRegistry.For(o.Kind).BlocksDictionarySwap);

    private AppSettings Settings => _services.Settings;

    // ---- 裁定の単位 ---------------------------------------------------------------

    /// <summary>
    /// 裁定 1 回ぶんの枠。中で起きた変更は最後にまとめて出す（Changed は 1 回だけ、窓の操作は最後に）。
    /// 非同期処理の続き（await の後）もここを通す。
    /// ボタンの有効・無効は IsEnabled のバインドで、WPF はこれを自動では再評価しない。
    /// Changed を受けた ShellPresenter が出し直すので、Changed を漏らすとボタンが固まったままになる。
    /// </summary>
    private void Batch(Action body)
    {
        _batchDepth++;
        try
        {
            body();
        }
        finally
        {
            if (--_batchDepth == 0) Flush();
        }
    }

    private void Flush()
    {
        if (_persistRequested)
        {
            _persistRequested = false;
            _layout.Persist();
        }
        if (!_closing) SyncFloats();

        var hosts = _queuedHosts.ToArray();
        _queuedHosts.Clear();
        Changed?.Invoke(new StateChange(Shell, Drag, Doc));
        foreach (var command in hosts) HostCommandIssued?.Invoke(command);
    }

    private void Queue(HostCommand command) => _queuedHosts.Add(command);

    private void RequestPersist() => _persistRequested = true;

    /// <summary>浮き枠と窓を突き合わせる。中身の入った浮き枠には窓を開け、空になった・消えた浮き枠の窓は閉じる。
    /// 本体に HWND ができるまでは開けない（独立ウィンドウは本体を Owner に持つため）。</summary>
    private void SyncFloats()
    {
        if (!_shellReady) return;
        foreach (var host in _layout.Floats)
        {
            var shown = _shownFloats.Contains(host);
            if (host.HasItems && !shown)
            {
                _shownFloats.Add(host);
                Queue(new HostCommand.OpenFloating(host));
            }
            else if (!host.HasItems && shown)
            {
                _shownFloats.Remove(host);
                Queue(new HostCommand.CloseHost(host.Id));
            }
        }
        foreach (var gone in _shownFloats.Where(f => !_layout.Floats.Contains(f)).ToList())
        {
            _shownFloats.Remove(gone);
            Queue(new HostCommand.CloseHost(gone.Id));
        }
    }

    private void SetStatus(string status)
    {
        if (_state.Status == status) return;
        _state.Status = status;
        // 文言自体は変わったことに気づきにくいので、変わるたびに一瞬光らせる（起動時の復元は除く）。
        if (!_starting) Queue(new HostCommand.FlashStatus());
    }

    private void SetGitHubBusy(bool busy)
    {
        _gitHubBusy = busy;
        _state.IsGitHubBusy = busy;
    }

    // ---- 振り分け -------------------------------------------------------------------

    private void Route(Intent intent)
    {
        // 終了が決まった後は、窓が閉じていく途中で上がる Intent に反応しない
        // （独立ウィンドウの中身を本体へ移し替えると、割り付けが保存直前に崩れる）。
        if (_closing) return;
        // 設定ウィンドウの HostId は、そこから最初に Intent が上がった時点で覚える（前に出す・閉じるのに使う）。
        if (_settingsState is not null && RoleOf(intent) == HostRole.Settings) _settingsHostId = intent.Context.HostId;

        var payload = intent.Payload;
        switch (intent.Kind)
        {
            case IntentKind.AreaGripPressed or IntentKind.TabGripPressed or IntentKind.RowGripPressed
                or IntentKind.SplitGripPressed or IntentKind.PointerMoved or IntentKind.PointerReleased
                or IntentKind.PointerCaptureLost:
                StepDrag(intent);
                break;
            case IntentKind.CancelRequested:
                OnCancel(intent);
                break;
            case IntentKind.ZoomFontRequested:
                ZoomFont(payload as int? ?? 0);
                break;

            case IntentKind.TabSelectRequested:
                if (OverlayOf(intent) is { } selected && _layout.LeafOf(selected) is { } leaf) leaf.Selected = selected;
                break;
            case IntentKind.TabCloseRequested:
                if (OverlayOf(intent) is { IsPinned: false } closing) CloseOverlay(closing);
                break;
            case IntentKind.AreaCloseRequested:
                if (intent.Context.LeafId is { } areaId && _layout.LeafById(areaId) is { } area)
                {
                    _layout.Dissolve(area);
                    RequestPersist();
                }
                break;
            case IntentKind.OpenScreenRequested:
                if (payload is AppCommand screen) OpenScreen(screen);
                break;
            case IntentKind.FocusScreenRequested:
                if (payload is OverlayViewModel focus) FocusOverlay(focus);
                break;

            case IntentKind.OpenDictionaryRequested:
                if (CanExecute(AppCommand.Open)) OpenDictionary();
                break;
            case IntentKind.NewDictionaryRequested:
                if (CanExecute(AppCommand.NewDictionary)) NewDictionary();
                break;
            case IntentKind.SaveRequested:
                if (CanExecute(AppCommand.Save)) Save(false);
                break;
            case IntentKind.SaveAsRequested:
                if (CanExecute(AppCommand.SaveAs)) Save(true);
                break;

            case IntentKind.NewWordRequested:
                if (CanExecute(AppCommand.NewWord)) NewWord();
                break;
            case IntentKind.EditWordRequested:
                // 一覧から単語を指して開くときは、開いている編集を差し替える（同じ単語なら前に出すだけ）。
                // 選択中の単語を開くとき（ツールバー・ショートカット）は同じ画面を 2 枚にしない。
                if (payload is Word target) RequestEditWord(target);
                else if (CanExecute(AppCommand.EditWord)) EditWord(_state.SelectedWord);
                break;
            case IntentKind.DuplicateWordRequested:
                if (CanExecute(AppCommand.DuplicateWord, payload as Word)) DuplicateWord(payload as Word ?? _state.SelectedWord);
                break;
            case IntentKind.DeleteWordRequested:
                if (CanExecute(AppCommand.DeleteWord, payload as Word)) ConfirmDeleteWord(payload as Word ?? _state.SelectedWord);
                break;
            case IntentKind.SelectWordRequested:
                SelectWord(payload as Word);
                break;
            case IntentKind.FollowRelationRequested:
                FollowRelation(payload as Relation);
                break;
            case IntentKind.WordCommitted:
                if (OpenOf<WordEditViewModel>() is { } edited && edited.Validate()) CommitEdit(edited);
                break;
            case IntentKind.WordEditCancelled:
                if (OpenOf<WordEditViewModel>() is { } cancelled) CloseOverlay(cancelled);
                break;

            case IntentKind.ShowExamplesRequested:
                if (CanExecute(AppCommand.ShowExamples)) ShowExamples();
                break;
            case IntentKind.NewExampleRequested:
                if (CanExecute(AppCommand.EditExample)) { RememberExampleQuery(); ShowExampleEditor(null); }
                break;
            case IntentKind.EditExampleRequested:
                if (payload is Example example && CanExecute(AppCommand.EditExample)) { RememberExampleQuery(); ShowExampleEditor(example); }
                break;
            case IntentKind.DeleteExampleRequested:
                if (OpenOf<ExampleEditViewModel>() is { CanDelete: true } deleting) ConfirmDeleteExample(deleting);
                break;
            case IntentKind.ExampleCommitted:
                if (OpenOf<ExampleEditViewModel>() is { } committed && committed.Validate()) CommitExample(committed);
                break;
            case IntentKind.ExampleEditCancelled:
                if (OpenOf<ExampleEditViewModel>() is { } leaving) BackFromExampleEditor(leaving);
                break;
            case IntentKind.ExampleOfferFetchRequested:
                if (OpenOf<ExampleEditViewModel>() is { CanFetch: true } fetching) _ = FetchOfferAsync(fetching);
                break;
            case IntentKind.ZpdicApiKeySaveRequested:
                if (OpenOf<ExampleEditViewModel>() is { CanSaveApiKey: true } keyed) SaveApiKey(keyed);
                break;

            // 条件が変わったときだけ絞り直す。一覧を作り直すと選択中の行が外れるので、同じ条件の
            // 再通知（Enter での流し込み、選択中のチップの押し直し）では何もしない。
            case IntentKind.QueryChanged:
                if (_state.Query != _filteredQuery) ApplyFilter();
                break;
            case IntentKind.SearchModeChanged:
                if (payload is string mode && Enum.TryParse<SearchMode>(mode, out var m) && m != _state.SearchMode)
                {
                    _state.SearchMode = m;
                    ApplyFilter();
                }
                break;
            case IntentKind.SearchScopeChanged:
                if (payload is string scope && Enum.TryParse<SearchScope>(scope, out var sc) && sc != _state.SearchScope)
                {
                    _state.SearchScope = sc;
                    ApplyFilter();
                }
                break;
            case IntentKind.ClearQueryRequested:
                _state.Query = "";
                if (_filteredQuery != "") ApplyFilter();
                break;
            case IntentKind.FocusResultListRequested:
                Queue(new HostCommand.MoveFocus(FocusTarget.ResultList));
                break;
            case IntentKind.FocusQueryBoxRequested:
                Queue(new HostCommand.MoveFocus(FocusTarget.QueryBox));
                break;

            case IntentKind.BrowserNavigateRequested:
                var url = BrowserAddress.Normalize(_state.Browser.Address);
                _state.Browser.Address = url;
                _state.Browser.RequestNavigate(url);
                break;
            case IntentKind.BrowserBackRequested:
                if (_state.Browser.CanGoBack) _state.Browser.RequestBack();
                break;
            case IntentKind.BrowserForwardRequested:
                if (_state.Browser.CanGoForward) _state.Browser.RequestForward();
                break;
            case IntentKind.BrowserReloadRequested:
                _state.Browser.RequestReload();
                break;

            case IntentKind.ChangelogExportRequested:
                if (OpenOf<ChangelogViewModel>() is { } exported) ExportChangelog(exported.ChangelogPath);
                break;
            case IntentKind.ChangelogRelinkRequested:
                if (OpenOf<ChangelogViewModel>() is { } relinked && RelinkChangelog())
                {
                    CloseOverlay(relinked);
                    ShowOverlay(BuildChangelog());
                }
                break;

            case IntentKind.GitHubLoadRequested:
                if (CanExecute(AppCommand.GitHubLoad)) _ = LoadFromGitHubAsync();
                break;
            case IntentKind.GitHubCommitRequested:
                if (CanExecute(AppCommand.GitHubCommit)) ShowCommitDialog();
                break;
            case IntentKind.GitHubCommitConfirmed:
                ConfirmCommit();
                break;
            case IntentKind.GitHubTokenSaveRequested:
                if (_settingsState is { CanSaveGitHubToken: true } tokenForm) SaveGitHubToken(tokenForm);
                break;
            case IntentKind.GitHubTokenDeleteRequested:
                if (_settingsState is { HasGitHubToken: true } tokenOwner) DeleteGitHubToken(tokenOwner);
                break;

            case IntentKind.ConfirmChoiceSelected:
                Choose(payload as int? ?? -1);
                break;
            case IntentKind.ModalDismissRequested:
                if (_modal) DismissModal();
                break;

            case IntentKind.SettingsRequested:
                if (CanExecute(AppCommand.ShowSettings)) ShowSettings();
                break;
            case IntentKind.SettingsApplyRequested:
                if (_settingsState is { } applied) ApplySettingsForm(applied, intent.Context.HostId);
                break;
            case IntentKind.PickHeksaFontRequested:
                if (_settingsState is { } fontForm
                    && _services.PickOpenPath(Strings.Settings_PickFontDialogTitle, Strings.Settings_FontFilter) is { } font)
                    fontForm.HeksaFontPath = font;
                break;
            case IntentKind.SettingsModeSelected:
                if (_settingsState is { } modeForm && payload is string em && Enum.TryParse<EditMode>(em, out var editMode))
                    modeForm.Mode = editMode;
                break;
            case IntentKind.SettingsReciprocalResetRequested:
                _settingsState?.ResetReciprocal();
                break;

            case IntentKind.TranslationRowAddRequested:
                OpenOf<WordEditViewModel>()?.AddTranslationRow();
                break;
            case IntentKind.TranslationRowRemoveRequested:
                if (payload is TranslationRow tr) OpenOf<WordEditViewModel>()?.RemoveTranslationRow(tr);
                break;
            case IntentKind.ContentRowAddRequested:
                if (payload is string contentType) OpenOf<WordEditViewModel>()?.AddContentType(contentType);
                break;
            case IntentKind.ContentRowRemoveRequested:
                if (payload is ContentRow cr) OpenOf<WordEditViewModel>()?.RemoveContentRow(cr);
                break;
            case IntentKind.VariationRowAddRequested:
                OpenOf<WordEditViewModel>()?.AddVariationRow();
                break;
            case IntentKind.VariationRowRemoveRequested:
                if (payload is VariationRow vr) OpenOf<WordEditViewModel>()?.RemoveVariationRow(vr);
                break;
            case IntentKind.RelationRowAddRequested:
                if (payload is Word relationTarget) OpenOf<WordEditViewModel>()?.AddRelation(relationTarget);
                break;
            case IntentKind.RelationRowRemoveRequested:
                if (payload is RelationRow rr) OpenOf<WordEditViewModel>()?.RemoveRelationRow(rr);
                break;
            case IntentKind.ExampleWordAddRequested:
                if (payload is Word exampleWord) OpenOf<ExampleEditViewModel>()?.AddWord(exampleWord);
                break;
            case IntentKind.ExampleWordRemoveRequested:
                if (payload is ExampleWord ew) OpenOf<ExampleEditViewModel>()?.RemoveWord(ew);
                break;

            case IntentKind.WindowCloseRequested:
                OnWindowClose(intent);
                break;
            case IntentKind.WindowBoundsChanged:
                OnWindowBounds(intent);
                break;
            case IntentKind.WindowActivated:
                if (RoleOf(intent) is HostRole.Shell)
                {
                    _shellHostId = intent.Context.HostId;
                    _shellReady = true;
                }
                break;
            case IntentKind.WindowStateChanged:
                if (RoleOf(intent) is HostRole.Shell && payload is WindowState ws) _shellWindowState = ws;
                break;
            case IntentKind.StreamWindowToggleRequested:
                _streamOpen = !_streamOpen;
                Queue(new HostCommand.ToggleStream(_streamOpen));
                break;
            case IntentKind.CountWindowToggleRequested:
                _countOpen = !_countOpen;
                Queue(new HostCommand.ToggleCount(_countOpen));
                break;

            case IntentKind.PopupLayerOpened:
                // 値選択のプルダウンと階層メニューは見た目も役割も別だが、同時に開いていると紛らわしいので
                // どちらか一方だけにする（機能上の制約ではない）。
                if (_popupLayers > 0) Queue(new HostCommand.ClosePopupLayer());
                if (_popupLayers++ == 0) Queue(new HostCommand.SetPopupLayerOpen(true));
                break;
            case IntentKind.PopupLayerClosed:
                if (_popupLayers > 0 && --_popupLayers == 0) Queue(new HostCommand.SetPopupLayerOpen(false));
                break;
            case IntentKind.RowMenuOpening:
                break;

            case IntentKind.UnhandledExceptionRaised:
                if (payload is Exception ex) ShowException(ex);
                break;
        }
    }

    private HostRole? RoleOf(Intent intent)
        => intent.Context.HostId is { } id ? HostLookup?.Invoke(id)?.Role : null;

    /// <summary>Intent が指すタブ。Payload にタブそのもの、無ければ Context の種類名から引く（同じ種類は 1 枚まで）。</summary>
    private OverlayViewModel? OverlayOf(Intent intent)
        => intent.Payload as OverlayViewModel ?? OverlayByKind(intent.Context.TabKind);

    private OverlayViewModel? OverlayByKind(string? kind)
        => kind is null ? null : _layout.Overlays.FirstOrDefault(o => o.Kind == kind);

    private T? OpenOf<T>() where T : OverlayViewModel => _layout.Overlays.OfType<T>().FirstOrDefault();

    // ---- ドラッグ -----------------------------------------------------------------

    private void StepDrag(Intent intent)
    {
        var ctx = intent.Context;
        var gripId = _drag.Phase == DragPhase.Idle ? ctx.LeafId : _drag.GripLeafId;
        var grip = gripId is { } g ? _layout.LeafById(g) : null;
        var siblings = grip?.Parent?.Other(grip).Leaves.Select(l => l.Id).ToList() ?? new List<int>();
        var hit = intent.Kind == IntentKind.PointerMoved && intent.ScreenPoint is { } screen ? HitTester?.Invoke(screen) : null;
        var sourceItems = intent.Kind == IntentKind.TabGripPressed && ctx.LeafId is { } s ? _layout.LeafById(s)?.Items.Count ?? 0 : 0;

        var env = new DragEnvironment(hit, ctx.LeafSize ?? default, ctx.LeafLocalPoint ?? default, siblings,
            _layout.AllLeaves.Count(), LayoutRules.MaxLeaves, DockSplit.MinLeafSize, EdgeZone,
            DragThreshold.Width, DragThreshold.Height, sourceItems);
        var step = DragTransitions.Step(_drag, intent, env);

        if (intent.Kind == IntentKind.RowGripPressed && step.Next.Phase == DragPhase.RowGripArmed) _rowDragSource = intent.Payload;
        _drag = step.Next;
        // 下見・着色を先に消してから木を組み替える（掴んだまま組み替えると掴みが解けなくなる）。
        // 遷移表が Clear* → Commit* の順で副作用を並べているので、その順に 1 つずつ適用する。
        foreach (var effect in step.Effect.Flat()) Apply(effect);
        if (_drag.Phase == DragPhase.Idle) _rowDragSource = null;
    }

    private void Apply(DragEffect effect)
    {
        switch (effect)
        {
            case DragEffect.CommitSplit c when _layout.LeafById(c.LeafId) is { } leaf:
                _layout.Split(leaf, c.Preview.Axis, c.Preview.Ratio, c.Preview.NewIsSecond);
                break;
            case DragEffect.CommitJoin c when _layout.LeafById(c.SurvivorLeafId) is { } leaf:
                _layout.Join(leaf);
                break;
            case DragEffect.CommitMove c when OverlayByKind(c.TabKind) is { } vm && _layout.LeafById(c.TargetLeafId) is { } leaf:
                _layout.Move(vm, leaf);
                break;
            case DragEffect.CommitSplitThenMove c when OverlayByKind(c.TabKind) is { } vm && _layout.LeafById(c.LeafId) is { } leaf:
                // 上限で割れなければ、割らずにその枠へタブとして合流させる。
                var target = _layout.Split(leaf, c.Preview.Axis, c.Preview.Ratio, c.Preview.NewIsSecond) ?? leaf;
                _layout.Move(vm, target);
                break;
            case DragEffect.CommitFloat c when OverlayByKind(c.TabKind) is { } vm:
                _layout.Float(vm, c.AtDip);
                break;
            case DragEffect.CommitResize c when _layout.SplitById(c.SplitId) is { } split:
                _layout.Resize(split, c.Change, c.Total);
                break;
            case DragEffect.Persist:
                RequestPersist();
                break;
            case DragEffect.ShowGhost g:
                Queue(new HostCommand.ShowDragGhost(OverlayByKind(g.TabKind)?.Title ?? g.TabKind, g.AtDip));
                break;
            case DragEffect.HideGhost:
                Queue(new HostCommand.HideDragGhost());
                break;
            case DragEffect.CommitSplit or DragEffect.CommitJoin or DragEffect.CommitMove
                or DragEffect.CommitSplitThenMove or DragEffect.CommitFloat or DragEffect.CommitResize:
                // 指している枠・タブがもう無い（閉じた・畳んだ）。木は組み替えない。
                break;
            default:
                DragEffectIssued?.Invoke(effect);
                break;
        }
    }

    // ---- Esc ------------------------------------------------------------------------

    private void OnCancel(Intent intent)
    {
        var role = RoleOf(intent) ?? HostRole.Shell;
        var floatTab = role == HostRole.Floating ? ActiveTabIn(intent.Context.HostId) : null;
        var closable = role switch
        {
            HostRole.Shell => ActiveOverlay is not null,
            HostRole.Floating => floatTab is not null,
            _ => false,
        };

        switch (EscRouter.Resolve(_popupLayers > 0, _drag.Phase, Shell, role, closable))
        {
            case EscRouter.EscTarget.ClosePopupLayer:
                Queue(new HostCommand.ClosePopupLayer());
                break;
            case EscRouter.EscTarget.CancelDrag:
                StepDrag(intent);
                break;
            case EscRouter.EscTarget.DismissModal:
                DismissModal();
                break;
            case EscRouter.EscTarget.CloseActiveTabInShell:
                if (ActiveOverlay is { } active) CloseOverlay(active);
                break;
            case EscRouter.EscTarget.CloseActiveTabInHost:
                if (role == HostRole.Settings) CloseSettings(intent.Context.HostId);
                else if (floatTab is not null) CloseOverlay(floatTab);
                break;
        }
    }

    /// <summary>本体の窓で Esc が閉じる相手。持ち出したタブはその窓の Esc が閉じるので飛ばす。</summary>
    private OverlayViewModel? ActiveOverlay
        => _recent.FirstOrDefault(vm => _layout.LeafOf(vm) is not null && _layout.FloatOf(vm) is null);

    /// <summary>独立ウィンドウで Esc が閉じる相手。その窓に出ているタブのうち据え置きでないもの。</summary>
    private OverlayViewModel? ActiveTabIn(Guid? hostId)
        => hostId is { } id && _layout.FloatById(id) is { } host
            ? host.Leaves.FirstOrDefault(l => l.Selected is { IsPinned: false })?.Selected
            : null;

    private void Touch(OverlayViewModel? vm)
    {
        // 据え置きのタブは閉じられないので、Esc の行き先（＝最後に触ったタブ）にも入れない。
        if (vm is null || vm.IsPinned) return;
        _recent.Remove(vm);
        _recent.Insert(0, vm);
    }

    // ---- 確認ダイアログ -------------------------------------------------------------

    /// <summary>確認ダイアログを出す。選択肢に動作を持たせず、選ばれた番号に対応する裁定をここで保留する。</summary>
    private void Confirm(string title, string message, params (string Label, bool IsDanger, Action? Then)[] choices)
    {
        _pendingChoices = choices.Select(c => c.Then).ToArray();
        _modal = true;
        ConfirmRequested?.Invoke(new ConfirmSpec(title, message,
            choices.Select(c => new ConfirmChoice(c.Label, c.IsDanger)).ToList()));
    }

    /// <summary>確認ダイアログの層に確認以外の画面（コミットメッセージの編集）を出す。</summary>
    private void ShowModal(OverlayViewModel vm)
    {
        _pendingChoices = null;
        _modal = true;
        _state.ModalOverlay = vm;
    }

    private void DismissModal()
    {
        _pendingChoices = null;
        _modal = false;
        ConfirmRequested?.Invoke(null);
    }

    private void Choose(int index)
    {
        if (!_modal) return;
        var then = _pendingChoices is { } p && index >= 0 && index < p.Length ? p[index] : null;
        DismissModal();
        then?.Invoke();
    }

    // UI スレッドで漏れた例外はアプリを落とさず、OBS に映るオーバーレイで知らせる。
    // MessageBox は別ウィンドウになるため使わない。
    private void ShowException(Exception ex)
    {
        try
        {
            Confirm(Strings.Error_Title,
                string.Format(Strings.Error_Message, ex.GetType().Name, ex.Message, ErrorLog.FilePath),
                (Strings.Common_Close, false, null));
        }
        catch (Exception overlayEx)
        {
            // オーバーレイを描くこと自体が失敗する状態ではこれ以上出さず、記録だけ残す。
            _services.LogError("ErrorOverlay", overlayEx);
        }
    }

    // ---- 画面（タブ）の開閉 -------------------------------------------------------------

    private void ShowOverlay(OverlayViewModel vm)
    {
        _layout.Add(vm);
        Touch(vm);
        RequestPersist();
    }

    private void CloseOverlay(OverlayViewModel vm)
    {
        // ブラウザは次の起動で開き直すかどうかを覚えているので、タブを閉じたことを記憶にも伝える。
        if (vm is BrowserTabViewModel) DeactivateBrowser();
        _layout.Remove(vm);
        _recent.Remove(vm);
        RequestPersist();
    }

    private void OpenScreen(AppCommand screen)
    {
        if (!CanExecute(screen)) return;
        switch (screen)
        {
            case AppCommand.ShowBrowser:
                OpenBrowserTab();
                break;
            case AppCommand.ShowSettings:
                ShowSettings();
                break;
            case AppCommand.ShowExamples:
                ShowExamples();
                break;
            case AppCommand.ShowDialectTool:
                ShowTool(() => new DialectToolViewModel(_state.SelectedWord?.Form));
                break;
            case AppCommand.ShowIpaTool:
                ShowTool(() => new IpaToolViewModel());
                break;
            case AppCommand.ShowStats:
                ShowTool(() => new StatsViewModel(_doc));
                break;
            case AppCommand.ShowLegend:
                ShowTool(() => new LegendViewModel(BuildLegendMarkdown()));
                break;
            case AppCommand.ShowChangelog:
                ShowTool(BuildChangelog);
                break;
        }
    }

    /// <summary>
    /// ツール類を開く。すでに開いていれば作り直さず、そのタブを表に出す（見ている位置や
    /// 打ちかけの入力を捨てないため）。独立ウィンドウにいるならその窓を前に出す。
    /// </summary>
    private void ShowTool<T>(Func<T> create) where T : OverlayViewModel
    {
        if (OpenOf<T>() is not { } open)
        {
            open = create();
            ShowOverlay(open);
        }
        else if (_layout.LeafOf(open) is { } leaf) leaf.Selected = open;
        FocusOverlay(open);
    }

    /// <summary>そのタブがいる窓を前に出す。</summary>
    private void FocusOverlay(OverlayViewModel vm)
    {
        if (_layout.FloatOf(vm) is { } host) Queue(new HostCommand.FocusHost(host.Id));
        else if (_shellHostId is { } shell) Queue(new HostCommand.FocusHost(shell));
    }

    /// <summary>
    /// ブラウザのタブを開く。中身の WebView2 は初期化を遅らせてあるので、
    /// 枠に並べてから起こす。
    /// </summary>
    private void OpenBrowserTab()
    {
        ShowOverlay(new BrowserTabViewModel(_state, _state.Browser));
        if (!_state.Browser.IsOpen)
        {
            _state.Browser.IsOpen = true;
            Settings.BrowserVisible = true;
            _services.SaveSettings();
        }
        _state.Browser.RequestInitialize();
    }

    /// <summary>次の起動で開き直すかどうかの記憶だけを落とす。</summary>
    private void DeactivateBrowser()
    {
        if (!_state.Browser.IsOpen) return;
        _state.Browser.IsOpen = false;
        Settings.BrowserVisible = false;
        _services.SaveSettings();
    }

    // ---- 文字サイズ -----------------------------------------------------------------

    /// <summary>Ctrl＋ホイールひと目盛りぶん文字サイズを増減する。動かす値は設定画面の倍率そのもの
    /// なので、上下限も設定の適用と揃えてある。設定の再適用（ApplySettings）は通さない。
    /// あちらは Heksa フォントをファイルから読み直すため、ホイールの連打で毎回走らせるには重い。</summary>
    private void ZoomFont(int steps)
    {
        var scale = Math.Clamp(Math.Round(Settings.FontScale + steps * 0.1, 1), MinFontScale, MaxFontScale);
        if (Math.Abs(scale - Settings.FontScale) < 0.001) return;

        Settings.FontScale = scale;
        _services.SaveSettings();
        FontScaleState.Instance.Scale = scale;
        _state.RaiseSettingsChanged();
        SetStatus(string.Format(Strings.Main_FontScaleStatus, $"{scale * 100:0}"));
    }

    private const double MinFontScale = 0.6;
    private const double MaxFontScale = 3.0;

    // ---- 窓 -----------------------------------------------------------------------

    private void OnWindowClose(Intent intent)
    {
        var hostId = intent.Context.HostId;
        switch (RoleOf(intent))
        {
            case HostRole.Shell:
                _shellHostId = hostId;
                if (intent.Payload is WindowBounds bounds) _shellBounds = bounds;
                if (Doc == DocPhase.Dirty)
                {
                    Confirm(Strings.App_UnsavedCloseTitle, string.Format(Strings.App_UnsavedCloseMessage, _state.DictionaryName),
                        (Strings.App_SaveAndExit, false, () => { Save(false); BeginClosing(); }),
                        (Strings.App_ExitWithoutSaving, true, BeginClosing),
                        (Strings.Common_KeepEditing, false, null));
                }
                else BeginClosing();
                break;

            case HostRole.Floating:
                if (hostId is { } id && _layout.FloatById(id) is { } host)
                {
                    // 手で閉じた独立ウィンドウ。中のタブは閉じ、閉じられない据え置きのタブ（検索・単語詳細）は
                    // 本体へ引き取る。位置の記憶ごと浮き枠を落とす（窓は裁定の最後に閉じる）。
                    foreach (var vm in host.Items.Where(vm => !vm.IsPinned).ToList()) CloseOverlay(vm);
                    _layout.Discard(host);
                    RequestPersist();
                }
                else if (hostId is { } orphan) Queue(new HostCommand.CloseHost(orphan));
                break;

            case HostRole.Settings:
                CloseSettings(hostId);
                break;

            case HostRole.Stream:
                _streamOpen = false;
                Queue(new HostCommand.ToggleStream(false));
                break;

            case HostRole.Count:
                _countOpen = false;
                Queue(new HostCommand.ToggleCount(false));
                break;
        }
    }

    private void OnWindowBounds(Intent intent)
    {
        switch (RoleOf(intent))
        {
            case HostRole.Floating when intent.Payload is Rect rect && intent.Context.HostId is { } id && _layout.FloatById(id) is { } host:
                // 独立ウィンドウの位置と大きさは動かすたび浮き枠に控えておくだけで、次の書き戻しで設定に残る。
                host.Bounds = rect;
                break;
            case HostRole.Shell when intent.Payload is WindowBounds bounds:
                _shellBounds = bounds;
                break;
        }
    }

    /// <summary>終了。割り付けと窓の大きさを保存してから、すべての窓を閉じる。
    /// 独立ウィンドウの中身は本体へ移し替えない（割り付けが保存直前に崩れるため）。</summary>
    private void BeginClosing()
    {
        if (_shellBounds is { } b)
        {
            Settings.WindowWidth = b.Width;
            Settings.WindowHeight = b.Height;
            Settings.WindowMaximized = b.Maximized;
        }
        // 独立ウィンドウの位置と大きさは浮き枠に控えてあるだけなので、ここで設定ごと書き出す。
        _persistRequested = false;
        _layout.Persist();

        _closing = true;
        foreach (var host in _shownFloats) Queue(new HostCommand.CloseHost(host.Id));
        _shownFloats.Clear();
        if (_streamOpen) Queue(new HostCommand.ToggleStream(false));
        if (_countOpen) Queue(new HostCommand.ToggleCount(false));
        if (_settingsHostId is { } settings) Queue(new HostCommand.CloseHost(settings));
        if (_shellHostId is { } shell) Queue(new HostCommand.CloseHost(shell));
    }

    // ---- 設定 -----------------------------------------------------------------------

    /// <summary>設定は他の画面と違い独立ウィンドウで開く。二重には開かない（開いていれば前に出す）。</summary>
    private void ShowSettings()
    {
        if (_settingsState is not null)
        {
            if (_settingsHostId is { } open) Queue(new HostCommand.FocusHost(open));
            return;
        }
        _settingsState = new SettingsViewModel(Settings, _doc, Choices.Current.Relations, _services.HasGitHubToken);
        Queue(new HostCommand.OpenSettings(_settingsState));
    }

    private void CloseSettings(Guid? hostId)
    {
        _settingsState = null;
        var id = hostId ?? _settingsHostId;
        _settingsHostId = null;
        if (id is { } close) Queue(new HostCommand.CloseHost(close));
    }

    // MainWindow の MinWidth / MinHeight（XAML）と同じ下限。ここより小さい値は
    // ウィンドウ側で結局弾かれるだけなので、適用前にクランプして食い違いを見せない。
    private const double MinWindowWidth = 900;
    private const double MinWindowHeight = 600;

    private void ApplySettingsForm(SettingsViewModel form, Guid? hostId)
    {
        var s = Settings;
        s.Language = form.Language;
        s.SortOrder = string.IsNullOrWhiteSpace(form.SortOrder) ? TextProcessor.DefaultSortOrder : form.SortOrder;
        s.FontScale = Math.Clamp(form.FontScale, MinFontScale, MaxFontScale);
        s.AutoSave = form.AutoSave;
        s.WindowWidth = Math.Max(form.WindowWidth, MinWindowWidth);
        s.WindowHeight = Math.Max(form.WindowHeight, MinWindowHeight);
        s.WindowAspectLocked = form.WindowAspectLocked;
        s.HeksaEnabled = form.HeksaEnabled;
        s.HeksaFontPath = string.IsNullOrWhiteSpace(form.HeksaFontPath) ? null : form.HeksaFontPath;

        s.Mode = form.Mode;
        s.GitHubOwner = string.IsNullOrWhiteSpace(form.GitHubOwner) ? null : form.GitHubOwner.Trim();
        s.GitHubRepo = string.IsNullOrWhiteSpace(form.GitHubRepo) ? null : form.GitHubRepo.Trim();
        s.GitHubBranch = string.IsNullOrWhiteSpace(form.GitHubBranch) ? "main" : form.GitHubBranch.Trim();
        s.GitHubJsonPath = string.IsNullOrWhiteSpace(form.GitHubJsonPath) ? null : form.GitHubJsonPath.Trim();
        s.GitHubChangelogPath = string.IsNullOrWhiteSpace(form.GitHubChangelogPath) ? null : form.GitHubChangelogPath.Trim();

        var map = new Dictionary<string, string>();
        foreach (var line in form.ReciprocalText.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            var i = t.IndexOf('=');
            if (i <= 0) continue;
            map[t[..i].Trim()] = t[(i + 1)..].Trim();
        }
        // 対照表だけは choices.json 側の持ち物なので、設定の保存とは別に書き戻す。空なら書き戻さない。
        if (map.Count > 0) _services.SaveRelations(map);

        s.StreamBackground = form.StreamBackground;
        s.StreamFontScale = Math.Clamp(form.StreamFontScale, 1.0, 6.0);
        s.StreamWindowTopmost = form.StreamTopmost;
        s.StreamShowTranslations = form.StreamShowTranslations;
        s.StreamShowContents = form.StreamShowContents;

        s.BrowserVisible = form.BrowserVisible;
        s.BrowserStartUrl = string.IsNullOrWhiteSpace(form.BrowserStartUrl) ? "" : form.BrowserStartUrl.Trim();

        if (_doc is not null)
        {
            _doc.ZpdicOnline["punctuations"] = new JsonArray(
                form.Punctuations.Select(c => (JsonNode)JsonValue.Create(c.ToString())!).ToArray());
            _doc.ZpdicOnline["ignoredPattern"] = form.IgnoredPattern;
        }

        _services.SaveSettings();
        ApplySettings();
        RebuildIndex();
        // 配信用ウィンドウは設定から背景色などを読むので張り直させる。本体の窓は、最大化中は
        // Width/Height を上書きすると元に戻したときの大きさが狂うため、通常時だけ大きさを反映する。
        // 固定する比率は既存の窓の実寸ではなく、設定欄に入っている幅・高さから決め直す。
        Queue(new HostCommand.RefreshStreamHosts());
        var normal = _shellWindowState == WindowState.Normal;
        Queue(new HostCommand.ApplyShellSize(
            normal ? s.WindowWidth : double.NaN, normal ? s.WindowHeight : double.NaN,
            s.WindowAspectLocked ? s.WindowWidth / s.WindowHeight : null));
        SetStatus(Strings.Settings_AppliedStatus);
        CloseSettings(hostId);
    }

    private void SaveGitHubToken(SettingsViewModel form)
    {
        try
        {
            _services.SaveGitHubToken(form.GitHubTokenInput);
            form.GitHubTokenInput = "";
            form.GitHubTokenStatus = Strings.Settings_TokenSaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            form.GitHubTokenStatus = string.Format(Strings.Settings_TokenSaveFailed, ex.Message);
        }
        form.HasGitHubToken = _services.HasGitHubToken;
    }

    private void DeleteGitHubToken(SettingsViewModel form)
    {
        _services.DeleteGitHubToken();
        form.GitHubTokenStatus = Strings.Settings_TokenDeleted;
        form.HasGitHubToken = _services.HasGitHubToken;
    }

    // ---- メニュー -------------------------------------------------------------------

    /// <summary>バーが手狭なので、開く・新規辞書・保存・別名で保存は階層メニューにまとめてある。</summary>
    public IReadOnlyList<MenuActionSpec> BuildFileMenu() => new[]
    {
        new MenuActionSpec(Strings.Menu_Open, "Ctrl+O", IntentKind.OpenDictionaryRequested, null, CanExecute(AppCommand.Open), false, true),
        new MenuActionSpec(Strings.Menu_NewDictionary, "", IntentKind.NewDictionaryRequested, null, CanExecute(AppCommand.NewDictionary), false, true),
        new MenuActionSpec(Strings.Common_Save, "Ctrl+S", IntentKind.SaveRequested, null, CanExecute(AppCommand.Save), true, true),
        new MenuActionSpec(Strings.Menu_SaveAs, "Ctrl+Shift+S", IntentKind.SaveAsRequested, null, CanExecute(AppCommand.SaveAs), false, true),
    };

    /// <summary>統計・凡例・更新履歴・方言変換・IPA→綴りは、常設のタブ枠を割かないよう独立ウィンドウで開く。
    /// すでに開いていれば作り直さず、そのタブを表に出すだけ（項目はグレーアウトさせない）。</summary>
    public IReadOnlyList<MenuActionSpec> BuildToolsMenu() => new[]
    {
        Tool(Strings.Dialect_Title, AppCommand.ShowDialectTool),
        Tool(Strings.Ipa_Title, AppCommand.ShowIpaTool),
        Tool(Strings.Stats_Title, AppCommand.ShowStats),
        Tool(Strings.Legend_Title, AppCommand.ShowLegend),
        Tool(Strings.Changelog_Title, AppCommand.ShowChangelog),
    };

    private MenuActionSpec Tool(string header, AppCommand command)
        => new(header, "", IntentKind.OpenScreenRequested, command, CanExecute(command), false, true);

    /// <summary>配信用の独立ウィンドウ。開いているものは「閉じる」に変えて出す。</summary>
    public IReadOnlyList<MenuActionSpec> BuildWindowMenu() => new[]
    {
        new MenuActionSpec(_streamOpen ? Strings.Window_StreamMenuClose : Strings.Window_StreamMenuOpen,
            Strings.Window_StreamMenuTooltip, IntentKind.StreamWindowToggleRequested, null, true, false, true),
        new MenuActionSpec(_countOpen ? Strings.Window_CountMenuClose : Strings.Window_CountMenuOpen,
            Strings.Window_CountMenuTooltip, IntentKind.CountWindowToggleRequested, null, true, false, true),
    };

    /// <summary>一覧の行の ⋯。その行の単語に効く編集・複製・削除。</summary>
    public IReadOnlyList<MenuActionSpec> BuildRowMenu(object? rowItem)
    {
        if (rowItem is not Word w) return Array.Empty<MenuActionSpec>();
        return new[]
        {
            new MenuActionSpec(Strings.Common_Edit, "", IntentKind.EditWordRequested, w, CanExecute(AppCommand.EditWord, w), false, true),
            new MenuActionSpec(Strings.Common_Duplicate, "", IntentKind.DuplicateWordRequested, w, CanExecute(AppCommand.DuplicateWord, w), false, true),
            new MenuActionSpec(Strings.Common_Delete, "", IntentKind.DeleteWordRequested, w, CanExecute(AppCommand.DeleteWord, w), false, true),
        };
    }
}

/// <summary>
/// メニュー 1 項目の描画パラメータ。押されたら Intent が上がるだけで Command は持たない。
/// プロパティ名は Themes/Theme.xaml の MenuActionTemplate が参照する名前と揃えてある。
/// </summary>
public sealed record MenuActionSpec(
    string Header, string ToolTip, IntentKind Intent, object? Payload,
    bool IsEnabled, bool IsPrimary, bool IsVisible);
