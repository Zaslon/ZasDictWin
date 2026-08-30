using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views.Overlays;

public partial class InfoOverlay : UserControl
{
    public InfoOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildLegend();
        Unloaded += (_, _) => FontScaleState.Instance.PropertyChanged -= OnFontScaleChanged;
        FontScaleState.Instance.PropertyChanged += OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontScaleState.Scale)) RebuildLegend();
    }

    /// <summary>凡例タブの Markdown を現在の文字サイズ倍率で FlowDocument に描画し直す。</summary>
    private void RebuildLegend()
    {
        if (DataContext is not InfoViewModel vm) return;
        LegendView.Document = Markdown.ToFlowDocument(vm.LegendMarkdown, FontScaleState.Instance.Scale);
    }

    /// <summary>
    /// 更新履歴は追記順（古い → 新しい）なので、画面に出た時点で最新行が見えるよう一度だけ末尾へ送る。
    /// タブをまたいだときも ScrollViewer が読み込まれ直すため、そのつど最新行から始まる。
    /// </summary>
    private void ChangelogScroll_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;
        scroll.ScrollToEnd();
        // Loaded の時点では高さが確定していないことがあるため、レイアウトが一通り終わった後にもう一度送る。
        scroll.Dispatcher.BeginInvoke(new Action(scroll.ScrollToEnd), DispatcherPriority.Loaded);
    }
}
