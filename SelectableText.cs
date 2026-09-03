using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// 選択できる本文（<see cref="SelectableText"/> / <see cref="SelectableRichText"/>）に共通の始末。
/// </summary>
internal static class SelectableTextBehavior
{
    /// <summary>
    /// TextBox 系は中に ScrollViewer を抱えていて、自分がスクロールできるかどうかに関わらず
    /// ホイールを食ってしまう。本文として置くぶんには自分でスクロールする必要が無いので、
    /// 外側（単語詳細のスクロール）へ流し直す。
    /// </summary>
    public static void ForwardWheel(Control source, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;

        var parent = source.Parent as UIElement ?? VisualTreeHelper.GetParent(source) as UIElement;
        parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = source
        });
    }

    /// <summary>
    /// TextBox 系は ContextMenu が空でも、右クリックで標準の切り取り／コピーの一覧を自前で開く。
    /// それは Popup（別 HWND）なので OBS のウィンドウキャプチャに映らない。この層で畳んでおき、
    /// コピーは選んで Ctrl+C で取ってもらう。
    /// </summary>
    public static void SuppressContextMenu(ContextMenuEventArgs e) => e.Handled = true;
}

/// <summary>
/// マウスで選んでコピーできる本文。WPF の TextBlock は文字を選べないため、読み取り専用の TextBox を
/// 素の文字に見えるまで削ったものを代わりに使う（見た目は Theme.xaml の既定スタイルが受け持つ）。
/// TextBox の暗黙スタイル（入力欄の枠と面）を継がないよう、別の型として立ててある。
/// </summary>
public class SelectableText : TextBox
{
    static SelectableText() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(typeof(SelectableText)));

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        SelectableTextBehavior.ForwardWheel(this, e);
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        SelectableTextBehavior.SuppressContextMenu(e);
    }
}

/// <summary>
/// <see cref="SelectableText"/> の書式付き版。語源欄のようにイジェール語の語幹だけ別フォントで
/// 描く必要がある本文に使う（中身は <see cref="EtymologyText"/> が組み立てる）。
/// </summary>
public class SelectableRichText : RichTextBox
{
    static SelectableRichText() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectableRichText), new FrameworkPropertyMetadata(typeof(SelectableRichText)));

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        SelectableTextBehavior.ForwardWheel(this, e);
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        SelectableTextBehavior.SuppressContextMenu(e);
    }
}
