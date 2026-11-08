using System.Text.Json.Nodes;
using System.Windows;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

/// <summary>AppMediatorTests が扱っていない流れ（新規辞書・複製・削除・例文・更新履歴・GitHub の失敗・設定・窓）。</summary>
public class AppMediatorFlowTests
{
    private static Word W(int id, string form) => MediatorHarness.NewWord(id, form);

    private static OtmDocument OpenWithExamples(MediatorHarness h, Word[] words, params Example[] examples)
    {
        var doc = new OtmDocument(new JsonObject(), words, "d.json", examples);
        h.Services.Docs["d.json"] = doc;
        h.Services.NextOpenPath = "d.json";
        h.Send(IntentKind.OpenDictionaryRequested);
        return doc;
    }

    private static Example Ex(int id, string sentence, params int[] wordIds)
    {
        var e = Example.CreateNew(id);
        e.Sentence = sentence;
        e.Words = wordIds.Select(i => new ExampleWord { Id = i }).ToList();
        return e;
    }

    private static void AddWord(MediatorHarness h, string form)
    {
        h.Send(IntentKind.NewWordRequested);
        h.Open<WordEditViewModel>()!.Form = form;
        h.Send(IntentKind.WordCommitted);
    }

    // ---- 辞書の新規作成・保存 ------------------------------------------------------------

