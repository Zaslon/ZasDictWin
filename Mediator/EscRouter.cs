using ZasDictWin.Root;

namespace ZasDictWin.Mediator;

/// <summary>
/// Esc の行き先。本体・独立ウィンドウ・設定ウィンドウのどこで押しても同じ表で決める。
/// 1 回の Esc で閉じるのは 1 つだけで、上の層（一覧 → 操作中のドラッグ → 確認ダイアログ → タブ）から順に畳む。
/// </summary>
public static class EscRouter
{
    public enum EscTarget
    {
        None,
        ClosePopupLayer,
        CancelDrag,
        DismissModal,
        CloseActiveTabInShell,
        CloseActiveTabInHost,
    }

    /// <summary>優先順は上から順（ClosePopupLayer が最優先）。1 回の Esc で 1 つだけ。</summary>
    public static EscTarget Resolve(
        bool popupLayerOpen, DragPhase drag, ShellPhase shell,
        HostRole role, bool hasClosableActiveTab)
    {
        if (popupLayerOpen) return EscTarget.ClosePopupLayer;
        if (drag != DragPhase.Idle) return EscTarget.CancelDrag;
        if (shell == ShellPhase.Modal) return EscTarget.DismissModal;
        return role switch
        {
            HostRole.Shell when hasClosableActiveTab => EscTarget.CloseActiveTabInShell,
            HostRole.Floating when hasClosableActiveTab => EscTarget.CloseActiveTabInHost,
            // 設定ウィンドウでは窓そのものを閉じる。
            HostRole.Settings => EscTarget.CloseActiveTabInHost,
            // 中央の単語詳細などの据え置きタブ、配信用ウィンドウでは何もしない。
            _ => EscTarget.None,
        };
    }
}
