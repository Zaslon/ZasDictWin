using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using ZasDictWin.Services;
using ZasDictWin.Views;

namespace ZasDictWin.Tests;

public class ThemePaletteTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>リポジトリの Themes フォルダ。テストの出力先から親をたどって探す。</summary>
    private static string ThemeFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "Themes", name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(name);
    }

    private static HashSet<string> Keys(string name)
        => XDocument.Load(ThemeFile(name)).Root!.Elements()
            .Select(e => (string?)e.Attribute(X + "Key"))
            .OfType<string>()
            .ToHashSet();

    // 片方にしか無いキーは、その配色のときだけ「リソースが見つからない」で落ちる。
    [Fact]
    public void LightAndDark_HaveTheSameKeys()
        => Assert.Equal(Keys("Dark.xaml").OrderBy(k => k), Keys("Light.xaml").OrderBy(k => k));

    // Theme.xaml の中に色を置くと、同じ辞書の中の参照がビルド時に結び付き、ライトへの差し替えが届かない。
    [Fact]
    public void Theme_HoldsNoColors()
    {
        var colors = XDocument.Load(ThemeFile("Theme.xaml")).Root!.Elements()
            .Where(e => e.Name.LocalName is "Color" or "SolidColorBrush");
        Assert.Empty(colors);
    }

    // Theme.xaml のスタイルは配色を StaticResource で引く。選んだ配色がキー付きのスタイルにも暗黙のスタイル
    // （TargetType だけのもの）にも届くことを、本物の App で確かめる（Light.xaml は実行時に初めて読むので、
    // 壊れていても起動まで気づかない）。他のテストのために、終わったら元の辞書に戻す。
    [Fact]
    public void LightPalette_ReachesStylesInTheTheme() => AppThread.Run(() =>
    {
        var app = Application.Current;
        var saved = app.Resources.MergedDictionaries.ToList();
        try
        {
            app.Resources.MergedDictionaries.Clear();
            ThemePalette.Load(app, AppTheme.Light);
            var panel = ((SolidColorBrush)app.FindResource("Panel")).Color;
            var raised = ((SolidColorBrush)app.FindResource("Raised")).Color;

            Assert.Equal(panel, BrushOf((Style)app.FindResource("Card"), Border.BackgroundProperty));
            Assert.Equal(raised, BrushOf((Style)app.FindResource(typeof(Button)), Control.BackgroundProperty));
        }
        finally
        {
            app.Resources.MergedDictionaries.Clear();
            foreach (var d in saved) app.Resources.MergedDictionaries.Add(d);
        }
    });

    private static Color BrushOf(Style style, DependencyProperty property)
        => ((SolidColorBrush)style.Setters.OfType<Setter>().Single(s => s.Property == property).Value).Color;

    [Theory]
    [InlineData(AppTheme.Light, AppTheme.Light)]
    [InlineData(AppTheme.Dark, AppTheme.Dark)]
    public void ExplicitChoice_IsUsedAsIs(AppTheme chosen, AppTheme expected)
        => Assert.Equal(expected, ThemePalette.Resolve(chosen));

    [Fact]
    public void Default_FollowsWindows() => Assert.Equal(AppTheme.System, new AppSettings().Theme);

    [Fact]
    public void FollowingWindows_ResolvesToLightOrDark()
        => Assert.Contains(ThemePalette.Resolve(AppTheme.System), new[] { AppTheme.Light, AppTheme.Dark });
}
