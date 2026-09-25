using ZasDictWin.Mediator;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Presenters;

/// <summary>
/// 本体の窓の描画パラメータ（ボタンの有効・無効、ヘッダの階層メニュー、確認ダイアログの層、
/// 一覧を開いている間の WebView2 の隠し、フォーカス移動の指示）を Mediator の裁定から写す。
/// </summary>
public sealed class ShellPresenter
{
    private readonly AppMediator _mediator;
    private readonly MainViewModel _state;

    public ShellPresenter(AppMediator mediator, MainViewModel state, AppRoot root)
    {
        _mediator = mediator;
        _state = state;
        mediator.Changed += _ => Refresh();
        mediator.ConfirmRequested += spec => _state.ModalOverlay = spec is null ? null : new ChoiceViewModel(spec);
        root.ViewCommandIssued += OnViewCommand;
        Refresh();
    }

    /// <summary>
    /// ボタンの有効・無効とメニューの中身を出し直す。IsEnabled のバインドは WPF が自動で再評価しないので、
    /// 状態が変わるたび（Changed）にここで出し直す必要がある。
    /// </summary>
    private void Refresh()
    {
        var m = _mediator;
        _state.CanOpen = m.CanExecute(AppCommand.Open);
        _state.CanNewDictionary = m.CanExecute(AppCommand.NewDictionary);
        _state.CanSave = m.CanExecute(AppCommand.Save);
        _state.CanSaveAs = m.CanExecute(AppCommand.SaveAs);
        _state.CanGitHubLoad = m.CanExecute(AppCommand.GitHubLoad);
        _state.CanGitHubCommit = m.CanExecute(AppCommand.GitHubCommit);
        _state.CanNewWord = m.CanExecute(AppCommand.NewWord);
        _state.CanEditWord = m.CanExecute(AppCommand.EditWord);
        _state.CanDuplicateWord = m.CanExecute(AppCommand.DuplicateWord);
        _state.CanDeleteWord = m.CanExecute(AppCommand.DeleteWord);
        _state.CanShowBrowser = m.CanExecute(AppCommand.ShowBrowser);
        _state.CanShowSettings = m.CanExecute(AppCommand.ShowSettings);
        _state.CanShowExamples = m.CanExecute(AppCommand.ShowExamples);
        _state.CanEditExample = m.CanExecute(AppCommand.EditExample);
        _state.CanShowDialectTool = m.CanExecute(AppCommand.ShowDialectTool);
        _state.CanShowIpaTool = m.CanExecute(AppCommand.ShowIpaTool);
        _state.CanShowStats = m.CanExecute(AppCommand.ShowStats);
        _state.CanShowLegend = m.CanExecute(AppCommand.ShowLegend);
        _state.CanShowChangelog = m.CanExecute(AppCommand.ShowChangelog);

        // 中身が同じなら差し替えない。MenuButton は Items が変わると開いている一覧を畳むので、
        // 一覧を開いたこと自体で起きる裁定のたびに差し替えると、開いた瞬間に閉じてしまう。
        if (!_state.FileMenuItems.SequenceEqual(m.BuildFileMenu())) _state.FileMenuItems = m.BuildFileMenu();
        if (!_state.ToolsMenuItems.SequenceEqual(m.BuildToolsMenu())) _state.ToolsMenuItems = m.BuildToolsMenu();
        if (!_state.WindowMenuItems.SequenceEqual(m.BuildWindowMenu())) _state.WindowMenuItems = m.BuildWindowMenu();
    }

    private void OnViewCommand(HostCommand command)
    {
        switch (command)
        {
            case HostCommand.SetPopupLayerOpen p:
                _state.Browser.IsOverlayOpen = p.Open;
                break;
            case HostCommand.MoveFocus f:
                _state.RequestFocus(f.Target);
                break;
        }
    }
}
