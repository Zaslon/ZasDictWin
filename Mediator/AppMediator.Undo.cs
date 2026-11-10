using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

/// <summary>
/// 辞書の編集の取り消し・やり直し。操作の前の辞書を JSON 全体で控えておき、戻すときはその中身へ差し替える。
/// 操作ごとの逆操作を持たないのは、関連語の対照登録や見出し語変更の波及で、1 つの操作が他の単語まで書き換えるため。
/// </summary>
public sealed partial class AppMediator
{
    /// <summary>取り消し・やり直し 1 回ぶん。State は戻した先の辞書、Changes はその操作で積んだ更新履歴。</summary>
    private sealed record UndoStep(string Label, string State, IReadOnlyList<ChangeEntry> Changes);

    // 辞書を丸ごと控えるので、段数ではなく文字数で上限を決める（大きな辞書でもメモリを食い潰さない）。
    private const int MaxUndoChars = 64 * 1024 * 1024;
    private const int MaxUndoSteps = 100;

    /// <summary>先頭が直近。</summary>
    private readonly LinkedList<UndoStep> _undo = new();
    private readonly LinkedList<UndoStep> _redo = new();

    /// <summary>取り消せる操作を実行している間だけ、積んだ更新履歴を集める。</summary>
    private List<ChangeEntry>? _recordingChanges;

    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;

    /// <summary>辞書を書き換える操作を、取り消せる形で実行する。何も変わらなかった操作は積まない。
    /// 見出しは操作の後で決める（追加した単語の見出し語は確定するまで分からないため）。</summary>
    private void Undoable(Func<string> label, Action body)
    {
        if (_doc is null) { body(); return; }

        var before = OtmJsonIo.ToJson(_doc);
        var changes = new List<ChangeEntry>();
        _recordingChanges = changes;
        try
        {
            body();
        }
        finally
        {
            _recordingChanges = null;
        }
        if (_doc is null || OtmJsonIo.ToJson(_doc) == before) return;

        Push(_undo, new UndoStep(label(), before, changes));
        _redo.Clear();
    }

    private void Undo()
    {
        if (_doc is null || _undo.First?.Value is not { } step) return;
        _undo.RemoveFirst();

        var current = OtmJsonIo.ToJson(_doc);
        RestoreDocument(step.State);
        RevertChanges(step.Changes);
        Push(_redo, step with { State = current });
        MarkDirty(string.Format(Strings.Undo_UndoneStatus, step.Label));
    }

    private void Redo()
    {
        if (_doc is null || _redo.First?.Value is not { } step) return;
        _redo.RemoveFirst();

        var current = OtmJsonIo.ToJson(_doc);
        RestoreDocument(step.State);
        var redone = new List<ChangeEntry>();
        foreach (var change in step.Changes)
        {
            var again = change with { At = _services.Now };
            if (AddPendingChange(again)) redone.Add(again);
        }
        Push(_undo, new UndoStep(step.Label, current, redone));
        MarkDirty(string.Format(Strings.Undo_RedoneStatus, step.Label));
    }

    private void ClearUndoHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private static void Push(LinkedList<UndoStep> stack, UndoStep step)
    {
        stack.AddFirst(step);
        var total = stack.Sum(s => (long)s.State.Length);
        while (stack.Count > 1 && (stack.Count > MaxUndoSteps || total > MaxUndoChars))
        {
            total -= stack.Last!.Value.State.Length;
            stack.RemoveLast();
        }
    }

    /// <summary>
    /// 取り消した操作の更新履歴を打ち消す。まだ CSV に書いていなければ行ごと消し、書いた後なら
    /// 逆向きの行を足す（CSV は追記しかしないので、書いた行は消せない）。
    /// </summary>
    private void RevertChanges(IReadOnlyList<ChangeEntry> changes)
    {
        if (changes.Count == 0) return;
        if (changes.All(c => _pendingChanges.Any(p => ReferenceEquals(p, c))))
        {
            _pendingChanges.RemoveAll(p => changes.Any(c => ReferenceEquals(p, c)));
            RefreshChangelogViews();
            return;
        }

        var detail = EnTag.Strip(Strings.Menu_Undo);
        foreach (var c in changes.Reverse())
        {
            var operation = c.Operation switch { "ADD" => "DELETE", "DELETE" => "ADD", var other => other };
            AddPendingChange(new ChangeEntry(_services.Now, operation, c.Form, detail));
        }
    }

