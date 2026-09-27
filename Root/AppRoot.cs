using System.Windows;
using ZasDictWin.Mediator;

namespace ZasDictWin.Root;

/// <summary>AdornerDecorator に描く一覧（DropDown / MenuButton）。Esc などの裁定で Root が畳む。</summary>
public interface IPopupLayer
{
    void Close();
}

/// <summary>
/// すべてのコンポーネントの論理的な根。窓（IUiHost）を子として持ち、
/// 各窓の Visual Tree の最上位で受けた Intent をここへ集めて Mediator 1 つに裁定させる。
/// </summary>
public sealed class AppRoot
{
    private static AppRoot? _current;

    private readonly List<IUiHost> _hosts = new();
    private readonly Dictionary<IUiHost, (UIElement Tree, EventHandler<UiIntentEventArgs> Handler)> _attached = new();

    /// <summary>いま開いている一覧。開いた Intent の Payload で覚え、Esc などの裁定で畳む。</summary>
    private IPopupLayer? _openPopup;

    public AppRoot(AppMediator mediator)
    {
        Mediator = mediator;
        mediator.HitTester = HitTest;
        mediator.HostLookup = HostById;
        mediator.HostCommandIssued += Execute;
    }

    /// <summary>アプリの唯一の根。App.OnStartup で <see cref="Initialize"/> してから使う
    /// （それより前に触ると窓の生成より先に Mediator が要るため例外にする）。</summary>
    public static AppRoot Current => _current ?? throw new InvalidOperationException("AppRoot is not initialized.");

    public static AppRoot Initialize(AppMediator mediator) => _current = new AppRoot(mediator);

    public AppMediator Mediator { get; }

    /// <summary>Mediator が出す HostCommand の実行役。App 起動時に View 層が差し込む。</summary>
    public IHostFactory? HostFactory { get; set; }

    /// <summary>窓の開け閉め以外の HostCommand（描画パラメータの写しや View にしかできない操作）。
    /// Presenter と View 層が受ける。</summary>
    public event Action<HostCommand>? ViewCommandIssued;

    public IReadOnlyList<IUiHost> Hosts => _hosts;

    public IUiHost? Shell => _hosts.FirstOrDefault(h => h.Role == HostRole.Shell);

    public IUiHost? HostById(Guid id) => _hosts.FirstOrDefault(h => h.HostId == id);

    /// <summary>窓を Root の子として登録する。treeRoot は Intent を受ける最上位要素。</summary>
    public void Attach(IUiHost host, UIElement treeRoot)
    {
        if (_attached.ContainsKey(host)) return;
        EventHandler<UiIntentEventArgs> handler = (_, e) => OnIntent(host, e);
        treeRoot.AddIntentHandler(handler);
        _attached[host] = (treeRoot, handler);
        _hosts.Add(host);
    }

    public void Detach(IUiHost host)
    {
        if (!_attached.Remove(host, out var entry)) return;
        entry.Tree.RemoveIntentHandler(entry.Handler);
        _hosts.Remove(host);
    }

    private void OnIntent(IUiHost host, UiIntentEventArgs e)
    {
        e.Context.HostId = host.HostId;
        e.Handled = true;
        Mediator.Dispatch(new Intent(e.Kind, e.Payload, e.Context, e.ScreenPoint));

        // 一覧は裁定の後で覚え直す。先に覚えると、裁定の中で「前に開いていた一覧を畳む」が今開いた方を畳んでしまう。
        switch (e.Kind)
        {
            case IntentKind.PopupLayerOpened when e.Payload is IPopupLayer opened:
                _openPopup = opened;
                break;
            case IntentKind.PopupLayerClosed when ReferenceEquals(e.Payload, _openPopup):
                _openPopup = null;
                break;
        }
    }

    /// <summary>手前から順に窓を見る当たり判定。Floating が先（Owner を持つので必ず本体より上）、
    /// Floating どうしは IsActiveHost が真のものを先に見る。どの窓にも乗っていなければ null。</summary>
    public HitLeaf? HitTest(Point screen)
    {
        // 独立ウィンドウは本体を Owner にしてあるので必ず本体より手前にある。
        // タブの運び先の当たり判定はこの前後関係に依存している。
        var ordered = _hosts.Where(h => h.Role == HostRole.Floating).OrderByDescending(h => h.IsActiveHost)
            .Concat(_hosts.Where(h => h.Role == HostRole.Shell));
        foreach (var host in ordered)
        {
            if (!host.ContainsScreenPoint(screen)) continue;
            return host.TryHitLeaf(screen, out var leafId, out var size, out var local, out var strip, out var slot)
                ? new HitLeaf(host, leafId, size, local, strip, slot)
                : new HitLeaf(host, -1, default, default);
        }
        return null;
    }

    private void Execute(HostCommand command)
    {
        switch (command)
        {
            case HostCommand.OpenFloating open:
                HostFactory?.OpenFloating(open.Host);
                break;
            case HostCommand.CloseHost close:
                if (HostById(close.HostId) is { } closing) HostFactory?.Close(closing);
                break;
            case HostCommand.FocusHost focus:
                HostById(focus.HostId)?.FocusFromRoot();
                break;
            case HostCommand.OpenSettings settings:
                HostFactory?.OpenSettings(settings.State);
                break;
            case HostCommand.ToggleStream stream:
                Toggle(HostRole.Stream, stream.Open, () => HostFactory?.OpenStream());
                break;
            case HostCommand.ToggleCount count:
                Toggle(HostRole.Count, count.Open, () => HostFactory?.OpenCount());
                break;
            case HostCommand.ClosePopupLayer:
                var popup = _openPopup;
                _openPopup = null;
                popup?.Close();
                break;
            case HostCommand.ApplyShellSize or HostCommand.ShowDragGhost or HostCommand.HideDragGhost
                or HostCommand.SetPopupLayerOpen or HostCommand.MoveFocus or HostCommand.RefreshStreamHosts
                or HostCommand.FlashStatus:
                ViewCommandIssued?.Invoke(command);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void Toggle(HostRole role, bool open, Action openHost)
    {
        var existing = _hosts.FirstOrDefault(h => h.Role == role);
        if (open && existing is null) openHost();
        else if (!open && existing is not null) HostFactory?.Close(existing);
    }
}
