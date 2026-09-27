using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class LegendPanel : UserControl
{
    private LegendViewModel? _vm;

    public LegendPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        // タブを別の枠や独立ウィンドウへ運ぶと器ごと作り直されるので、張り直しは Loaded で行う。
        // 外れた器は文書を手放す（FlowDocument は同時に 1 つの表示器にしか載せられない）。
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) =>
        {
            Detach();
            LegendView.Document = null;
        };
    }

    private void Attach()
    {
        Detach();
        _vm = DataContext as LegendViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        ShowDocument(_vm.Document);
    }

    /// <summary>
    /// タブを別の窓へ運ぶと、新しい器の DataContextChanged が古い器の Unloaded より先に来る。
    /// 古い器が文書を持ったままだと差し込めずに例外になるので、先に取り上げる。
    /// </summary>
    private void ShowDocument(FlowDocument? document) => Host(LegendView, document);

    internal static void Host(FlowDocumentScrollViewer viewer, FlowDocument? document)
    {
        if (document is not null && LogicalTreeHelper.GetParent(document) is FlowDocumentScrollViewer owner && owner != viewer)
            owner.Document = null;
        viewer.Document = document;
    }

    private void Detach()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LegendViewModel.Document) && _vm is not null) ShowDocument(_vm.Document);
    }
}
