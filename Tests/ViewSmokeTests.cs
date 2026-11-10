using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;
using ZasDictWin.Views;
using ZasDictWin.Views.Overlays;

namespace ZasDictWin.Tests;

/// <summary>
/// XAML は実行時に初めて読まれるので、キー名の打ち間違いやテンプレートの不整合はビルドでは見つからず、
/// バインディングの誤りは例外にもならずに黙って効かない。本物の App のリソースで画面を組み立て、
/// 表示の設定の両端（最小・最大）でレイアウトまで通し、その間のバインディングの誤りも拾う。
/// </summary>
public class ViewSmokeTests
{
    private sealed class Collector : TraceListener
    {
        public List<string> Lines { get; } = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message is not null) Lines.Add(message); }
    }

    private static T Build<T>(Func<T> build, double width, double height) where T : FrameworkElement
    {
        var display = FontScaleState.Instance;
        var collector = new Collector();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(collector);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        T? last = null;
        try
        {
            foreach (var (density, scale, ui) in new[] { (Density.Standard, 1.0, 1.0), (Density.Compact, 2.0, 2.0) })
            {
                display.Density = density;
                display.Scale = scale;
                display.UiScale = ui;
                last = build();
                var root = last is Window w ? (FrameworkElement)w.Content : last;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
            }
        }
        finally
        {
            display.Density = Density.Standard;
            display.Scale = 1.0;
            display.UiScale = 1.0;
            PresentationTraceSources.DataBindingSource.Listeners.Remove(collector);
        }
        Assert.Empty(collector.Lines);
        return last!;
    }

    [Fact]
    public void MainWindow_BuildsAndLaysOut() => AppThread.Run(() =>
    {
        var h = new MediatorHarness();
        AppRoot.Initialize(h.M);
        var window = Build(() => new MainWindow(h.State), 1280, 800);

        var bar = (Panel)window.FindName("MenuBar");
        var menus = bar.Children.OfType<MenuButton>().ToList();
        Assert.Equal(new[] { "F", "E", "V", "T", "W", "S", "H" }, menus.Select(m => m.AccessKey));
        // メニューバーは Tab の移動先に含めない。
        Assert.All(menus, m => Assert.False(m.IsTabStop));
        Assert.All(menus, m => Assert.EndsWith($"({m.AccessKey})", m.Label));
    });

    [Fact]
    public void SettingsWindow_BuildsAndLaysOut() => AppThread.Run(() =>
    {
        var h = new MediatorHarness();
        AppRoot.Initialize(h.M);
        Build(() => new SettingsWindow(new SettingsViewModel(h.Services.Settings, null, new Dictionary<string, string>(), false)), 820, 760);
    });

    [Fact]
    public void DisplaySettings_BuildsAndLaysOut() => AppThread.Run(() =>
        Build(() => new DisplaySettingsOverlay { DataContext = new DisplaySettingsViewModel(new AppSettings()) }, 900, 700));

    [Fact]
    public void SearchPanel_RadioGroupsStopOnceForTab() => AppThread.Run(() =>
    {
        var h = new MediatorHarness();
        var panel = Build(() => new SearchPanel { DataContext = h.State }, 360, 700);

        var radios = Descendants<RadioButton>(panel).ToList();
        Assert.Equal(8, radios.Count);
        // 2 つのグループそれぞれで、Tab で止まるのは選択中の 1 つだけ。
        Assert.Equal(2, radios.Count(r => r.IsTabStop));
        Assert.All(radios.Where(r => r.IsTabStop), r => Assert.True(r.IsChecked));
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }
}
