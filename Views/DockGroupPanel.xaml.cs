using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class DockGroupPanel : UserControl
{
    public DockGroupPanel()
    {
        InitializeComponent();
        this.AddIntentHandler(OnIntentPassing);
    }

    /// <summary>
    /// 中から上がってくる Intent に、この枠しか知らない事実（枠の番号・実寸・枠を基準にしたポインタ位置）を補う。
    /// 裁定はせず、止めもしない（必ず根まで上げる）。入れ子の内側の枠が先に書くので、外側は上書きしない。
    /// </summary>
    private void OnIntentPassing(object? sender, UiIntentEventArgs e)
    {
        if (DataContext is not DockLeaf leaf || e.Context.LeafId is not null) return;
        e.Context.LeafId = leaf.Id;
        e.Context.LeafSize ??= new Size(ActualWidth, ActualHeight);
        e.Context.LeafLocalPoint ??= Mouse.GetPosition(this);
    }

    /// <summary>タブを押したら前に出す。掴んで運ぶ側（OverlayDrag）とは別に効かせる。
    /// 中ボタンなら前に出さず閉じる（ブラウザの中クリックと同じ操作感。据え置きのタブかどうかは裁定側が見る）。</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverlayViewModel vm } tab) return;
        var args = new UiIntentEventArgs(
            e.ChangedButton == MouseButton.Middle ? IntentKind.TabCloseRequested : IntentKind.TabSelectRequested, vm);
        args.Context.TabKind = vm.Kind;
        tab.RaiseIntent(args);
    }

    /// <summary>上端のタブ列の高さ。タブの無い枠ではタブ列を出さないので 0。</summary>
    internal double TabStripHeight => TabStrip.IsVisible ? TabStrip.ActualHeight : 0;

    /// <summary>
    /// 枠を基準にした位置がタブ列の上なら、そこへ差し込むときの位置（何番目のタブの手前か。末尾なら並びの数）。
    /// タブ列の外なら -1。タブは折り返して複数行に並ぶので、まず高さの合う行を選び、
    /// その行で中心が位置より右にある最初のタブの手前とする。タブの隙間や列の余白の上でも位置が決まる。
    /// </summary>
    internal int TabSlotAt(Point local)
    {
        if (!TabStrip.IsVisible || local.Y < 0 || local.Y > TabStrip.ActualHeight) return -1;

        var tabs = new List<(int Index, Rect Bounds)>();
        for (var i = 0; i < TabList.Items.Count; i++)
        {
            if (TabList.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { IsVisible: true } tab)
                tabs.Add((i, tab.TransformToAncestor(this).TransformBounds(new Rect(tab.RenderSize))));
        }
        if (tabs.Count == 0) return -1;

        var nearest = tabs.MinBy(t => Math.Abs(t.Bounds.Top + t.Bounds.Height / 2 - local.Y)).Bounds.Top;
        var row = tabs.Where(t => Math.Abs(t.Bounds.Top - nearest) < 1).ToList();
        foreach (var (index, bounds) in row)
        {
            if (bounds.Left + bounds.Width / 2 > local.X) return index;
        }
        return row[^1].Index + 1;
    }

    /// <summary>空の枠を畳む。隣の枠がその場所を引き取る（角を隣へ引く結合と同じ結果）。</summary>
    private void CloseArea_Click(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement button) button.RaiseIntent(IntentKind.AreaCloseRequested);
    }
}
