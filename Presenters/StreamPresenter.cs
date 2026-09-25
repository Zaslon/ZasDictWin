using System.ComponentModel;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Presenters;

/// <summary>
/// 配信用ウィンドウ（単語・単語数）の描画パラメータの入れ物。本体の状態（MainViewModel）を
/// 丸ごと渡さず、配信で見せる値だけを StreamPresenter が写す。
/// </summary>
public sealed class StreamViewState : ViewModelBase
{
    private string _background = "";
    private bool _isTopmost;
    private Word? _selectedWord;
    private string _wordCountLabel = "";
    private double _headwordSize, _bodySize, _labelSize, _placeholderSize;
    private bool _showTranslations, _showContents;

    public string Background { get => _background; internal set => Set(ref _background, value); }
    public bool IsTopmost { get => _isTopmost; internal set => Set(ref _isTopmost, value); }

    public Word? SelectedWord
    {
        get => _selectedWord;
        internal set { if (Set(ref _selectedWord, value)) Raise(nameof(HasSelection)); }
    }

    public bool HasSelection => SelectedWord is not null;
    public string WordCountLabel { get => _wordCountLabel; internal set => Set(ref _wordCountLabel, value); }
    public double HeadwordSize { get => _headwordSize; internal set => Set(ref _headwordSize, value); }
    public double BodySize { get => _bodySize; internal set => Set(ref _bodySize, value); }
    public double LabelSize { get => _labelSize; internal set => Set(ref _labelSize, value); }
    public double PlaceholderSize { get => _placeholderSize; internal set => Set(ref _placeholderSize, value); }
    public bool ShowTranslations { get => _showTranslations; internal set => Set(ref _showTranslations, value); }
    public bool ShowContents { get => _showContents; internal set => Set(ref _showContents, value); }
}

/// <summary>
/// 配信用ウィンドウと凡例の描画パラメータを出し直す。凡例の Markdown は文字サイズ倍率ごとに
/// FlowDocument へ描き直す必要がある（倍率は設定と Ctrl＋ホイールの両方から変わる）。
/// </summary>
public sealed class StreamPresenter
{
    private readonly MainViewModel _state;
    private readonly AppSettings _settings;

    public StreamPresenter(AppMediator mediator, MainViewModel state, AppRoot root)
    {
        _state = state;
        _settings = state.Settings;
        mediator.Changed += _ => Refresh();
        root.ViewCommandIssued += command =>
        {
            if (command is HostCommand.RefreshStreamHosts) Refresh();
        };
        // 一覧で選び直した単語は TwoWay のバインドで先に入るので、裁定を待たずに追随させる。
        state.PropertyChanged += OnStateChanged;
        FontScaleState.Instance.PropertyChanged += OnFontScaleChanged;
        Refresh();
    }

    public StreamViewState View { get; } = new();

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedWord) or nameof(MainViewModel.WordCountLabel) or null)
            Refresh();
    }

    private void OnFontScaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontScaleState.Scale)) RefreshLegends();
    }

    private void Refresh()
    {
        var scale = _settings.StreamFontScale;
        View.Background = _settings.StreamBackground;
        View.IsTopmost = _settings.StreamWindowTopmost;
        View.ShowTranslations = _settings.StreamShowTranslations;
        View.ShowContents = _settings.StreamShowContents;
        View.HeadwordSize = 30 * scale;
        View.BodySize = 15 * scale;
        View.LabelSize = 12 * scale;
        View.PlaceholderSize = 22 * scale;
        View.SelectedWord = _state.SelectedWord;
        View.WordCountLabel = _state.WordCountLabel;
        RefreshLegends();
    }

    /// <summary>凡例のうち、今の文字サイズ倍率で描いていないものを描き直す。</summary>
    private void RefreshLegends()
    {
        var scale = FontScaleState.Instance.Scale;
        foreach (var legend in _state.Layout.Overlays.OfType<LegendViewModel>())
        {
            if (legend.DocumentScale.Equals(scale) && legend.Document is not null) continue;
            legend.Document = Markdown.ToFlowDocument(legend.LegendMarkdown, scale);
            legend.DocumentScale = scale;
        }
    }
}
