using System.ComponentModel;
using System.Windows.Controls;
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
}
