namespace ZasDictWin.Mediator;

/// <summary>
/// ボタンの有効・無効。確認ダイアログ中はどれも押させない・同じ種類の画面は 1 枚まで・
/// 編集中は辞書を差し替えさせない、という規則を 1 つの表にしたもの。
/// </summary>
public static class CommandGate
{
    public static bool CanExecute(AppCommand command, GateContext ctx)
    {
        // 確認ダイアログを開いている間は、先にそれへ答えてもらう（ショートカットはオーバーレイの上からでも届くため）。
        if (ctx.Shell is ShellPhase.Modal or ShellPhase.Closing) return false;
        // GitHub と通信している間は、辞書そのものを入れ替える・書き出す操作だけを止める
        // （連打で二重コミットにならないように。単語の編集やツールは通信と関係なく使える）。
        var busy = ctx.Shell == ShellPhase.ModalBusy || ctx.GitHubBusy;
        var hasDoc = ctx.Doc != DocPhase.NoDocument;

        return command switch
        {
            // 編集中に辞書を差し替えると、保存の宛先だけが入れ替わって別の辞書に書き込まれる。
            AppCommand.Open or AppCommand.NewDictionary => !busy && !ctx.EditorOpen,
            AppCommand.Save or AppCommand.SaveAs => !busy && hasDoc,
            AppCommand.GitHubLoad => ctx.GitHubMode && !busy && !ctx.EditorOpen,
            // 同期済み（読み込み・コミット以降に編集していない）ならコミットしても差が出ない。
            AppCommand.GitHubCommit => ctx.GitHubMode && !busy && hasDoc && !ctx.GitHubSynced,
            // 同じ画面は 1 枚まで。開いている種類のボタンは無効にして、書きかけの入力が差し替えで飛ぶのを防ぐ。
            AppCommand.NewWord => hasDoc && !IsOpen("WordEditViewModel", ctx),
            // 編集中の単語を消せると、開いたままのエディタが宙に浮く。
            AppCommand.EditWord or AppCommand.DuplicateWord or AppCommand.DeleteWord
                => hasDoc && !IsOpen("WordEditViewModel", ctx) && ctx.HasSelection,
            AppCommand.ShowExamples => hasDoc && !IsOpen("ExamplesViewModel", ctx),
            AppCommand.EditExample => !IsOpen("ExampleEditViewModel", ctx),
            AppCommand.ShowBrowser => !IsOpen("BrowserTabViewModel", ctx),
            // ツール類と設定は開いていても押せる（押し直すとその窓が前に出る）。
            AppCommand.ShowSettings or AppCommand.ShowDialectTool or AppCommand.ShowIpaTool
                or AppCommand.ShowStats or AppCommand.ShowLegend or AppCommand.ShowChangelog => true,
            _ => false,
        };
    }

    /// <summary>その種類のタブがすでに開いているか（同じ種類は 1 枚まで）。</summary>
    public static bool IsOpen(string kind, GateContext ctx) => ctx.OpenKinds.Contains(kind);

    /// <summary>ツール類（すでに開いていれば押し直せて前に出す）か。</summary>
    public static bool IsReopenable(AppCommand command) => command is
        AppCommand.ShowSettings or AppCommand.ShowDialectTool or AppCommand.ShowIpaTool
        or AppCommand.ShowStats or AppCommand.ShowLegend or AppCommand.ShowChangelog;
}
