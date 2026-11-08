using System.Windows;
using Microsoft.Win32;
using ZasDictWin.Services;

namespace ZasDictWin.Views;

/// <summary>
/// 起動時に配色を決めて、配色の辞書（Themes/Dark.xaml か Themes/Light.xaml）と外見の辞書（Themes/Theme.xaml）を
/// この順でアプリに読み込む。
/// </summary>
public static class ThemePalette
{
    internal static readonly Uri ThemeUri = new("/ZasDictWin;component/Themes/Theme.xaml", UriKind.Relative);
    internal static readonly Uri DarkUri = new("/ZasDictWin;component/Themes/Dark.xaml", UriKind.Relative);
    internal static readonly Uri LightUri = new("/ZasDictWin;component/Themes/Light.xaml", UriKind.Relative);

    /// <summary>
    /// 窓を 1 つも作る前に呼ぶ。外見のスタイルは配色を StaticResource で引き、引いた時点の値で固まる。
    /// App.xaml で配色と外見を並べて読み込むと、外見を読み込んだ時点で先に読んだ配色に結び付いてしまい、
    /// 後から配色を差し替えてもスタイルに届かない。そのため配色を選んでから外見を読み込む。
    /// </summary>
    public static void Load(Application app, AppTheme theme)
    {
        var palette = Resolve(theme) == AppTheme.Light ? LightUri : DarkUri;
        app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(palette));
        app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(ThemeUri));
    }

    public static AppTheme Resolve(AppTheme theme) => theme switch
    {
        AppTheme.System => SystemUsesLightTheme() ? AppTheme.Light : AppTheme.Dark,
        _ => theme,
    };

    /// <summary>Windows の［個人用設定］→［色］の「アプリ モード」。読めない古い Windows ではダーク扱い。</summary>
    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}
