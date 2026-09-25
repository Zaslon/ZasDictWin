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

    /// <summary>空の枠を畳む。隣の枠がその場所を引き取る（角を隣へ引く結合と同じ結果）。</summary>
    private void CloseArea_Click(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement button) button.RaiseIntent(IntentKind.AreaCloseRequested);
    }
}
