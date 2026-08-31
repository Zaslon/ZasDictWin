using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

public sealed class ChoiceItem
{
    public string Label { get; init; } = "";
    public bool IsDanger { get; init; }
    public ICommand Command { get; init; } = new RelayCommand(() => { });
}

/// <summary>確認ダイアログと右クリックメニューの代替を兼ねる汎用オーバーレイ。</summary>
public sealed class ChoiceViewModel : OverlayViewModel
{
    public string Message { get; }
    public ObservableCollection<ChoiceItem> Choices { get; } = new();

    public ChoiceViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public ChoiceViewModel Add(string label, Action action, bool isDanger = false)
    {
        Choices.Add(new ChoiceItem
        {
            Label = label,
            IsDanger = isDanger,
            Command = new RelayCommand(() => { RequestClose?.Invoke(); action(); })
        });
        return this;
    }

    public ChoiceViewModel AddCancel(string label = "キャンセル")
    {
        Choices.Add(new ChoiceItem { Label = label, Command = new RelayCommand(() => RequestClose?.Invoke()) });
        return this;
    }
}

public sealed class ToolsViewModel : OverlayViewModel
{
    private string _input = "";
    private string _ipaInput = "";
    private DialectResult _dialects = new("", "", "", "");
    private string _ipaSpelling = "";
    private bool _faithfulTitauini = Dialects.FaithfulTitauini;

    public ToolsViewModel(string? initialInput = null)
    {
        Title = "ツール";
        _input = initialInput ?? "";
        Convert();
    }

    /// <summary>強勢のある母音を大文字で入力する。</summary>
    public string Input
    {
        get => _input;
        set { if (Set(ref _input, value)) Convert(); }
    }

    public string IpaInput
    {
        get => _ipaInput;
        set { if (Set(ref _ipaInput, value)) IpaSpelling = Ipa.ToSpelling(value); }
    }

    public string IpaSpelling { get => _ipaSpelling; private set => Set(ref _ipaSpelling, value); }

    public string Sekore => _dialects.Sekore;
    public string Titauini => _dialects.Titauini;
    public string Kaiko => _dialects.Kaiko;
    public string Arzafire => _dialects.Arzafire;

    /// <summary>Python 版の titauini() が 3母音化の結果を捨てる挙動をそのまま使うか。</summary>
    public bool FaithfulTitauini
    {
        get => _faithfulTitauini;
        set
        {
            if (!Set(ref _faithfulTitauini, value)) return;
            Dialects.FaithfulTitauini = value;
            Convert();
        }
    }

    private void Convert()
    {
        _dialects = Input.Length == 0 ? new DialectResult("", "", "", "") : Dialects.Convert(Input);
        Raise(nameof(Sekore));
        Raise(nameof(Titauini));
        Raise(nameof(Kaiko));
        Raise(nameof(Arzafire));
    }
}

public sealed class SettingsViewModel : OverlayViewModel
{
    private readonly AppSettings _settings;
    private readonly OtmDocument? _doc;
    private readonly Action _apply;

    public SettingsViewModel(AppSettings settings, OtmDocument? doc, Action apply)
    {
        _settings = settings;
        _doc = doc;
        _apply = apply;
        Title = "設定";

        SortOrder = settings.SortOrder;
        FontScale = settings.FontScale;
        AutoSave = settings.AutoSave;
        HeksaEnabled = settings.HeksaEnabled;
        HeksaFontPath = settings.HeksaFontPath ?? "";
        ReciprocalText = FormatReciprocal(settings.ReciprocalMap);

        StreamBackground = settings.StreamBackground;
        StreamFontScale = settings.StreamFontScale;
        StreamTopmost = settings.StreamWindowTopmost;
        StreamShowTranslations = settings.StreamShowTranslations;
        StreamShowContents = settings.StreamShowContents;

        BrowserVisible = settings.BrowserVisible;
        BrowserStartUrl = settings.BrowserStartUrl;

        if (doc is not null)
        {
            Punctuations = string.Concat(
                (doc.ZpdicOnline["punctuations"] as JsonArray)?
                    .Select(n => n?.GetValue<string>() ?? "") ?? Array.Empty<string>());
            IgnoredPattern = doc.ZpdicOnline["ignoredPattern"]?.GetValue<string>() ?? "";
        }

        ApplyCommand = new RelayCommand(ApplyAll);
        PickFontCommand = new RelayCommand(PickFont);
        ResetReciprocalCommand = new RelayCommand(() => ReciprocalText = FormatReciprocal(RelationService.DefaultMap));
    }

