using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

/// <summary>辞書・単語・例文・GitHub・更新履歴の流れ（DocPhase の遷移）。</summary>
public sealed partial class AppMediator
{
    private readonly List<ChangeEntry> _pendingChanges = new();

    private OtmDocument? _doc;
    private TextProcessor _text = new(TextProcessor.DefaultSortOrder, "");
    private SearchService _search;
    private RelationService _relations;

    /// <summary>例文一覧の絞り込み文字列。編集に入って戻ってきても打ち直さずに済むよう覚えておく。</summary>
    private string _exampleQuery = "";

    /// <summary>コミットメッセージの編集画面を出している間だけ持つ送り先。</summary>
    private GitHubTarget? _commitTarget;

    // ---- 辞書 -------------------------------------------------------------------------

    private void NewDictionary()
    {
        _doc = _services.CreateEmpty();
        Settings.LastDictionaryPath = null;
        _state.SelectedWord = null;
        _state.Document = _doc;
        RebuildIndex();
        _state.IsDirty = true;
        _state.IsGitHubSynced = false;
        ReloadLegends();
        SetStatus(Strings.Main_NewDictionaryCreated);
        RaiseDocumentChanged();
    }

    private void OpenDictionary()
    {
        // ファイルダイアログだけは OS のウィンドウ。OBS ではこの瞬間だけ映らない。
        if (_services.PickOpenPath(Strings.Main_OpenDialogTitle, Strings.Main_OtmFilter) is { } path) LoadDictionary(path);
    }

    private void LoadDictionary(string path)
    {
        var result = _services.Load(path);
        if (!result.Ok || result.Document is null)
        {
            SetStatus(string.Format(Strings.Main_LoadFailedStatus, result.Message));
            Confirm(Strings.Main_LoadFailedTitle, $"{path}{Environment.NewLine}{result.Message}", (Strings.Common_Close, false, null));
            return;
        }

        _doc = result.Document;
        Settings.LastDictionaryPath = path;
        _services.SaveSettings();
        _state.SelectedWord = null;
        _pendingChanges.Clear();
        _exampleQuery = "";
        _state.Document = _doc;
        ApplySettings();
        RebuildIndex();
        _state.IsDirty = false;
        // ここで読み込んだファイルがGitHub側と一致しているとはまだ分からない。
        // GitHubから読み込んだ直後は LoadFromGitHubCoreAsync がこの直後に true へ戻す。
        _state.IsGitHubSynced = false;

        // 開いたままの更新履歴は、辞書が入れ替わると連携先の CSV ごと変わるので引き直す。
        var csv = ChangelogCsvPath();
        foreach (var vm in _layout.Overlays.OfType<ChangelogViewModel>().ToList())
            vm.Reload(ReadChangelogRows(csv), csv);
        ReloadLegends();

        SetStatus(string.Format(Strings.Main_LoadedStatus, Path.GetFileName(path)));
        RaiseDocumentChanged();
    }

    private void Save(bool forceNewPath)
    {
        if (_doc is null) return;
        var path = _doc.Path;
        if (forceNewPath || path is null)
        {
            path = _services.PickSavePath(Strings.Main_SaveDialogTitle, "OTM-JSON (*.json)|*.json",
                path is null ? "dictionary.json" : Path.GetFileName(path));
            if (path is null) return;
        }

        var result = _services.Save(_doc, path);
        if (!result.Ok)
        {
            SetStatus(string.Format(Strings.Main_SaveFailedStatus, result.Message));
            Confirm(Strings.Main_SaveFailedTitle, result.Message, (Strings.Common_Close, false, null));
            return;
        }

        FlushChangelog();
        Settings.LastDictionaryPath = path;
        _services.SaveSettings();
        _state.IsDirty = false;
        SetStatus(string.Format(Strings.Main_SavedStatus, Path.GetFileName(path)));
        RaiseDocumentChanged();
    }

