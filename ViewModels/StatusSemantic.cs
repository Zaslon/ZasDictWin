namespace ZasDictWin.ViewModels;

/// <summary>
/// ステータスバーに出す通知の種類。ZasDesignSystem rules.md「メッセージを SSIP コードで分類する」の Semantic に当たる。
/// Warning と Error だけが強調（光らせる）の対象で、Info と Success は強調しない
/// （implementation/common.md「ステータスバー」：Interruption 0・1 は強調しない）。
/// </summary>
public enum StatusSemantic
{
    Info,
    Success,
    Warning,
    Error,
}
