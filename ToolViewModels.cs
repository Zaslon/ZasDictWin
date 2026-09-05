using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// 「ツール」メニューから開く画面の ViewModel 群。どれも他のタブと同じオーバーレイだが、
/// 常設の枠を割きたくないので、行き先を覚えていないうちは独立ウィンドウとして出す
/// （<see cref="OverlayViewModel.PrefersFloating"/>）。掴んで本体の枠へ運べばタブになる。
/// <see cref="OverlayViewModel.FloatSize"/> は独立ウィンドウで出すときの大きさ。
/// </summary>
public sealed class DialectToolViewModel : OverlayViewModel
{
    private string _input = "";
    private DialectResult _dialects = new("", "", "", "");
    private bool _faithfulTitauini = Dialects.FaithfulTitauini;

    public DialectToolViewModel(string? initialInput = null)
    {
        Title = "変換";
        _input = initialInput ?? "";
        Convert();
    }

    public override bool PrefersFloating => true;

    public override Size FloatSize => new(560, 560);

    /// <summary>強勢のある母音を大文字で入力する。</summary>
    public string Input
    {
        get => _input;
        set { if (Set(ref _input, value)) Convert(); }
    }

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

public sealed class IpaToolViewModel : OverlayViewModel
{
    private string _ipaInput = "";
    private string _ipaSpelling = "";

    public IpaToolViewModel() => Title = "IPA";

    public override bool PrefersFloating => true;

    public override Size FloatSize => new(480, 360);

    public string IpaInput
    {
        get => _ipaInput;
        set { if (Set(ref _ipaInput, value)) IpaSpelling = Ipa.ToSpelling(value); }
    }

    public string IpaSpelling { get => _ipaSpelling; private set => Set(ref _ipaSpelling, value); }
}

/// <summary>統計・凡例ウィンドウの品詞・タグ内訳をグリッド表示するための 1 セル分データ。</summary>
public sealed record BreakdownItem(string Name, int Count);

public sealed class StatsViewModel : OverlayViewModel
{
    public StatsViewModel(OtmDocument? doc)
    {
        Title = "統計";
        if (doc is null) return;
        WordCount = doc.Words.Count;
        var forms = doc.Words.Select(w => w.Form).ToList();
        HomonymCount = forms.GroupBy(f => f, StringComparer.Ordinal).Count(g => g.Count() > 1);
        ExampleCount = doc.Examples.Count;
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

    public override bool PrefersFloating => true;

    public override Size FloatSize => new(560, 560);

    public int WordCount { get; }
    public int HomonymCount { get; }
    public int ExampleCount { get; }
    public double AverageFormLength { get; }
    /// <summary>タグ内訳（件数の多い順）。WrapPanel + SharedSizeGroup でグリッド状に整列表示する。</summary>
    public IReadOnlyList<BreakdownItem> TagItems { get; } = Array.Empty<BreakdownItem>();
    /// <summary>品詞（訳語タイトル）内訳（件数の多い順）。</summary>
    public IReadOnlyList<BreakdownItem> PosItems { get; } = Array.Empty<BreakdownItem>();
}

public sealed class LegendViewModel : OverlayViewModel
{
    public LegendViewModel(string legendMarkdown)
    {
        Title = "凡例";
        LegendMarkdown = legendMarkdown;
    }

    public override bool PrefersFloating => true;

    public override Size FloatSize => new(640, 680);

    /// <summary>凡例の Markdown ソース（legend が文字列でない場合は整形済み JSON のフォールバック）。</summary>
    public string LegendMarkdown { get; }
}

public sealed class ChangelogViewModel : OverlayViewModel
{
    public ChangelogViewModel(IReadOnlyList<string[]> changelogRows, string changelogPath,
                               ICommand exportChangelogCommand, ICommand relinkChangelogCommand)
    {
        Title = "更新履歴";
        // CSV 先頭の見出し行は、本文と一緒にスクロールせず固定表示するヘッダー側に回す。
        var (header, body) = SplitChangelogHeader(changelogRows);
        ChangelogHeader = header;
        ChangelogRows = new ObservableCollection<string[]>(body);
        ChangelogPath = changelogPath;
        ExportChangelogCommand = exportChangelogCommand;
        RelinkChangelogCommand = relinkChangelogCommand;
    }

    /// <summary>画面を開いたまま単語を編集・追加・削除・複製したときに、中身をその場で引き直す。</summary>
    public void Refresh(IReadOnlyList<string[]> changelogRows)
    {
        var (_, body) = SplitChangelogHeader(changelogRows);
        ChangelogRows.Clear();
        foreach (var row in body) ChangelogRows.Add(row);
    }

    public override bool PrefersFloating => true;

    public override Size FloatSize => new(820, 600);

    /// <summary>更新履歴の見出し行。画面側はスクロール領域の外に固定して表示する（常に 4 列）。</summary>
    public string[] ChangelogHeader { get; }
    /// <summary>更新履歴の本文行（見出し行は含まない）。追記順 = 古い順で並ぶ。</summary>
    public ObservableCollection<string[]> ChangelogRows { get; }
    public string ChangelogPath { get; }
    public ICommand ExportChangelogCommand { get; }
    public ICommand RelinkChangelogCommand { get; }

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