    /// <summary>更新履歴は保存が成功したときだけ CSV に書く。</summary>
    private void FlushChangelog()
    {
        if (_doc?.Path is null || _pendingChanges.Count == 0) return;
        var csv = Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path);
        try
        {
            _services.AppendChangelog(csv, _pendingChanges.ToList());
            _pendingChanges.Clear();
        }
        catch (IOException ex)
        {
            // 追記に失敗しても未保存の履歴は捨てず、次回保存で再試行する。
            _services.LogError($"更新履歴の追記 ({csv})", ex);
            SetStatus(string.Format(Strings.Main_ChangelogAppendFailedStatus, ex.Message));
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

        // 更新履歴の画面を開いたままだと ChangelogViewModel はスナップショットのままなので、
        // 開いていればその場で引き直す（閉じていればどうせ次に開いたときに最新化される）。
        foreach (var vm in _layout.Overlays.OfType<ChangelogViewModel>().ToList())
            vm.Refresh(ReadChangelogRows(vm.ChangelogPath));
    }

    /// <summary>保留中の更新履歴（まだ CSV に書いていないもの）。</summary>
    internal IReadOnlyList<ChangeEntry> PendingChanges => _pendingChanges;

    // ---- 索引・絞り込み ----------------------------------------------------------------

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

        var heksa = Settings.HeksaEnabled ? _services.LoadHeksaFont(Settings.HeksaFontPath) : null;
        HeadwordFontState.Instance.Family = heksa ?? HeadwordFontState.Fallback;
        FontScaleState.Instance.Scale = Settings.FontScale;
        _state.Browser.SyncWithSettings();   // 幅と開始 URL。表示中のページは切り替えない

