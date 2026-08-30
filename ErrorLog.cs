using System.IO;

namespace ZasDictWin.Services;

/// <summary>
/// アプリ全体の障害ログ。設定と同じ %APPDATA%\ZasDictWin に error.log として追記します。
/// 報告用にテキストファイルを添付してもらえるよう、UI 側に出すメッセージにもパスを載せます。
/// </summary>
public static class ErrorLog
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin", "error.log");

    public static void Write(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}" +
                $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}" +
                $"{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logEx)
        {
            // ログが書けない状況でさらに落とすわけにはいかない。情報は落ちるが処理は続ける。
            System.Diagnostics.Trace.WriteLine($"[ErrorLog] 書き込み失敗: {logEx.Message}");
        }
    }
}
