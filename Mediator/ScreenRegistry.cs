namespace ZasDictWin.Mediator;

/// <summary>
/// 画面（タブ）1 種類ぶんの決まり。どこへ出すか・閉じられるか・
/// 開いている間に辞書の差し替えを止めるかを 1 つの表にまとめる。FloatSize は描画パラメータなので各 ViewModel に残す。
/// </summary>
public sealed record ScreenRule(
    string Kind,
    bool PrefersFloating,       // 行き先を覚えていないときは独立ウィンドウ
    bool IsPinned,              // 閉じられない（検索・単語詳細）
    bool BlocksDictionarySwap); // 開いている間は辞書の差し替えを止める（単語／例文エディタ）

public static class ScreenRegistry
{
    // 確認ダイアログ・設定・コミットのように規則が既定どおりの画面も載せておく。
    // 載っていない画面は For が黙って既定を返すため、登録漏れはテストで全画面と突き合わせて見つける。
    private static readonly ScreenRule[] Rules =
    {
        new("SearchViewModel", false, true, false),
        new("WordDetailViewModel", false, true, false),
        new("BrowserTabViewModel", false, false, false),
        new("WordEditViewModel", false, false, true),
        new("ExampleEditViewModel", false, false, true),
        new("ExamplesViewModel", false, false, false),
        new("DialectToolViewModel", true, false, false),
        new("IpaToolViewModel", true, false, false),
        new("StatsViewModel", true, false, false),
        new("LegendViewModel", true, false, false),
        new("ChangelogViewModel", true, false, false),
        new("SettingsViewModel", false, false, false),
        new("ChoiceViewModel", false, false, false),
        new("CommitViewModel", false, false, false),
    };

    private static readonly Dictionary<string, ScreenRule> ByKind = Rules.ToDictionary(r => r.Kind);

    /// <summary>未知の種類でも例外にせず既定（タブとして並べるだけ）を返す。画面を足したときに落ちないように。</summary>
    public static ScreenRule For(string? kind)
        => kind is not null && ByKind.TryGetValue(kind, out var rule)
            ? rule
            : new ScreenRule(kind ?? "", false, false, false);

    public static IReadOnlyList<ScreenRule> All => Rules;
}
