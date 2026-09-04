using System.ComponentModel;
using System.Windows.Controls;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class LegendPanel : UserControl
{
    public LegendPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RebuildLegend();
        // 文字サイズ倍率はこの画面の外（設定・Ctrl＋ホイール）から変わりうるので、出ている間は追従させる。
        // タブを別の枠や独立ウィンドウへ運ぶと器ごと作り直されるため、張り直しは Loaded で行う。
        Loaded += (_, _) =>
        {
            FontScaleState.Instance.PropertyChanged -= OnFontScaleChanged;
            FontScaleState.Instance.PropertyChanged += OnFontScaleChanged;
            RebuildLegend();
        };
        Unloaded += (_, _) => FontScaleState.Instance.PropertyChanged -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontScaleState.Scale)) RebuildLegend();
    }

    /// <summary>凡例の Markdown を現在の文字サイズ倍率で FlowDocument に描画し直す。</summary>
    private void RebuildLegend()
    {
        if (DataContext is not LegendViewModel vm) return;
        LegendView.Document = Markdown.ToFlowDocument(vm.LegendMarkdown, FontScaleState.Instance.Scale);
    }
}
