namespace ZasDictWin.Mediator;

/// <summary>
/// 画面（タブ）1 種類ぶんの決まり。どこへ出すか・閉じられるか・押し直したら前に出すか・
/// 開いている間に辞書の差し替えを止めるかを 1 つの表にまとめる。FloatSize は描画パラメータなので各 ViewModel に残す。
/// </summary>
public sealed record ScreenRule(
    string Kind,
    bool IsDockable,            // 偽なら中央モーダル（確認ダイアログ）か独立ウィンドウ
    bool PrefersFloating,       // 行き先を覚えていないときは独立ウィンドウ
    bool IsPinned,              // 閉じられない（検索・単語詳細）
    bool IsReopenable,          // すでに開いていれば前に出す（ツール類・設定）
    bool BlocksDictionarySwap); // 開いている間は辞書の差し替えを止める（単語／例文エディタ）

public static class ScreenRegistry
{
    private static readonly ScreenRule[] Rules =
    {
        new("SearchViewModel", true, false, true, false, false),
        new("WordDetailViewModel", true, false, true, false, false),
        new("BrowserTabViewModel", true, false, false, false, false),
        new("WordEditViewModel", true, false, false, false, true),
        new("ExampleEditViewModel", true, false, false, false, true),
        new("ExamplesViewModel", true, false, false, false, false),
        new("DialectToolViewModel", true, true, false, true, false),
        new("IpaToolViewModel", true, true, false, true, false),
        new("StatsViewModel", true, true, false, true, false),
        new("LegendViewModel", true, true, false, true, false),
        new("ChangelogViewModel", true, true, false, true, false),
        new("SettingsViewModel", false, false, false, true, false),
        new("ChoiceViewModel", false, false, false, false, false),
        new("CommitViewModel", false, false, false, false, false),
    };

    private static readonly Dictionary<string, ScreenRule> ByKind = Rules.ToDictionary(r => r.Kind);

    /// <summary>未知の種類でも例外にせず既定（タブとして並べるだけ）を返す。画面を足したときに落ちないように。</summary>
    public static ScreenRule For(string? kind)
        => kind is not null && ByKind.TryGetValue(kind, out var rule)
            ? rule
            : new ScreenRule(kind ?? "", true, false, false, false, false);

    public static IReadOnlyList<ScreenRule> All => Rules;
}