        _state.RaiseSettingsChanged();
        Queue(new HostCommand.RefreshStreamHosts());
    }

    private void RebuildIndex()
    {
        if (_doc is null) { _state.FilteredWords.Clear(); _state.RaiseCounts(); return; }

        var sorted = _doc.Words.OrderBy(w => w, _text.WordComparer).ToList();
        TextProcessor.AssignHomonymIndexes(sorted);

        _doc.Words.Clear();
        foreach (var w in sorted) _doc.Words.Add(w);

        ApplyFilter();
    }

    /// <summary>いま一覧に出している絞り込みの検索文字列。</summary>
    private string? _filteredQuery;

    private void ApplyFilter()
    {
        _filteredQuery = _state.Query;
        _state.FilteredWords.Clear();
        if (_doc is null) { _state.RaiseCounts(); return; }
        foreach (var w in _search.Filter(_doc.Words, _state.Query, _state.SearchMode, _state.SearchScope))
            _state.FilteredWords.Add(w);
        _state.RaiseCounts();
    }

    private void RaiseDocumentChanged()
    {
        RefreshRelatedExamples();
        _state.RaiseDocumentChanged();
    }

    private void RefreshRelatedExamples()
    {
        _state.RelatedExamples.Clear();
        if (_doc is null || _state.SelectedWord is null) return;
        _doc.ResolveExampleForms();
        foreach (var e in _doc.ExamplesFor(_state.SelectedWord.Id)) _state.RelatedExamples.Add(e);
    }

    private void SelectWord(Word? word)
    {
        _state.SelectedWord = word;
        RefreshRelatedExamples();
    }

    // ---- 単語 -------------------------------------------------------------------------

    /// <summary>検索欄の文字列を見出し語の初期値にする（ZasDictAndroid の FAB と同じ挙動）。</summary>
    private void NewWord()
    {
        if (_doc is null) return;
        ShowOverlay(new WordEditViewModel(null, _doc.Words, _relations, _search, _state.Query));
    }

    private void EditWord(Word? w)
    {
        if (_doc is null || w is null) return;
        ShowOverlay(new WordEditViewModel(w, _doc.Words, _relations, _search));
    }

    /// <summary>一覧から単語を指して編集を開く。「同じ画面は 1 枚まで」の縛りはそのままに、
    /// 単語編集を開いたまま別の単語を指したときだけ、そちらへ差し替える。
    /// 差し替え前の内容が変わっていれば確認する（同じ単語なら前に出すだけ）。</summary>
    private void RequestEditWord(Word w)
    {
        if (_doc is null || _modal) return;

        var current = OpenOf<WordEditViewModel>();
        if (current is null) { EditWord(w); return; }
        if (current.Source == w)
        {
            if (_layout.LeafOf(current) is { } leaf) leaf.Selected = current;
            return;
        }

        if (!current.HasChanges) { CloseOverlay(current); EditWord(w); return; }

        Confirm(Strings.WordEdit_UnsavedTitle, string.Format(Strings.WordEdit_UnsavedMessage, current.Form),
            (Strings.Common_Close, true, () => { CloseOverlay(current); EditWord(w); }),
            (Strings.Common_Stop, false, null));
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
        // CSV に書き出す欄なので、UI 文字列の [en] はここで落とす。
        var changeDetail = !isNew && formChanged ? EnTag.Strip(string.Format(Strings.Changelog_OldFormDetail, oldForm)) : "";
        AddPendingChange(new ChangeEntry(_services.Now, isNew ? "ADD" : "CHANGE", word.Form, changeDetail));

        CloseOverlay(vm);
        RebuildIndex();
        SelectWord(word);
        MarkDirty(string.Format(isNew ? Strings.Word_AddedStatus : Strings.Word_UpdatedStatus, word.Form));
    }

    private void DuplicateWord(Word? w)
    {
        if (_doc is null || w is null) return;
        var copy = w.Duplicate(_doc.NextId());
        _doc.Words.Add(copy);
        AddPendingChange(new ChangeEntry(_services.Now, "ADD", copy.Form, ""));
        RebuildIndex();
        SelectWord(copy);
        MarkDirty(string.Format(Strings.Word_DuplicatedStatus, w.Form));
        EditWord(copy);
    }

    private void ConfirmDeleteWord(Word? w)
    {
        if (w is null) return;
        Confirm(Strings.Word_DeleteConfirmTitle, string.Format(Strings.Word_DeleteConfirmMessage, w.DisplayForm),
            (Strings.Common_DeleteAction, true, () => DeleteWord(w)),
            (Strings.Common_Cancel, false, null));
    }

    private void DeleteWord(Word w)
    {
        if (_doc is null) return;
        RelationService.RemoveReferences(_doc.Words, w);
        _doc.Words.Remove(w);
        // 例文から参照を外すことはしない（消した単語を後で作り直すことがあるため）。
        // 表示は「id:12」に落ちるので、例文側で消すかどうかは書き手が決められる。
        _doc.ResolveExampleForms();
        AddPendingChange(new ChangeEntry(_services.Now, "DELETE", w.Form, ""));
        if (_state.SelectedWord == w) SelectWord(null);
        RebuildIndex();
        MarkDirty(string.Format(Strings.Word_DeletedStatus, w.Form));
    }

    private void FollowRelation(Relation? r)
    {
        if (_doc is null || r is null) return;
        var target = _doc.Words.FirstOrDefault(w => w.Id == r.Id);
        if (target is null)
        {
            SetStatus(string.Format(Strings.Word_RelationNotFoundStatus, r.Id, r.Form));
            return;
        }
        SelectWord(target);
    }

    private void MarkDirty(string status)
    {
        _state.IsDirty = true;
        _state.IsGitHubSynced = false;
        SetStatus(status);
        // 自動保存はモードに関係なく機能する（GitHubモードでもローカルファイルへの保存は通常どおり）。
        // コミットはこの保存結果を対象にするだけで、保存自体はコミットボタンの役割ではない。
        if (Settings.AutoSave && _doc?.Path is not null) Save(false);
    }

    // ---- 例文 -------------------------------------------------------------------------

    /// <summary>例文の一覧を開く。閉じて開き直したときも同じ絞り込みで始まる。</summary>
    private void ShowExamples()
    {
        if (_doc is null) { SetStatus(Strings.Main_OpenDictionaryPrompt); return; }
        ShowOverlay(new ExamplesViewModel(_doc, _exampleQuery));
    }

    private void RememberExampleQuery()
    {
        if (OpenOf<ExamplesViewModel>() is { } list) _exampleQuery = list.Query;
    }

    /// <summary>
    /// 例文エディタを開く。一覧は別のタブとして開いたままなので、閉じれば元の並びがそのまま出る。
    /// </summary>
    private void ShowExampleEditor(Example? example)
    {
        if (_doc is null) return;
        ShowOverlay(new ExampleEditViewModel(example, _doc, _search, _services.HasZpdicApiKey));
    }

    /// <summary>エディタを閉じて一覧へ戻る。一覧が開いていれば中身を引き直して、追加・削除をその場で反映する。</summary>
    private void BackFromExampleEditor(ExampleEditViewModel vm)
    {
        CloseOverlay(vm);
        foreach (var list in _layout.Overlays.OfType<ExamplesViewModel>().ToList()) list.Refresh();
    }

    private void CommitExample(ExampleEditViewModel vm)
    {
        if (_doc is null) return;

        var isNew = vm.Source is null;
        var example = vm.Source ?? Example.CreateNew(_doc.NextExampleId());
        vm.ApplyTo(example);
        if (isNew) _doc.Examples.Add(example);

        RefreshRelatedExamples();
        BackFromExampleEditor(vm);
        MarkDirty(isNew ? Strings.Example_AddedStatus : Strings.Example_UpdatedStatus);
    }

    private void ConfirmDeleteExample(ExampleEditViewModel vm)
    {
        if (vm.Source is not { } example) return;
        // 確認は編集画面とは別の層に出るので、やめたときは畳むだけで書きかけの入力はそのまま残る。
        Confirm(Strings.Example_DeleteConfirmTitle, string.Format(Strings.Example_DeleteConfirmMessage, example.SentencePreview),
            (Strings.Common_DeleteAction, true, () => DeleteExample(vm, example)),
            (Strings.Common_Stop, false, null));
    }

    private void DeleteExample(ExampleEditViewModel vm, Example example)
    {
        if (_doc is null) return;
        _doc.Examples.Remove(example);
        RefreshRelatedExamples();
        BackFromExampleEditor(vm);
        MarkDirty(Strings.Example_DeletedStatus);
    }

    private async Task FetchOfferAsync(ExampleEditViewModel vm)
    {
        var number = vm.OfferNumber;
        if (number <= 0) { vm.OfferStatus = Strings.Zpdic_EnterNumber; return; }
        if (!_services.HasZpdicApiKey) { vm.OfferStatus = Strings.Zpdic_KeyMissing; return; }

        vm.IsFetching = true;
        vm.OfferStatus = Strings.Zpdic_Fetching;
        ExampleOfferResult result;
        try
        {
            result = await _services.FetchExampleOfferAsync(vm.Catalog, number);
        }
        catch (Exception ex)
        {
            // 呼び出し元は結果を待たない（_ = で投げっぱなし）ので、ここで漏らすと例外が行方不明になる。
            _services.LogError("例文の出典照会", ex);
            result = new ExampleOfferResult(false, string.Format(Strings.Zpdic_FetchFailed, ex.Message));
        }

        Batch(() =>
        {
            vm.IsFetching = false;
            vm.OfferStatus = result.Message;
            if (result.Ok)
            {
                vm.TranslationText = result.Translation;
                vm.Supplement = result.Supplement;
                return;
            }
            // 通らなかったキーは残しておくと毎回同じ失敗を繰り返すので捨てる。
            if (result.KeyRejected)
            {
                _services.DeleteZpdicApiKey();
                vm.HasApiKey = _services.HasZpdicApiKey;
            }
            if (result.NotFound) vm.OfferNumberText = "0";
        });
    }

    private void SaveApiKey(ExampleEditViewModel vm)
    {
        try
        {
            _services.SaveZpdicApiKey(vm.ApiKeyInput);
            vm.ApiKeyInput = "";
            vm.OfferStatus = Strings.Zpdic_KeySaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.OfferStatus = string.Format(Strings.Zpdic_KeySaveFailed, ex.Message);
        }
        vm.HasApiKey = _services.HasZpdicApiKey;
    }

    // ---- ツール・更新履歴 ---------------------------------------------------------------

    /// <summary>
    /// 更新履歴の画面を組み立てる。CSV を選び直すと中身が丸ごと変わるので、そのときは
    /// 閉じて開き直す。行き先は種類ごとに覚えているので、タブでも独立ウィンドウでも同じ場所に戻る。
    /// </summary>
    private ChangelogViewModel BuildChangelog()
    {
        var csv = ChangelogCsvPath();
        return new ChangelogViewModel(ReadChangelogRows(csv), csv);
    }

    // legend が Markdown 文字列ならそのまま描画に渡す。構造化 JSON の場合は
    // 整形済み JSON をそのままテキストとして流す（Markdown として見劣りしない範囲で）。
    private string BuildLegendMarkdown() => _doc is null ? "" : _doc.Legend switch
    {
        JsonValue lv when lv.TryGetValue<string>(out var ls) => ls,
        var other => OtmJsonIo.PrettyPrint(other ?? _doc.Root["zpdicOnline"]),
    };

    /// <summary>legend が無い・文字列なら Markdown として書き、それ以外は JSON のまま書き戻す
    /// （構造化された legend を文字列に潰すと、読む側のツールが解釈できなくなる）。</summary>
    private bool LegendIsMarkdown => _doc?.Legend switch
    {
        null => true,
        JsonValue lv => lv.TryGetValue<string>(out _),
        _ => false,
    };

    /// <summary>編集欄に出す中身。表示と違い、legend が無いときに zpdicOnline へは落とさない。</summary>
    private string LegendEditSource() => _doc?.Legend switch
    {
        null => "",
        JsonValue lv when lv.TryGetValue<string>(out var ls) => ls,
        var other => OtmJsonIo.PrettyPrint(other),
    };

    private void CommitLegend(LegendViewModel vm)
    {
        if (_doc is null) return;
        if (vm.Draft == vm.EditSource) { vm.EndEdit(); return; }

        JsonNode? legend;
        if (LegendIsMarkdown) legend = JsonValue.Create(vm.Draft);
        else
        {
            try
            {
                legend = JsonNode.Parse(vm.Draft);
            }
            catch (JsonException ex)
            {
                vm.ValidationMessage = string.Format(Strings.Legend_InvalidJson, ex.Message);
                return;
            }
        }

        _doc.Root["legend"] = legend;
        ReloadLegends();
        MarkDirty(Strings.Legend_UpdatedStatus);
    }

    /// <summary>開いている凡例を辞書の今の legend で描き直す。</summary>
    private void ReloadLegends()
    {
        foreach (var vm in _layout.Overlays.OfType<LegendViewModel>())
            vm.Reload(BuildLegendMarkdown(), _doc is not null);
    }

    private string ChangelogCsvPath() => _doc?.Path is null
        ? (Settings.ChangelogPath ?? "")
        : (Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path));

    private IReadOnlyList<string[]> ReadChangelogRows(string csv)
    {
        // CSV にまだフラッシュしていない保留履歴は、行頭（timestamp）に * を付けて後続表示する。
        var rows = new List<string[]>(csv.Length > 0 ? _services.ReadChangelog(csv) : Array.Empty<string[]>());
        foreach (var e in _pendingChanges)
            rows.Add(new[] { "*" + e.At.ToString("yyyy-MM-dd"), e.Operation, e.Form, e.Detail });
        return rows;
    }

    /// <summary>書き出し元は開いた時点のパスではなく、画面が今つないでいる CSV。</summary>
    private void ExportChangelog(string csv)
    {
        if (!_services.FileExists(csv)) { SetStatus(Strings.Changelog_NothingToExportStatus); return; }
        if (_services.PickSavePath(Strings.Changelog_ExportDialogTitle, "CSV (*.csv)|*.csv", Path.GetFileName(csv)) is not { } to) return;
        _services.CopyFile(csv, to);
        SetStatus(string.Format(Strings.Changelog_ExportedStatus, to));
    }

    /// <summary>CSV を選び直す。戻り値は選び直せたか（成功したときだけ画面を作り直す）。</summary>
    private bool RelinkChangelog()
    {
        if (_services.PickOpenPath(Strings.Changelog_RelinkDialogTitle, Strings.Changelog_CsvFilter) is not { } path) return false;
        Settings.ChangelogPath = path;
        _services.SaveSettings();
        SetStatus(string.Format(Strings.Changelog_RelinkedStatus, Path.GetFileName(path)));
        return true;
    }

    // ---- GitHub モード ----------------------------------------------------------------

    /// <summary>改行コード（CRLF/LF）の差だけで「差あり」と誤判定しないよう正規化してから比較する。</summary>
    internal static bool TextEquals(string a, string b) => a.Replace("\r\n", "\n") == b.Replace("\r\n", "\n");

    private async Task LoadFromGitHubAsync()
    {
        if (!_services.TryGetGitHubTarget(out var target, out var error)) { SetStatus(error); return; }

        // 差があるかどうかを確認ダイアログの前に見ておく。無ければ「破棄して読み込む」確認自体が
        // 不要（何も破棄されない）なので出さず、その場でステータスに出して終える。
        SetGitHubBusy(true);
        SetStatus(Strings.GitHub_Checking);
        GitHubFileResult json;
        try
        {
            json = await _services.GetGitHubFileAsync(target, target.JsonPath);
        }
        catch (Exception ex)
        {
            _services.LogError("GitHubからの読み込み", ex);
            json = new GitHubFileResult(false, ex.Message);
        }

        Batch(() =>
        {
            SetGitHubBusy(false);
            if (_closing) return;
            if (!json.Ok)
            {
                if (json.AuthFailed) _services.DeleteGitHubToken();
                SetStatus(string.Format(Strings.GitHub_LoadFailedStatus, json.Message));
                return;
            }

            var localPath = _services.GitHubWorkingCopyPath(target, _doc?.Path);
            if (_services.FileExists(localPath) && TextEquals(_services.ReadAllText(localPath), json.Content))
            {
                _state.IsGitHubSynced = true;
                SetStatus(Strings.GitHub_NoDiff);
                return;
            }

            // 押し間違いで手元のファイルを消さないよう確認を挟む。上書きするのは開いている辞書
            // そのものなので、どのファイルが置き換わるかを文面に出す。取得済みの内容はそのまま渡し、
            // 確定後にもう一度取りに行かない。
            Confirm(Strings.GitHub_LoadConfirmTitle,
                Strings.GitHub_LoadConfirmQuestion + Environment.NewLine + string.Format(Strings.GitHub_LoadConfirmTarget, localPath),
                (Strings.GitHub_LoadConfirmYes, true, () => _ = LoadFromGitHubCoreAsync(target, json)),
                (Strings.Common_Stop, false, null));
        });
    }

    private async Task LoadFromGitHubCoreAsync(GitHubTarget target, GitHubFileResult jsonResult)
    {
        SetGitHubBusy(true);
        SetStatus(Strings.GitHub_Loading);
        try
        {
            var localPath = _services.GitHubWorkingCopyPath(target, _doc?.Path);
            _services.WriteAllText(localPath, jsonResult.Content, withBom: false);

            GitHubFileResult? csvResult = null;
            if (target.ChangelogPath.Length > 0)
                csvResult = await _services.GetGitHubFileAsync(target, target.ChangelogPath);

            Batch(() =>
            {
                if (_closing) return;
                // 辞書は読み込めても更新履歴の取得にだけ失敗した場合、ローカルのCSVはGitHub側と
                // 一致していない。その場合はコミットボタンを無効化しない（＝差ありのまま）。
                var csvSynced = true;
                if (csvResult is { Ok: true })
                {
                    // 更新履歴の CSV をローカルで選んであるなら、その CSV を上書きする。
                    // 追記先（FlushChangelog）とコミット対象がここと同じファイルになるようにするため。
                    var csvLocalPath = string.IsNullOrWhiteSpace(Settings.ChangelogPath)
                        ? ChangelogService.DefaultPathFor(localPath)
                        : Settings.ChangelogPath;
                    _services.WriteAllText(csvLocalPath, csvResult.Content, withBom: true);
                    Settings.ChangelogPath = csvLocalPath;
                }
                else if (csvResult is { NotFound: false })
                {
                    // 辞書自体は読み込めたので続行する。履歴だけ最初のコミットで新規作成させる。
                    SetStatus(string.Format(Strings.GitHub_ChangelogLoadFailedStatus, csvResult.Message));
                    csvSynced = false;
                }

                LoadDictionary(localPath);
                // LoadDictionary が一旦 false に戻すので、その後で確定させる。
                if (csvSynced) _state.IsGitHubSynced = true;
                SetStatus(string.Format(Strings.GitHub_LoadedStatus, target.Owner, target.Repo, target.Branch));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.LogError("GitHubから取得したファイルの書き込み", ex);
            Batch(() => SetStatus(string.Format(Strings.GitHub_WriteFailedStatus, ex.Message)));
        }
        finally
        {
            Batch(() => SetGitHubBusy(false));
        }
    }

    private void ShowCommitDialog()
    {
        if (!_services.TryGetGitHubTarget(out var target, out var error)) { SetStatus(error); return; }
        if (_doc is null) { SetStatus(Strings.Main_OpenDictionaryPrompt); return; }

        var forms = _pendingChanges.Select(e => e.Form).Distinct().ToList();
        var summary = _pendingChanges.Count == 0
            ? Strings.GitHub_NoPendingChanges
            : string.Join(Environment.NewLine, _pendingChanges.Select(e => $"{e.Operation} {e.Form}"));
        // コミットメッセージは編集欄を通って GitHub へ出ていくので、[en] はここで落とす。
        var defaultMessage = EnTag.Strip(_pendingChanges.Count == 0
            ? Strings.GitHub_DefaultCommitMessage
            : string.Format(Strings.GitHub_CommitMessageFormat, string.Join(", ", forms.Take(5)), forms.Count > 5 ? Strings.GitHub_AndMoreSuffix : ""));

        _commitTarget = target;
        ShowModal(new CommitViewModel(summary, defaultMessage));
    }

    private void ConfirmCommit()
    {
        if (_state.ModalOverlay is not CommitViewModel vm || _commitTarget is not { } target) return;
        var message = vm.Message.Trim();
        _commitTarget = null;
        DismissModal();
        _ = CommitToGitHubAsync(target, message.Length == 0 ? vm.DefaultMessage : message);
    }

    private async Task CommitToGitHubAsync(GitHubTarget target, string message)
    {
        if (_doc is null) return;

        // コミットの対象は常にローカルの最新内容。保存は自動保存（または手動保存）が担うが、
        // それがオフの環境でも古い内容をコミットしないよう、念のためここでも確定させる。
        if (_state.IsDirty) Save(false);
        if (_doc.Path is null) { SetStatus(Strings.Main_NoSavePathStatus); return; }

        SetGitHubBusy(true);
        SetStatus(Strings.GitHub_Committing);
        try
        {
            var files = new List<GitHubFileChange> { new(target.JsonPath, _services.ReadAllText(_doc.Path)) };

            var csvPath = Settings.ChangelogPath ?? ChangelogService.DefaultPathFor(_doc.Path);
            if (target.ChangelogPath.Length > 0 && _services.FileExists(csvPath))
            {
                var csvText = _services.ReadAllText(csvPath);
                if (!csvText.StartsWith(string.Join(',', ChangelogService.DefaultHeader)))
                    csvText = string.Join(',', ChangelogService.DefaultHeader) + "\n" + csvText;
                files.Add(new GitHubFileChange(target.ChangelogPath, csvText));
            }

            // 辞書と更新履歴は 1 本のツリーに積んでから 1 回でコミットする（片方だけ更新した中途半端な
            // コミットにしない）。ブランチ先端を毎回そのまま基点にするので sha を覚えておく必要は無く、
            // 同時に他所が進めていた場合だけ fast-forward が失敗して安全に弾かれる。
            var result = await _services.CommitGitHubAsync(target, files, message);
            Batch(() =>
            {
                if (_closing) return;
                if (!result.Ok)
                {
                    if (result.AuthFailed) _services.DeleteGitHubToken();
                    SetStatus(string.Format(Strings.GitHub_CommitFailedStatus, result.Message));
                    return;
                }
                _state.IsGitHubSynced = true;
                SetStatus(Strings.GitHub_Committed);
            });
        }
        catch (IOException ex)
        {
            _services.LogError("コミット対象ファイルの読み込み", ex);
            Batch(() => SetStatus(string.Format(Strings.GitHub_CommitFailedStatus, ex.Message)));
        }
        finally
        {
            Batch(() => SetGitHubBusy(false));
        }
    }
}