    /// <summary>
    /// 開いている辞書の中身だけを差し替える。辞書の入れ物（OtmDocument と一覧のコレクション）は
    /// 例文の一覧などが握っているので、作り直さずに中身を入れ替える。
    /// </summary>
    private void RestoreDocument(string json)
    {
        var doc = _doc!;
        var restored = OtmJsonIo.Parse(json, doc.Path);
        var dictionarySettingsChanged =
            restored.Root["zpdicOnline"]?.ToJsonString() != doc.Root["zpdicOnline"]?.ToJsonString();
        var selectedId = _state.SelectedWord?.Id;

        doc.Root.Clear();
        foreach (var (key, value) in restored.Root.ToList())
        {
            restored.Root.Remove(key);
            doc.Root[key] = value;
        }
        doc.Words.Clear();
        foreach (var w in restored.Words) doc.Words.Add(w);
        doc.Examples.Clear();
        foreach (var e in restored.Examples) doc.Examples.Add(e);
        doc.ResolveExampleForms();

        // 句読点と無視パターンは索引の作り方に効く。設定の再適用は Heksa フォントを読み直すので、変わったときだけにする。
        if (dictionarySettingsChanged) ApplySettings();
        RebuildIndex();
        ReloadLegends();
        foreach (var list in _layout.Overlays.OfType<ExamplesViewModel>().ToList()) list.Refresh();
        SelectWord(selectedId is { } id ? doc.Words.FirstOrDefault(w => w.Id == id) : null);
        RaiseDocumentChanged();
    }

    // ---- メニュー -------------------------------------------------------------------

    /// <summary>選択中の単語に効く操作は、一覧の行の ⋯ と同じものをここにも並べる
    /// （ZasDesignSystem rules.md「隠れた操作は近道に留める」。行の右クリックとショートカットの手掛かり）。</summary>
    public IReadOnlyList<MenuActionSpec> BuildEditMenu() => new[]
    {
        new MenuActionSpec(Strings.Menu_Undo, "", IntentKind.UndoRequested, null, CanExecute(AppCommand.Undo), false, true,
            Shortcut: "Ctrl+Z", AccessKey: 'U'),
        new MenuActionSpec(Strings.Menu_Redo, "", IntentKind.RedoRequested, null, CanExecute(AppCommand.Redo), false, true,
            Shortcut: "Ctrl+Y", AccessKey: 'R'),
        new MenuActionSpec(Strings.Main_NewWordButton, "", IntentKind.NewWordRequested, null, CanExecute(AppCommand.NewWord), false, true,
            Shortcut: "Ctrl+N", AccessKey: 'W', Ellipsis: true, GroupStart: true),
        new MenuActionSpec(Strings.Menu_EditWord, "", IntentKind.EditWordRequested, null, CanExecute(AppCommand.EditWord), false, true,
            Shortcut: "Ctrl+E", AccessKey: 'E', Ellipsis: true),
        new MenuActionSpec(Strings.Menu_DuplicateWord, "", IntentKind.DuplicateWordRequested, null, CanExecute(AppCommand.DuplicateWord), false, true,
            Shortcut: "Ctrl+D", AccessKey: 'D'),
        new MenuActionSpec(Strings.Menu_DeleteWord, "", IntentKind.DeleteWordRequested, null, CanExecute(AppCommand.DeleteWord), false, true,
            AccessKey: 'L'),
        new MenuActionSpec(Strings.Menu_Search, "", IntentKind.FocusQueryBoxRequested, null, true, false, true,
            Shortcut: "Ctrl+F", AccessKey: 'F', GroupStart: true),
    };
}
