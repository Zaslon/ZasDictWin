using System.IO;

namespace ZasDictWin.Services;

/// <summary>設定・選択肢・秘密情報・障害ログ・作業コピーを置く %APPDATA%\ZasDictWin。</summary>
public static class AppDataDir
{
    // テストは利用者の実ファイル（settings.json・choices.json・トークン）に触れないよう、
    // 何かを読み書きする前に一時フォルダへ差し替える。アプリ側からは書き換えない。
    public static string Root { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin");

    public static string Combine(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());
}