    [Fact]
    public void NewDictionary_StaysDirtyUntilAPathIsChosen()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.NewDictionaryRequested);
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.Null(h.Services.Settings.LastDictionaryPath);

        h.Send(IntentKind.SaveRequested);   // 保存先の選択をやめた
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.Equal("dictionary.json", h.Services.SuggestedFileName);

        h.Services.NextSavePath = "new.json";
        h.Send(IntentKind.SaveRequested);
        Assert.Equal(DocPhase.Clean, h.M.Doc);
        Assert.Equal(new[] { "new.json" }, h.Services.SavedPaths);
        Assert.Equal("new.json", h.Services.Settings.LastDictionaryPath);
    }

    [Fact]
    public void SaveAs_SuggestsTheCurrentFileName()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        h.Services.NextSavePath = "copy.json";

        h.Send(IntentKind.SaveAsRequested);

        Assert.Equal("d.json", h.Services.SuggestedFileName);
        Assert.Equal(new[] { "copy.json" }, h.Services.SavedPaths);
    }

    [Fact]
    public void Save_AppendsChangelogNextToDictionaryOrToChosenCsv()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        AddWord(h, "b");
        h.Send(IntentKind.SaveRequested);
        Assert.Equal("d_changelog.csv", Assert.Single(h.Services.Appended).Csv);
        Assert.Empty(h.M.PendingChanges);

        h.Services.Settings.ChangelogPath = "chosen.csv";
        AddWord(h, "c");
        h.Send(IntentKind.SaveRequested);
        Assert.Equal("chosen.csv", h.Services.Appended[1].Csv);
    }

    [Fact]
    public void ChangelogAppendFailure_IsShownAndRetriedOnNextSave()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        AddWord(h, "b");
        h.Services.AppendFails = true;

        h.Send(IntentKind.SaveRequested);

        Assert.Equal(DocPhase.Clean, h.M.Doc);   // 辞書そのものは保存できている
        Assert.Single(h.M.PendingChanges);
        Assert.Equal(string.Format(Strings.Main_ChangelogAppendFailedStatus, "locked"), h.State.Status);
        Assert.Contains(h.Services.Logged, l => l.Contains("更新履歴の追記"));
        // 未保存の印が消えて見過ごされるので、ステータスだけでなくダイアログでも知らせる。
        Assert.Equal((ShellPhase.Modal, Strings.Main_ChangelogAppendFailedTitle), (h.M.Shell, h.Confirm!.Title));
        Assert.Contains(new HostCommand.FlashStatus(true), h.HostCommands);

        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        h.Services.AppendFails = false;
        h.Send(IntentKind.SaveRequested);
        Assert.Equal("b", Assert.Single(h.Services.Appended).Entries.Single().Form);
        Assert.Empty(h.M.PendingChanges);
    }

    [Fact]
    public void ChangelogNotYetWritten_IsAskedAboutOnExit()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        AddWord(h, "b");
        h.Services.AppendFails = true;
        h.Send(IntentKind.SaveRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        // 辞書は保存済みでも、書けていない履歴が残っていれば黙って終了しない。
        h.Send(IntentKind.WindowCloseRequested);

        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        Assert.Equal(string.Format(Strings.App_UnwrittenChangelogMessage, h.State.DictionaryName), h.Confirm!.Message);
    }

    // ---- 元に戻す・やり直し --------------------------------------------------------------

    private static void DeleteWord(MediatorHarness h, string form)
    {
        h.Send(IntentKind.DeleteWordRequested, h.State.AllWords.Single(w => w.Form == form));
        h.Send(IntentKind.ConfirmChoiceSelected, 0);
    }

    [Fact]
    public void Undo_RestoresADeletedWord_AndRedoDeletesItAgain()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"), W(2, "b"));
        DeleteWord(h, "b");
        Assert.True(h.M.CanExecute(AppCommand.Undo));

        h.Send(IntentKind.UndoRequested);

        Assert.Equal(new[] { "a", "b" }, h.State.AllWords.Select(w => w.Form));
        // まだ CSV に書いていない DELETE は、行ごと取り下げる。
        Assert.Empty(h.M.PendingChanges);
        Assert.Equal(string.Format(Strings.Undo_UndoneStatus, string.Format(Strings.Undo_DeleteWord, "b")), h.State.Status);
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.False(h.M.CanExecute(AppCommand.Undo));

        h.Send(IntentKind.RedoRequested);

        Assert.Equal(new[] { "a" }, h.State.AllWords.Select(w => w.Form));
        Assert.Equal(("DELETE", "b"), (h.M.PendingChanges.Single().Operation, h.M.PendingChanges.Single().Form));
        Assert.True(h.M.CanExecute(AppCommand.Undo));
        Assert.False(h.M.CanExecute(AppCommand.Redo));
    }

    [Fact]
    public void Undo_AfterTheChangelogWasWritten_AppendsTheOppositeEntry()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"), W(2, "b"));
        DeleteWord(h, "b");
        h.Send(IntentKind.SaveRequested);
        Assert.Empty(h.M.PendingChanges);

        h.Send(IntentKind.UndoRequested);

        // CSV は追記しかしないので、書いた DELETE は消さずに ADD を足す。
        var entry = Assert.Single(h.M.PendingChanges);
        Assert.Equal(("ADD", "b"), (entry.Operation, entry.Form));
    }

    [Fact]
    public void Undo_RestoresRelationsTheEditTouched()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        var b = W(2, "b");
        a.Relations.Add(new Relation { Title = "対義語", Id = 2, Form = "b" });
        b.Relations.Add(new Relation { Title = "対義語", Id = 1, Form = "a" });
        h.OpenDictionary(a, b);
        DeleteWord(h, "b");
        Assert.Empty(h.State.AllWords.Single().Relations);

        h.Send(IntentKind.UndoRequested);

        Assert.Equal(2, Assert.Single(h.State.AllWords.Single(w => w.Form == "a").Relations).Id);
    }

    [Fact]
    public void Undo_IsBlockedWhileAnEditorIsOpen_AndForgottenWhenAnotherDictionaryOpens()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        AddWord(h, "b");
        Assert.True(h.M.CanExecute(AppCommand.Undo));

        h.Send(IntentKind.NewWordRequested);
        Assert.False(h.M.CanExecute(AppCommand.Undo));
        h.Send(IntentKind.WordEditCancelled);

        h.OpenDictionary(W(1, "x"));
        Assert.False(h.M.CanExecute(AppCommand.Undo));
    }

    [Fact]
    public void Undo_KeepsTheSelectionWhenTheWordStillExists()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"), W(2, "b"));
        DeleteWord(h, "b");
        h.Send(IntentKind.SelectWordRequested, h.State.AllWords.Single());

        h.Send(IntentKind.UndoRequested);

        Assert.Equal("a", h.State.SelectedWord!.Form);
    }

    [Fact]
    public void Open_SortsByAlphabetAndNumbersHomonyms()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "ka"), W(2, "e"), W(3, "ka"));

        Assert.Equal(new[] { "e", "ka", "ka (2)" }, h.State.AllWords.Select(w => w.DisplayForm));
        Assert.Equal(3, h.State.AllWords[2].Id);
    }

    [Fact]
    public void Open_AppliesTheDictionarysIgnoredPattern()
    {
        var h = new MediatorHarness();
        var doc = new OtmDocument(new JsonObject { ["zpdicOnline"] = new JsonObject { ["ignoredPattern"] = "【.*?】" } },
            new[] { MediatorHarness.NewWord(1, "ab", "【音】hit") }, "d.json");
        h.Services.Docs["d.json"] = doc;
        h.Services.NextOpenPath = "d.json";
        h.Send(IntentKind.OpenDictionaryRequested);

        h.Send(IntentKind.SearchModeChanged, nameof(SearchMode.Exact));
        h.Send(IntentKind.SearchScopeChanged, nameof(SearchScope.Translation));
        h.State.Query = "hit";
        h.Send(IntentKind.QueryChanged);

        Assert.Single(h.State.FilteredWords);
    }

    [Fact]
    public void ClearQuery_ShowsEverythingAgain()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"), W(2, "b"));
        h.State.Query = "zz";
        h.Send(IntentKind.QueryChanged);
        Assert.Empty(h.State.FilteredWords);

        h.Send(IntentKind.ClearQueryRequested);

        Assert.Equal("", h.State.Query);
        Assert.Equal(2, h.State.FilteredWords.Count);
    }

    // ---- 単語 ------------------------------------------------------------------------

    [Fact]
    public void Duplicate_AddsCopyWithoutRelationsAndOpensItsEditor()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        var b = W(2, "b");
        a.Relations.Add(new Relation { Title = "類義語", Id = 2, Form = "b" });
        h.OpenDictionary(a, b);

        h.Send(IntentKind.DuplicateWordRequested, a);

        var copy = h.Open<WordEditViewModel>()!.Source!;
        Assert.Equal((3, "a"), (copy.Id, copy.Form));
        Assert.Empty(copy.Relations);
        Assert.Equal(3, h.State.AllWords.Count);
        Assert.Equal(new[] { "ADD" }, h.M.PendingChanges.Select(c => c.Operation));
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
    }

    [Fact]
    public void Delete_DropsReferencesButExamplesKeepTheId()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        var b = W(2, "b");
        b.Relations.Add(new Relation { Title = "類義語", Id = 1, Form = "a" });
        var doc = OpenWithExamples(h, new[] { a, b }, Ex(1, "s", 1));
        h.Send(IntentKind.SelectWordRequested, a);

        h.Send(IntentKind.DeleteWordRequested, a);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.Empty(b.Relations);
        Assert.Null(h.State.SelectedWord);
        Assert.Equal("id:1", doc.Examples[0].Words[0].Form);
        Assert.Equal(new[] { "DELETE" }, h.M.PendingChanges.Select(c => c.Operation));
    }

    [Fact]
    public void Rename_UpdatesRelationFormsAndLogsOldForm()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        var b = W(2, "b");
        b.Relations.Add(new Relation { Title = "類義語", Id = 1, Form = "a" });
        h.OpenDictionary(a, b);

        h.Send(IntentKind.EditWordRequested, a);
        h.Open<WordEditViewModel>()!.Form = "a2";
        h.Send(IntentKind.WordCommitted);

        Assert.Equal("a2", b.Relations[0].Form);
        var entry = Assert.Single(h.M.PendingChanges);
        Assert.Equal(("CHANGE", "a2"), (entry.Operation, entry.Form));
        Assert.Equal(EnTag.Strip(string.Format(Strings.Changelog_OldFormDetail, "a")), entry.Detail);
    }

    [Fact]
    public void AddedRelation_RegistersCounterpartOnCommit()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        var b = W(2, "b");
        h.OpenDictionary(a, b);
        h.Send(IntentKind.EditWordRequested, a);
        var editor = h.Open<WordEditViewModel>()!;
        editor.RelationTitle = "上位語";

        h.Send(IntentKind.RelationRowAddRequested, b);
        h.Send(IntentKind.WordCommitted);

        var back = Assert.Single(b.Relations);
        Assert.Equal((Choices.Current.Relations["上位語"], 1), (back.Title, back.Id));
    }

    [Fact]
    public void FollowRelation_SelectsTargetOrReportsMissing()
    {
        var h = new MediatorHarness();
        var b = W(2, "b");
        h.OpenDictionary(W(1, "a"), b);

        h.Send(IntentKind.FollowRelationRequested, new Relation { Id = 2, Form = "b" });
        Assert.Same(b, h.State.SelectedWord);

        h.Send(IntentKind.FollowRelationRequested, new Relation { Id = 9, Form = "gone" });
        Assert.Equal(string.Format(Strings.Word_RelationNotFoundStatus, 9, "gone"), h.State.Status);
        Assert.Same(b, h.State.SelectedWord);
    }

    [Fact]
    public void EditWithoutPayload_OpensSelectedWord_AndCancelLeavesDocClean()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        h.OpenDictionary(a);
        h.Send(IntentKind.SelectWordRequested, a);

        h.Send(IntentKind.EditWordRequested);
        Assert.Same(a, h.Open<WordEditViewModel>()!.Source);
        h.Open<WordEditViewModel>()!.Form = "changed";

        // 書きかけがあるので、捨ててよいか確かめてから閉じる。
        h.Send(IntentKind.WordEditCancelled);
        Assert.Equal(Strings.WordEdit_DiscardButton, h.Confirm!.Choices[0].Label);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.Null(h.Open<WordEditViewModel>());
        Assert.Equal(("a", DocPhase.Clean), (a.Form, h.M.Doc));
    }

    [Fact]
    public void ClosingAnEditorWithChanges_AsksFirst_WhicheverWayItIsClosed()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        h.OpenDictionary(a);
        h.Send(IntentKind.EditWordRequested, a);
        var editor = h.Open<WordEditViewModel>()!;
        editor.Form = "changed";

        // タブの × も Esc も、キャンセルと同じく確認を挟む。「編集を続ける」なら閉じない。
        h.Send(IntentKind.TabCloseRequested, editor);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        h.Send(IntentKind.ConfirmChoiceSelected, 1);
        Assert.Same(editor, h.Open<WordEditViewModel>());

        h.Send(IntentKind.CancelRequested);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.Null(h.Open<WordEditViewModel>());
    }

    [Fact]
    public void ClosingAnEditorWithoutChanges_ClosesAtOnce()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        h.OpenDictionary(a);
        h.Send(IntentKind.EditWordRequested, a);

        h.Send(IntentKind.WordEditCancelled);

        Assert.Equal(ShellPhase.Normal, h.M.Shell);
        Assert.Null(h.Open<WordEditViewModel>());
    }

    [Fact]
    public void EditorRowIntents_ReachTheOpenEditor()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.NewWordRequested);
        var editor = h.Open<WordEditViewModel>()!;

        h.Send(IntentKind.TranslationRowAddRequested);
        h.Send(IntentKind.VariationRowAddRequested);
        h.Send(IntentKind.ContentRowAddRequested, "語法");
        Assert.Equal((2, 1, 1), (editor.Translations.Count, editor.Variations.Count, editor.Contents.Count));

        h.Send(IntentKind.TranslationRowRemoveRequested, editor.Translations[0]);
        h.Send(IntentKind.VariationRowRemoveRequested, editor.Variations[0]);
        h.Send(IntentKind.ContentRowRemoveRequested, editor.Contents[0]);
        Assert.Equal((1, 0, 0), (editor.Translations.Count, editor.Variations.Count, editor.Contents.Count));
    }

    // ---- 例文 ------------------------------------------------------------------------

    [Fact]
    public void NewExample_IsAddedWithItsOwnIdAsOfferNumber()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.ShowExamplesRequested);
        var list = h.Open<ExamplesViewModel>()!;

        h.Send(IntentKind.NewExampleRequested);
        h.Open<ExampleEditViewModel>()!.Sentence = "s";
        h.Send(IntentKind.ExampleCommitted);

        var e = Assert.Single(doc.Examples);
        Assert.Equal((1, 1, Const.ExampleCatalogSelf), (e.Id, e.OfferNumber, e.OfferCatalog));
        Assert.Null(h.Open<ExampleEditViewModel>());
        Assert.Single(list.Examples);   // 開いたままの一覧にもその場で出る
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
    }

    [Fact]
    public void ExampleWithoutSentence_IsNotSaved()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.NewExampleRequested);

        h.Send(IntentKind.ExampleCommitted);

        Assert.Empty(doc.Examples);
        Assert.Equal(Strings.ExampleEdit_ValidationSentence, h.Open<ExampleEditViewModel>()!.ValidationMessage);
    }

    [Fact]
    public void DeleteExample_AsksFirst()
    {
        var h = new MediatorHarness();
        var example = Ex(1, "s");
        var doc = OpenWithExamples(h, new[] { W(1, "a") }, example);
        h.Send(IntentKind.EditExampleRequested, example);

        h.Send(IntentKind.DeleteExampleRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 1);   // やめる
        Assert.Single(doc.Examples);
        Assert.NotNull(h.Open<ExampleEditViewModel>());

        h.Send(IntentKind.DeleteExampleRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.Empty(doc.Examples);
        Assert.Null(h.Open<ExampleEditViewModel>());
    }

    [Fact]
    public void ExampleListQuery_IsRememberedAfterVisitingTheEditor()
    {
        var h = new MediatorHarness();
        OpenWithExamples(h, new[] { W(1, "a") }, Ex(1, "s"));
        h.Send(IntentKind.ShowExamplesRequested);
        var list = h.Open<ExamplesViewModel>()!;
        list.Query = "s";

        h.Send(IntentKind.NewExampleRequested);
        h.Send(IntentKind.ExampleEditCancelled);
        h.Send(IntentKind.TabCloseRequested, list);
        h.Send(IntentKind.ShowExamplesRequested);

        Assert.Equal("s", h.Open<ExamplesViewModel>()!.Query);
    }

    [Fact]
    public void RelatedExamples_FollowTheSelectedWord()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        var b = W(2, "b");
        OpenWithExamples(h, new[] { a, b }, Ex(1, "s", 1));

        h.Send(IntentKind.SelectWordRequested, a);
        Assert.Single(h.State.RelatedExamples);
        h.Send(IntentKind.SelectWordRequested, b);
        Assert.Empty(h.State.RelatedExamples);
    }

    private static ExampleEditViewModel OnlineEditor(MediatorHarness h, bool hasKey = true)
    {
        h.Services.HasZpdicApiKey = hasKey;
        h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.NewExampleRequested);
        var editor = h.Open<ExampleEditViewModel>()!;
        editor.Catalog = "meat";
        editor.OfferNumberText = "3";
        return editor;
    }

    [Fact]
    public void FetchOffer_FillsTranslationAndSupplement()
    {
        var h = new MediatorHarness();
        var editor = OnlineEditor(h);

        h.Send(IntentKind.ExampleOfferFetchRequested);

        Assert.Equal(("tr", "sup", "ok", false), (editor.TranslationText, editor.Supplement, editor.OfferStatus, editor.IsFetching));
    }

    [Fact]
    public void FetchOffer_WithoutKey_AsksForOne()
    {
        var h = new MediatorHarness();
        var editor = OnlineEditor(h, hasKey: false);

        h.Send(IntentKind.ExampleOfferFetchRequested);

        Assert.Equal(Strings.Zpdic_KeyMissing, editor.OfferStatus);
    }

    [Fact]
    public void FetchOffer_RejectedKeyIsDiscarded_MissingNumberIsReset()
    {
        var h = new MediatorHarness();
        var editor = OnlineEditor(h);

        h.Services.FetchOffer = (_, _) => new ExampleOfferResult(false, "nf") { NotFound = true };
        h.Send(IntentKind.ExampleOfferFetchRequested);
        Assert.Equal("0", editor.OfferNumberText);

        editor.OfferNumberText = "3";
        h.Services.FetchOffer = (_, _) => new ExampleOfferResult(false, "bad key") { KeyRejected = true };
        h.Send(IntentKind.ExampleOfferFetchRequested);
        Assert.True(h.Services.ZpdicKeyDeleted);
        Assert.False(editor.HasApiKey);
        Assert.Equal("bad key", editor.OfferStatus);
    }

    [Fact]
    public void FetchOffer_ExceptionBecomesStatusAndLog()
    {
        var h = new MediatorHarness();
        var editor = OnlineEditor(h);
        h.Services.FetchOffer = (_, _) => throw new InvalidOperationException("boom");

        h.Send(IntentKind.ExampleOfferFetchRequested);

        Assert.Equal(string.Format(Strings.Zpdic_FetchFailed, "boom"), editor.OfferStatus);
        Assert.False(editor.IsFetching);
        Assert.Contains("例文の出典照会", h.Services.Logged);
    }

    [Fact]
    public void SaveApiKey_StoresAndClearsTheInput()
    {
        var h = new MediatorHarness();
        var editor = OnlineEditor(h, hasKey: false);
        editor.ApiKeyInput = "key";

        h.Send(IntentKind.ZpdicApiKeySaveRequested);

        Assert.Equal("key", h.Services.SavedZpdicKey);
        Assert.Equal(("", Strings.Zpdic_KeySaved, true), (editor.ApiKeyInput, editor.OfferStatus, editor.HasApiKey));
    }

    [Fact]
    public void ExampleWordIntents_ReachTheOpenEditor()
    {
        var h = new MediatorHarness();
        var a = W(1, "a");
        h.OpenDictionary(a);
        h.Send(IntentKind.NewExampleRequested);
        var editor = h.Open<ExampleEditViewModel>()!;

        h.Send(IntentKind.ExampleWordAddRequested, a);
        Assert.Single(editor.Words);
        h.Send(IntentKind.ExampleWordRemoveRequested, editor.Words[0]);
        Assert.Empty(editor.Words);
    }

    // ---- 更新履歴の画面 ---------------------------------------------------------------

    [Fact]
    public void ChangelogView_ShowsUnsavedRowsMarkedAndLive()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowChangelog);
        var view = h.Open<ChangelogViewModel>()!;
        Assert.Equal("d_changelog.csv", view.ChangelogPath);

        AddWord(h, "b");

        Assert.Equal(new[] { "*2026-09-25", "ADD", "b", "" }, Assert.Single(view.ChangelogRows));
    }

    [Fact]
    public void ChangelogExport_CopiesTheLinkedCsv()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowChangelog);

        h.Send(IntentKind.ChangelogExportRequested);
        Assert.Equal(Strings.Changelog_NothingToExportStatus, h.State.Status);

        h.Services.Files["d_changelog.csv"] = "x";
        h.Services.NextSavePath = "out.csv";
        h.Send(IntentKind.ChangelogExportRequested);
        Assert.Equal(("d_changelog.csv", "out.csv"), Assert.Single(h.Services.Copies));
    }

    [Fact]
    public void ChangelogRelink_RebuildsTheViewOnTheNewCsv()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowChangelog);
        var before = h.Open<ChangelogViewModel>();
        h.Services.NextOpenPath = "other.csv";

        h.Send(IntentKind.ChangelogRelinkRequested);

        Assert.Equal("other.csv", h.Services.Settings.ChangelogPath);
        var after = h.Open<ChangelogViewModel>()!;
        Assert.NotSame(before, after);
        Assert.Equal("other.csv", after.ChangelogPath);
    }

    // ---- GitHub ------------------------------------------------------------------------

    private static MediatorHarness GitHub(Action<StubServices>? setup = null)
        => new(s =>
        {
            s.Settings.Mode = EditMode.GitHub;
            setup?.Invoke(s);
        });

    [Fact]
    public void GitHubLoad_NotConfigured_ShowsWhy()
    {
        var h = GitHub(s => s.GitHubConfigured = false);
        h.Send(IntentKind.GitHubLoadRequested);
        Assert.Equal(("missing", ShellPhase.Normal), (h.State.Status, h.M.Shell));
    }

    [Fact]
    public void GitHubLoad_NetworkFailure_KeepsToken()
    {
        var h = GitHub(s => s.GetFile = _ => new GitHubFileResult(false, "boom"));
        h.Send(IntentKind.GitHubLoadRequested);
        Assert.Equal(string.Format(Strings.GitHub_LoadFailedStatus, "boom"), h.State.Status);
        Assert.False(h.Services.TokenDeleted);
    }

    [Fact]
    public void GitHubLoad_WritesChangelogWithBomAndLinksIt()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.GetFile = path => new GitHubFileResult(true, "", path == "log.csv" ? "csv" : "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.Equal("csv", h.Services.Files["gh_changelog.csv"]);
        Assert.Contains("gh_changelog.csv", h.Services.WrittenWithBom);    // Excel で開けるように
        Assert.DoesNotContain("gh.json", h.Services.WrittenWithBom);
        Assert.Equal("gh_changelog.csv", h.Services.Settings.ChangelogPath);
        Assert.True(h.State.IsGitHubSynced);
    }

    [Fact]
    public void GitHubLoad_ChangelogNotYetOnRemote_StillSynced()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.GetFile = path => path == "log.csv" ? new GitHubFileResult(false, "404") { NotFound = true } : new GitHubFileResult(true, "", "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.True(h.State.IsGitHubSynced);
    }

    [Fact]
    public void GitHubLoad_OnlyChangelogDiffers_Confirms()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.Files["gh.json"] = "{}";
            s.Files["gh_changelog.csv"] = "old";
            s.GetFile = path => new GitHubFileResult(true, "", path == "log.csv" ? "new" : "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);
        Assert.NotNull(h.Confirm);
        Assert.Contains(string.Format(Strings.GitHub_LoadConfirmTarget, "gh_changelog.csv"), h.Confirm!.Message);

        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.Equal("new", h.Services.Files["gh_changelog.csv"]);
        Assert.True(h.State.IsGitHubSynced);
    }

    [Fact]
    public void GitHubLoad_BothSameIgnoringBom_DoesNotConfirm()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.Files["gh.json"] = "{}";
            s.Files["gh_changelog.csv"] = "a\r\nb";
            s.GetFile = path => new GitHubFileResult(true, "", path == "log.csv" ? "﻿a\nb" : "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);

        Assert.Null(h.Confirm);
        Assert.True(h.State.IsGitHubSynced);
        Assert.Equal(Strings.GitHub_NoDiff, h.State.Status);
    }

    [Fact]
    public void GitHubLoad_DictionarySameButChangelogFetchFails_ReportsWithoutConfirm()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.Files["gh.json"] = "{}";
            s.GetFile = path => path == "log.csv" ? new GitHubFileResult(false, "boom") : new GitHubFileResult(true, "", "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);

        Assert.Null(h.Confirm);
        Assert.False(h.State.IsGitHubSynced);
        Assert.Equal(string.Format(Strings.GitHub_ChangelogLoadFailedStatus, "boom"), h.State.Status);
    }

    [Fact]
    public void GitHubLoad_NoLocalFile_AsksToCreateAndPutsChangelogNextToIt()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.Settings.ChangelogPath = "other_changelog.csv";   // 前に開いていた別の辞書のもの
            s.Files["other_changelog.csv"] = "keep";
            s.GetFile = path => new GitHubFileResult(true, "", path == "log.csv" ? "csv" : "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);
        var message = h.Confirm!.Message;
        Assert.Contains(Strings.GitHub_LoadCreateQuestion, message);
        Assert.Contains(string.Format(Strings.GitHub_LoadCreateTarget, "gh.json"), message);
        Assert.Contains(string.Format(Strings.GitHub_LoadCreateTarget, "gh_changelog.csv"), message);

        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.Equal(("{}", "csv", "keep"), (h.Services.Files["gh.json"], h.Services.Files["gh_changelog.csv"], h.Services.Files["other_changelog.csv"]));
        Assert.Equal("gh_changelog.csv", h.Services.Settings.ChangelogPath);
        Assert.Equal("gh.json", h.Services.Settings.LastDictionaryPath);
    }

    [Fact]
    public void GitHubLoad_WorkingCopyExistsButNotOpen_IgnoresChangelogSetting()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.Settings.ChangelogPath = "other_changelog.csv";
            s.Files["gh.json"] = "{\"old\":1}";   // 前回取得した作業コピーだけ残っている
            s.GetFile = path => new GitHubFileResult(true, "", path == "log.csv" ? "csv" : "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);
        Assert.Contains(string.Format(Strings.GitHub_LoadCreateTarget, "gh_changelog.csv"), h.Confirm!.Message);

        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.False(h.Services.Files.ContainsKey("other_changelog.csv"));
        Assert.Equal("gh_changelog.csv", h.Services.Settings.ChangelogPath);
    }

    [Fact]
    public void GitHubLoad_OpenDictionary_UsesLinkedChangelog()
    {
        var h = GitHub(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.Settings.ChangelogPath = "linked.csv";
            s.GetFile = path => new GitHubFileResult(true, "", path == "log.csv" ? "csv" : "{}");
        });
        h.OpenDictionary();

        h.Send(IntentKind.GitHubLoadRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.Equal("csv", h.Services.Files["linked.csv"]);
        Assert.Equal("linked.csv", h.Services.Settings.ChangelogPath);
    }

    [Fact]
    public void GitHubCommit_SavesFirstAndAddsHeaderToChangelog()
    {
        var h = GitHub(s => s.ChangelogRemotePath = "log.csv");
        h.OpenDictionary(W(1, "a"));
        h.Services.Files["d.json"] = "{json}";
        h.Services.Files["d_changelog.csv"] = "2026-01-01,ADD,x,\n";
        AddWord(h, "b");

        h.Send(IntentKind.GitHubCommitRequested);
        h.Send(IntentKind.GitHubCommitConfirmed);

        Assert.Equal(new[] { "d.json" }, h.Services.SavedPaths);   // 古い内容をコミットしない
        var files = Assert.Single(h.Services.Commits).Files;
        Assert.Equal(("d.json", "{json}"), (files[0].Path, files[0].Content));
        Assert.Equal("log.csv", files[1].Path);
        Assert.Equal("timestamp,type,form,details\n2026-01-01,ADD,x,\n", files[1].Content);
    }

    [Fact]
    public void GitHubCommit_AuthFailure_DeletesTokenAndStaysUnsynced()
    {
        var h = GitHub(s => s.CommitResult = new GitHubCommitResult(false, "401") { AuthFailed = true });
        h.OpenDictionary(W(1, "a"));

        h.Send(IntentKind.GitHubCommitRequested);
        h.Send(IntentKind.GitHubCommitConfirmed);

        Assert.True(h.Services.TokenDeleted);
        Assert.False(h.State.IsGitHubSynced);
        Assert.Equal(string.Format(Strings.GitHub_CommitFailedStatus, "401"), h.State.Status);
    }

    [Fact]
    public void GitHubCommitMessage_ListsUpToFiveForms()
    {
        var h = GitHub();
        h.OpenDictionary(W(1, "z"));
        foreach (var f in new[] { "a", "b", "c", "d", "e", "f" }) AddWord(h, f);

        h.Send(IntentKind.GitHubCommitRequested);

        var dialog = Assert.IsType<CommitViewModel>(h.State.ModalOverlay);
        Assert.Equal(EnTag.Strip(string.Format(Strings.GitHub_CommitMessageFormat, "a, b, c, d, e", Strings.GitHub_AndMoreSuffix)), dialog.DefaultMessage);
        Assert.StartsWith("ADD a", dialog.Summary);
    }

    [Fact]
    public void GitHubCommitMessage_WithoutPendingChanges_UsesDefault()
    {
        var h = GitHub();
        h.OpenDictionary(W(1, "a"));

        h.Send(IntentKind.GitHubCommitRequested);

        var dialog = Assert.IsType<CommitViewModel>(h.State.ModalOverlay);
        Assert.Equal((EnTag.Strip(Strings.GitHub_DefaultCommitMessage), Strings.GitHub_NoPendingChanges), (dialog.DefaultMessage, dialog.Summary));
    }

    [Fact]
    public void GitHubCommit_UnsavedNewDictionary_Stops()
    {
        var h = GitHub();
        h.Send(IntentKind.NewDictionaryRequested);

        h.Send(IntentKind.GitHubCommitRequested);
        h.Send(IntentKind.GitHubCommitConfirmed);   // 保存先の選択をやめた

        Assert.Empty(h.Services.Commits);
        Assert.Equal(Strings.Main_NoSavePathStatus, h.State.Status);
    }

    // ---- 設定 ------------------------------------------------------------------------

    private static SettingsViewModel OpenSettings(MediatorHarness h)
    {
        h.Send(IntentKind.SettingsRequested);
        return Assert.IsType<HostCommand.OpenSettings>(h.HostCommands.Last()).State;
    }

    [Theory]
    [InlineData(IntentKind.WindowCloseRequested)]   // ［キャンセル］と ×
    [InlineData(IntentKind.CancelRequested)]        // Esc
    public void Settings_ClosingWithUnappliedChanges_AsksInsideTheSettingsWindow(IntentKind close)
    {
        var h = new MediatorHarness();
        var form = OpenSettings(h);
        var window = h.AddHost(HostRole.Settings);
        form.SortOrder = "zyx";

        h.Send(close, from: window);

        // 設定ウィンドウはモーダルなので、確認は本体ではなく設定ウィンドウの中に出す。
        Assert.Equal(Strings.Settings_DiscardButton, form.Confirm!.Choices[0].Label);
        Assert.Null(h.Confirm);
        Assert.DoesNotContain(new HostCommand.CloseHost(window.HostId), h.HostCommands);

        h.Send(IntentKind.ConfirmChoiceSelected, 1, from: window);   // 編集を続ける
        Assert.Null(form.Confirm);
        Assert.DoesNotContain(new HostCommand.CloseHost(window.HostId), h.HostCommands);

        h.Send(close, from: window);
        h.Send(IntentKind.ConfirmChoiceSelected, 0, from: window);   // 適用せずに閉じる
        Assert.Contains(new HostCommand.CloseHost(window.HostId), h.HostCommands);
        Assert.NotEqual("zyx", h.Services.Settings.SortOrder);
    }

    [Fact]
    public void Settings_ClosingWithoutChanges_ClosesAtOnce()
    {
        var h = new MediatorHarness();
        var form = OpenSettings(h);
        var window = h.AddHost(HostRole.Settings);

        h.Send(IntentKind.WindowCloseRequested, from: window);

        Assert.Null(form.Confirm);
        Assert.Contains(new HostCommand.CloseHost(window.HostId), h.HostCommands);
    }

    // 同じ画面に即時反映の項目を混ぜないよう、トークンも［適用］を押すまで保存・削除しない。
    [Fact]
    public void Settings_TokenIsSavedOnlyOnApply()
    {
        var h = new MediatorHarness(s => s.HasGitHubToken = false);
        var form = OpenSettings(h);
        form.GitHubTokenInput = "tok";
        Assert.Null(h.Services.SavedGitHubToken);

        h.Send(IntentKind.SettingsApplyRequested);

        Assert.Equal("tok", h.Services.SavedGitHubToken);
        Assert.True(h.Services.HasGitHubToken);
    }

    [Fact]
    public void Settings_TokenIsDeletedOnlyOnApply()
    {
        var h = new MediatorHarness();
        var form = OpenSettings(h);
        form.DeleteGitHubToken = true;
        Assert.False(h.Services.TokenDeleted);

        h.Send(IntentKind.SettingsApplyRequested);

        Assert.True(h.Services.TokenDeleted);
        Assert.Null(h.Services.SavedGitHubToken);
    }

    [Fact]
    public void Settings_FormIntentsChangeOnlyTheForm()
    {
        var h = new MediatorHarness();
        var form = OpenSettings(h);
        form.ReciprocalText = "";
        h.Services.NextOpenPath = "font.ttf";

        h.Send(IntentKind.SettingsModeSelected, nameof(EditMode.GitHub));
        h.Send(IntentKind.SettingsReciprocalResetRequested);
        h.Send(IntentKind.PickHeksaFontRequested);

        Assert.Equal(EditMode.GitHub, form.Mode);
        Assert.Contains("上位語=下位語", form.ReciprocalText);
        Assert.Equal("font.ttf", form.HeksaFontPath);
        Assert.Equal(EditMode.Local, h.Services.Settings.Mode);   // 適用するまでは設定に書かない
    }

    [Fact]
    public void Settings_ApplyTrimsGitHubFieldsAndWritesSearchOptionsToDictionary()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(W(1, "a"));
        var form = OpenSettings(h);
        form.GitHubOwner = " o ";
        form.GitHubRepo = " ";
        form.GitHubBranch = " ";
        form.Punctuations = "'-";
        form.IgnoredPattern = "x";
        form.BrowserStartUrl = "  https://z/  ";

        h.Send(IntentKind.SettingsApplyRequested, from: h.AddHost(HostRole.Settings));

        var s = h.Services.Settings;
        Assert.Equal(("o", null, "main", "https://z/"), (s.GitHubOwner, s.GitHubRepo, s.GitHubBranch, s.BrowserStartUrl));
        Assert.Equal(new[] { "'", "-" }, doc.ZpdicOnline["punctuations"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("x", doc.ZpdicOnline["ignoredPattern"]!.GetValue<string>());
        Assert.Contains(h.HostCommands, c => c is HostCommand.RefreshStreamHosts);
    }

    [Theory]
    [InlineData("'", "")]
    [InlineData("", "x")]
    public void Settings_ChangedSearchOptions_MarkDictionaryUnsaved(string punctuations, string ignored)
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        var form = OpenSettings(h);
        form.Punctuations = punctuations;
        form.IgnoredPattern = ignored;

        h.Send(IntentKind.SettingsApplyRequested, from: h.AddHost(HostRole.Settings));

        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.Equal(Strings.Settings_AppliedStatus, h.State.Status);
        // 終了しようとすると保存を確かめる（書き換えた辞書データを黙って捨てない）。
        h.Send(IntentKind.WindowCloseRequested);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
    }

    [Fact]
    public void Settings_UnchangedSearchOptions_LeaveDictionaryUntouched()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(W(1, "a"));

        h.Send(IntentKind.SettingsApplyRequested, from: OpenSettingsHost(h));

        Assert.Equal(DocPhase.Clean, h.M.Doc);
        Assert.Null(doc.ZpdicOnline["ignoredPattern"]);   // 無かったキーを書き足さない
        Assert.Null(doc.ZpdicOnline["punctuations"]);
    }

    [Fact]
    public void Settings_ChangedSearchOptions_AreAutoSaved()
    {
        var h = new MediatorHarness(s => s.Settings.AutoSave = true);
        h.OpenDictionary(W(1, "a"));
        var form = OpenSettings(h);
        form.IgnoredPattern = "x";

        h.Send(IntentKind.SettingsApplyRequested, from: h.AddHost(HostRole.Settings));

        Assert.Equal(new[] { "d.json" }, h.Services.SavedPaths);
        Assert.Equal(DocPhase.Clean, h.M.Doc);
    }

    private static StubHost OpenSettingsHost(MediatorHarness h)
    {
        OpenSettings(h);
        return h.AddHost(HostRole.Settings);
    }

    [Fact]
    public void Settings_ApplyWhileMaximized_KeepsSizeButLocksAspect()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowStateChanged, WindowState.Maximized);
        var form = OpenSettings(h);
        form.WindowWidth = 1600;
        form.WindowHeight = 900;
        form.WindowAspectLocked = true;

        h.Send(IntentKind.SettingsApplyRequested, from: h.AddHost(HostRole.Settings));

        var size = h.HostCommands.OfType<HostCommand.ApplyShellSize>().Last();   // 起動時の復元にも 1 つ出る
        Assert.True(double.IsNaN(size.Width) && double.IsNaN(size.Height));
        Assert.Equal(1600.0 / 900, size.AspectRatio!.Value, 6);
    }

    [Fact]
    public void Settings_EscClosesTheWindow_AndReopenFocusesIt()
    {
        var h = new MediatorHarness();
        OpenSettings(h);
        var host = h.AddHost(HostRole.Settings);
        h.Send(IntentKind.SettingsModeSelected, nameof(EditMode.Local), from: host);   // 窓の id を覚えさせる

        h.Send(IntentKind.SettingsRequested);
        Assert.Contains(new HostCommand.FocusHost(host.HostId), h.HostCommands);

        h.Send(IntentKind.CancelRequested, from: host);
        Assert.Contains(new HostCommand.CloseHost(host.HostId), h.HostCommands);
        OpenSettings(h);
        Assert.Equal(2, h.HostCommands.OfType<HostCommand.OpenSettings>().Count());
    }

    // ---- 窓 -----------------------------------------------------------------------

    [Theory]
    [InlineData(HostRole.Stream)]
    [InlineData(HostRole.Count)]
    public void BroadcastWindowClosedByUser_FlipsTheMenuBack(HostRole role)
    {
        var h = new MediatorHarness();
        var index = role == HostRole.Stream ? 0 : 1;
        var closedHeader = h.M.BuildWindowMenu()[index].Header;
        h.Send(role == HostRole.Stream ? IntentKind.StreamWindowToggleRequested : IntentKind.CountWindowToggleRequested);
        Assert.NotEqual(closedHeader, h.M.BuildWindowMenu()[index].Header);

        h.Send(IntentKind.WindowCloseRequested, from: h.AddHost(role));

        Assert.Equal(closedHeader, h.M.BuildWindowMenu()[index].Header);
        Assert.Contains(h.HostCommands, c => c is HostCommand.ToggleStream { Open: false } or HostCommand.ToggleCount { Open: false });
    }

    [Fact]
    public void Closing_ClosesEveryWindowAndRemembersShellBounds()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowActivated);
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);
        var floating = Assert.Single(h.State.Layout.Floats);
        h.Send(IntentKind.StreamWindowToggleRequested);
        h.Send(IntentKind.CountWindowToggleRequested);
        OpenSettings(h);
        var settings = h.AddHost(HostRole.Settings);
        h.Send(IntentKind.SettingsModeSelected, nameof(EditMode.Local), from: settings);
        h.Send(IntentKind.WindowBoundsChanged, new WindowBounds(1200, 700, true));
        h.HostCommands.Clear();

        h.Send(IntentKind.WindowCloseRequested);

        Assert.Equal(new HostCommand[]
        {
            new HostCommand.CloseHost(floating.Id), new HostCommand.ToggleStream(false), new HostCommand.ToggleCount(false),
            new HostCommand.CloseHost(settings.HostId), new HostCommand.CloseHost(h.Shell.HostId),
        }, h.HostCommands.Where(c => c is HostCommand.CloseHost or HostCommand.ToggleStream or HostCommand.ToggleCount));
        Assert.Equal((1200.0, 700.0, true), (h.Services.Settings.WindowWidth, h.Services.Settings.WindowHeight, h.Services.Settings.WindowMaximized));
    }

    [Fact]
    public void Close_ExitWithoutSaving_DropsChanges()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(W(1, "a"));
        AddWord(h, "b");

        h.Send(IntentKind.WindowCloseRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 1);

        Assert.Equal(ShellPhase.Closing, h.M.Shell);
        Assert.Empty(h.Services.SavedPaths);
    }

    [Fact]
    public void FloatingWindowMoved_IsRememberedOnItsFloat()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowActivated);
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);
        var floating = Assert.Single(h.State.Layout.Floats);

        h.Send(IntentKind.WindowBoundsChanged, new Rect(10, 20, 300, 200), from: h.AddHost(HostRole.Floating, floating.Id));

        Assert.Equal(new Rect(10, 20, 300, 200), floating.Bounds);
    }

    [Fact]
    public void UnknownFloatingWindowClosed_IsJustClosed()
    {
        var h = new MediatorHarness();
        var orphan = h.AddHost(HostRole.Floating);
        h.Send(IntentKind.WindowCloseRequested, from: orphan);
        Assert.Contains(new HostCommand.CloseHost(orphan.HostId), h.HostCommands);
    }

    // ---- タブ・ブラウザ・例外 ------------------------------------------------------------

    [Fact]
    public void Browser_OpeningRemembersAndClosingForgets()
    {
        var h = new MediatorHarness();
        var initialized = false;
        h.State.Browser.InitializeRequested += () => initialized = true;

        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowBrowser);
        var tab = h.Open<BrowserTabViewModel>()!;
        Assert.True(initialized);
        Assert.True(h.Services.Settings.BrowserVisible);

        h.Send(IntentKind.TabCloseRequested, tab);
        Assert.False(h.Services.Settings.BrowserVisible);
        Assert.False(h.State.Browser.IsOpen);
    }

    [Fact]
    public void Browser_NavigationGoesThroughTheAddressNormalizer()
    {
        var h = new MediatorHarness();
        var seen = new List<string>();
        h.State.Browser.NavigateRequested += seen.Add;
        h.State.Browser.BackRequested += () => seen.Add("back");
        h.State.Browser.ForwardRequested += () => seen.Add("forward");
        h.State.Browser.ReloadRequested += () => seen.Add("reload");
        h.State.Browser.Address = "zasdict";

        h.Send(IntentKind.BrowserNavigateRequested);
        h.Send(IntentKind.BrowserBackRequested);      // 戻れないうちは何もしない
        h.State.Browser.ReportHistoryState(true, true);
        h.Send(IntentKind.BrowserBackRequested);
        h.Send(IntentKind.BrowserForwardRequested);
        h.Send(IntentKind.BrowserReloadRequested);

        Assert.Equal(new[] { "https://www.google.com/search?q=zasdict", "back", "forward", "reload" }, seen);
        Assert.Equal("https://www.google.com/search?q=zasdict", h.State.Browser.Address);
    }

    [Fact]
    public void AreaClose_DissolvesTheLeafAndPersists()
    {
        var h = new MediatorHarness(s => s.Settings.Layout = new DockNodeSettings
        {
            Axis = "Columns",
            First = new DockNodeSettings { Id = 1, Tabs = { nameof(SearchViewModel) } },
            Second = new DockNodeSettings { Id = 2, Tabs = { nameof(WordDetailViewModel) } },
        });
        var before = h.Persists;

        h.Send(IntentKind.AreaCloseRequested, fill: c => c.LeafId = 2);

        Assert.Single(h.State.Layout.AllLeaves);
        Assert.NotNull(h.Open<WordDetailViewModel>());   // 据え置きのタブは残る枠へ引き取る
        Assert.Equal(before + 1, h.Persists);
    }

    [Fact]
    public void TabSelect_AndFocusScreen()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowActivated);
        h.OpenDictionary(W(1, "a"));
        h.Send(IntentKind.ShowExamplesRequested);
        var examples = h.Open<ExamplesViewModel>()!;
        var leaf = h.State.Layout.LeafOf(examples)!;
        var neighbor = leaf.Items.First(i => i != examples);
        Assert.Same(examples, leaf.Selected);

        h.Send(IntentKind.TabSelectRequested, neighbor);
        Assert.Same(neighbor, leaf.Selected);

        h.Send(IntentKind.FocusScreenRequested, neighbor);
        Assert.Contains(new HostCommand.FocusHost(h.Shell.HostId), h.HostCommands);
    }

    [Fact]
    public void UnhandledException_IsShownInTheOverlay()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.UnhandledExceptionRaised, new InvalidOperationException("oops"));

        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        Assert.Equal(Strings.Error_Title, h.Confirm!.Title);
        Assert.Contains("oops", h.Confirm.Message);
        // 例外の型名は利用者の役に立たないので出さない（記録には残る）。
        Assert.DoesNotContain("InvalidOperationException", h.Confirm.Message);
    }

    [Fact]
    public void StatusChange_Flashes()
    {
        var h = new MediatorHarness();
        h.HostCommands.Clear();
        h.OpenDictionary(W(1, "a"));
        Assert.Contains(h.HostCommands, c => c is HostCommand.FlashStatus);
    }
}
