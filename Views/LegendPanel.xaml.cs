using System.ComponentModel;
using System.Windows.Controls;
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
        LegendView.Document = _vm.Document;
    }

    private void Detach()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LegendViewModel.Document) && _vm is not null) LegendView.Document = _vm.Document;
    }
}
