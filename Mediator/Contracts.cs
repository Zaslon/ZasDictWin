using System.Windows;
using ZasDictWin.Root;

namespace ZasDictWin.Mediator;

public readonly record struct Intent(
    IntentKind Kind, object? Payload, IntentContext Context, Point? ScreenPoint);

public enum ShellPhase { Normal, Modal, ModalBusy, Closing }

public enum DocPhase { NoDocument, Clean, Dirty }

public enum DragPhase
{
    Idle,
    AreaGripArmed, AreaSplitPreview, AreaSplitBlocked, AreaJoinPreview,
    TabGripArmed, TabOverLeaf, TabOverSourceEdge, TabOverNoLeaf, TabOutside,
    RowGripArmed, RowReordering,
    SplitGripDragging,
}

public enum AppCommand
{
    Open, NewDictionary, Save, SaveAs, GitHubLoad, GitHubCommit,
    NewWord, EditWord, DuplicateWord, DeleteWord,
    ShowBrowser, ShowSettings, ShowExamples, EditExample,
    ShowDialectTool, ShowIpaTool, ShowStats, ShowLegend, ShowChangelog,
}

/// <summary>ボタンの有効・無効を決める材料。View も Presenter もここを組み立てない（Mediator だけ）。</summary>
public readonly record struct GateContext(
    ShellPhase Shell, DocPhase Doc,
    bool GitHubMode, bool GitHubBusy, bool GitHubSynced,
    bool EditorOpen, IReadOnlySet<string> OpenKinds, bool HasSelection);

/// <summary>確認ダイアログの中身。View は文言とボタン並びだけを受け取り、押されたら番号を Intent で返す。</summary>
public sealed record ConfirmSpec(string Title, string Message, IReadOnlyList<ConfirmChoice> Choices);

public sealed record ConfirmChoice(string Label, bool IsDanger);

public readonly record struct StateChange(ShellPhase Shell, DragPhase Drag, DocPhase Doc);

/// <summary>本体の窓を閉じるときに控える大きさ。最大化・最小化中は元に戻したときの大きさ（RestoreBounds）。</summary>
public readonly record struct WindowBounds(double Width, double Height, bool Maximized);