    private static string FormatReciprocal(IEnumerable<KeyValuePair<string, string>> map)
        => string.Join(Environment.NewLine, map.Select(kv => $"{kv.Key}={kv.Value}"));

    public string SortOrder { get; set; }
    public double FontScale { get; set; }
    public bool AutoSave { get; set; }

    private bool _heksaEnabled;
    public bool HeksaEnabled
    {
        get => _heksaEnabled;
        set { if (Set(ref _heksaEnabled, value)) RaisePreview(); }
    }

    private string _heksaFontPath = "";
    public string HeksaFontPath
    {
        get => _heksaFontPath;
        set
        {
            if (!Set(ref _heksaFontPath, value)) return;
            _previewFont = HeadwordFontState.Load(value);
            RaisePreview();
        }
    }

    private FontFamily? _previewFont;

    /// <summary>［適用］前でも選んだ ttf をその場で確かめられるように、入力中のパスから読み直す。</summary>
    public FontFamily PreviewFontFamily => _previewFont ?? HeadwordFontState.Fallback;
    public bool HasPreviewFont => HeksaEnabled && _previewFont is not null;
    public bool IsFontMissing => HeksaEnabled && _previewFont is null;

    private void RaisePreview()
    {
        Raise(nameof(PreviewFontFamily));
        Raise(nameof(HasPreviewFont));
        Raise(nameof(IsFontMissing));
    }

    private string _reciprocalText = "";
    public string ReciprocalText { get => _reciprocalText; set => Set(ref _reciprocalText, value); }

    public string Punctuations { get; set; } = "";
    public string IgnoredPattern { get; set; } = "";
    public bool HasDictionary => _doc is not null;

    private string _streamBackground = "";

    /// <summary>
    /// 単語ウィンドウの背景色。隣の見本（Border.Background）が同じ値を見ているため、
    /// 素の自動プロパティにすると打っている最中に見本が追随しない。
    /// </summary>
    public string StreamBackground
    {
        get => _streamBackground;
        set => Set(ref _streamBackground, value);
    }

    public double StreamFontScale { get; set; }
    public bool StreamTopmost { get; set; }
    public bool StreamShowTranslations { get; set; }
    public bool StreamShowContents { get; set; }

    public bool BrowserVisible { get; set; }
    public string BrowserStartUrl { get; set; } = "";

    public ICommand ApplyCommand { get; }
    public ICommand PickFontCommand { get; }
    public ICommand ResetReciprocalCommand { get; }

    private void PickFont()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "イジェール文字フォント（Heksa）を選択",
            Filter = "フォント (*.ttf;*.otf)|*.ttf;*.otf|すべてのファイル (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) HeksaFontPath = dlg.FileName;
    }

    private void ApplyAll()
    {
        _settings.SortOrder = string.IsNullOrWhiteSpace(SortOrder) ? TextProcessor.DefaultSortOrder : SortOrder;
        _settings.FontScale = Math.Clamp(FontScale, 0.6, 3.0);
        _settings.AutoSave = AutoSave;
        _settings.HeksaEnabled = HeksaEnabled;
        _settings.HeksaFontPath = string.IsNullOrWhiteSpace(HeksaFontPath) ? null : HeksaFontPath;

        var map = new Dictionary<string, string>();
        foreach (var line in ReciprocalText.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            var i = t.IndexOf('=');
            if (i <= 0) continue;
            map[t[..i].Trim()] = t[(i + 1)..].Trim();
        }
        if (map.Count > 0) _settings.ReciprocalMap = map;

        _settings.StreamBackground = StreamBackground;
        _settings.StreamFontScale = Math.Clamp(StreamFontScale, 1.0, 6.0);
        _settings.StreamWindowTopmost = StreamTopmost;
        _settings.StreamShowTranslations = StreamShowTranslations;
        _settings.StreamShowContents = StreamShowContents;

        _settings.BrowserVisible = BrowserVisible;
        _settings.BrowserStartUrl = string.IsNullOrWhiteSpace(BrowserStartUrl) ? "" : BrowserStartUrl.Trim();

        if (_doc is not null)
        {
            _doc.ZpdicOnline["punctuations"] = new JsonArray(
                Punctuations.Select(c => (JsonNode)JsonValue.Create(c.ToString())!).ToArray());
            _doc.ZpdicOnline["ignoredPattern"] = IgnoredPattern;
        }

        _settings.Save();
        _apply();
        RequestClose?.Invoke();
    }
}

