using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ZasDictWin.Views;

/// <summary>
/// 入力欄の右クリックで WPF が自前で開く標準メニュー（切り取り／コピー／貼り付けと IME の再変換候補）に、
/// Themes/Theme.xaml の EditorContextMenu / EditorMenuItem を当てる。
/// このメニューは非公開の派生型（EditorContextMenu 等）で、型で当てる暗黙スタイルが届かない。
/// 既定のままだと、面はシステムの明るい色になる。一方で文字はアプリ全体の暗黙の TextBlock スタイル（Text 色）を拾うので、
/// ダーク配色では白地に白い字になって読めなくなる。
/// </summary>
internal static class EditorContextMenuStyler
{
    /// <summary>Theme.xaml を読み込んだ後に一度だけ呼ぶ。</summary>
    public static void Register()
    {
        // Loaded は初回の描画より前に来るので、既定の見た目が一瞬出るのを防げる。
        // Opened は Loaded が来ない開き方に備えた保険（Style を当て済みなら何もしない）。
        EventManager.RegisterClassHandler(typeof(ContextMenu), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnMenuEvent));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuEvent));
    }

    private static void OnMenuEvent(object sender, RoutedEventArgs e)
    {
        // アプリが自分で Style を付けたメニューには触れない。対象は入力欄から WPF が開いたものだけ。
        if (sender is not ContextMenu menu || menu.Style is not null || menu.PlacementTarget is not TextBoxBase) return;
        if (menu.TryFindResource("EditorContextMenu") is not Style menuStyle
            || menu.TryFindResource("EditorMenuItem") is not Style itemStyle) return;

        menu.Style = menuStyle;
        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi && mi.Style is null) mi.Style = itemStyle;
            else if (item is Separator sep && menu.TryFindResource(MenuItem.SeparatorStyleKey) is Style sepStyle) sep.Style = sepStyle;
        }
    }
}
