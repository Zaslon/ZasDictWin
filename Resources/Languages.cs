namespace ZasDictWin.Resources;

/// <summary>選択肢に出す 1 言語。Name は常にその言語自身での表記（"日本語" / "English" 等）にする。
/// 表示は Strings.EnFont に固定されるので、[en] タグは書かない（プルダウンは表示中の言語に関わらず
/// どの言語名も読める必要があり、タグでは自分以外の言語名を救えないため）。</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// 対応言語の一覧。設定画面のプルダウン（SettingsViewModel.AvailableLanguages）と
/// 起動時の切り替え（App.ApplyLanguage → Strings.SetLanguage）の両方がここを参照する。
///
/// 対応言語を追加する手順:
///   1. Resources/Strings.template.resx をコピーして編集する。書き方はtemplateファイル参照。
///   2. 下の All に 1 行追加する。Code は手順 1 のファイル名（拡張子と "Strings." を除いた部分）と
///      一致させること（例: Strings.fr.resx なら "fr"）。
/// </summary>
public static class Languages
{
    public static readonly IReadOnlyList<LanguageOption> All = new[]
    {
        new LanguageOption("ja", "日本語"),
        new LanguageOption("en", "English"),
        new LanguageOption("idz", "Idyerin"),
        // 新しい言語はこの下に追加する（上の手順 1・3 を忘れずに）。
    };

    /// <summary>settings.json にまだ Language が無い（i18n 対応前からの）利用者に使う既定値。
    /// AppSettings.Language の既定値と一致させること。</summary>
    public const string Default = "ja";

    /// <summary>settings.json 由来の値を検証する。未知のコードが来た場合も、デフォルト言語で起動する。</summary>
    public static string Normalize(string? code)
        => All.Any(l => l.Code == code) ? code! : Default;
}