/// <summary>統計タブの品詞・タグ内訳をグリッド表示するための 1 セル分データ。</summary>
public sealed record BreakdownItem(string Name, int Count);

public sealed class InfoViewModel : OverlayViewModel
{
    public InfoViewModel(OtmDocument? doc, string legendMarkdown, IReadOnlyList<string[]> changelogRows,
                         string changelogPath, ICommand exportChangelogCommand, ICommand relinkChangelogCommand)
    {
        Title = "凡例・統計・更新履歴";
        LegendMarkdown = legendMarkdown;
        // CSV 先頭の見出し行は、本文と一緒にスクロールせず固定表示するヘッダー側に回す。
        var (header, body) = SplitChangelogHeader(changelogRows);
        ChangelogHeader = header;
        ChangelogRows = new ObservableCollection<string[]>(body);
        ChangelogPath = changelogPath;
        ExportChangelogCommand = exportChangelogCommand;
        RelinkChangelogCommand = relinkChangelogCommand;

        if (doc is null) return;
        WordCount = doc.Words.Count;
        var forms = doc.Words.Select(w => w.Form).ToList();
        HomonymCount = forms.GroupBy(f => f, StringComparer.Ordinal).Count(g => g.Count() > 1);
        TranslationCount = doc.Words.Sum(w => w.Translations.Sum(t => t.Forms.Count));
        RelationCount = doc.Words.Sum(w => w.Relations.Count);
        AverageFormLength = forms.Count == 0 ? 0 : Math.Round(forms.Average(f => f.Length), 2);
        TagItems = doc.Words.SelectMany(w => w.Tags)
            .GroupBy(t => t, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new BreakdownItem(g.Key, g.Count()))
            .ToList();
        PosItems = doc.Words.SelectMany(w => w.Translations.Select(t => t.Title))
            .Where(t => !string.IsNullOrEmpty(t))
            .GroupBy(t => t, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new BreakdownItem(g.Key, g.Count()))
            .ToList();
    }

    /// <summary>凡例の Markdown ソース（legend が文字列でない場合は整形済み JSON のフォールバック）。</summary>
    public string LegendMarkdown { get; }
    /// <summary>更新履歴の見出し行。タブ側ではスクロール領域の外に固定して表示する（常に 4 列）。</summary>
    public string[] ChangelogHeader { get; }
    /// <summary>更新履歴の本文行（見出し行は含まない）。追記順 = 古い順で並ぶ。</summary>
    public ObservableCollection<string[]> ChangelogRows { get; }
    public string ChangelogPath { get; }
    public ICommand ExportChangelogCommand { get; }
    public ICommand RelinkChangelogCommand { get; }

    public int WordCount { get; }
    public int HomonymCount { get; }
    public int TranslationCount { get; }
    public int RelationCount { get; }
    public double AverageFormLength { get; }
    /// <summary>タグ内訳（件数の多い順）。WrapPanel + SharedSizeGroup でグリッド状に整列表示する。</summary>
    public IReadOnlyList<BreakdownItem> TagItems { get; } = Array.Empty<BreakdownItem>();
    /// <summary>品詞（訳語タイトル）内訳（件数の多い順）。</summary>
    public IReadOnlyList<BreakdownItem> PosItems { get; } = Array.Empty<BreakdownItem>();

    /// <summary>
    /// 読み出した CSV を「見出し行」と「本文行」に分離する。見出し行が無い CSV では既定の列名をそのまま使う。
    /// 手作業で作った CSV で列が足りないときもヘッダーのバインディングが壊れないよう、常に 4 列へそろえる。
    /// </summary>
    private static (string[] Header, IEnumerable<string[]> Rows) SplitChangelogHeader(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0 || !ChangelogService.IsHeaderRow(rows[0]))
            return ((string[])ChangelogService.DefaultHeader.Clone(), rows);

        var header = new string[ChangelogService.DefaultHeader.Length];
        for (var i = 0; i < header.Length; i++) header[i] = i < rows[0].Length ? rows[0][i] : "";
        return (header, rows.Skip(1));
    }
}
