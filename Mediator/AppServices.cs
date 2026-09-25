using System.IO;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

public readonly record struct DocumentResult(bool Ok, OtmDocument? Document, string Message);
public readonly record struct SaveResult(bool Ok, string Message);

/// <summary>ToString() を上書きして Token を隠す（error.log に混ざるのを防ぐ）。</summary>
public readonly record struct GitHubTarget(
    string Owner, string Repo, string Branch, string JsonPath, string ChangelogPath, string Token)
{
    // 自動生成の ToString は全メンバーを並べるため、例外メッセージや記録に紛れ込むとトークンが漏れる。
    public override string ToString()
        => $"GitHubTarget {{ Owner = {Owner}, Repo = {Repo}, Branch = {Branch}, JsonPath = {JsonPath}, ChangelogPath = {ChangelogPath} }}";
}

/// <summary>
/// Mediator から見た外界。IO・OS ダイアログ・時計をここでまとめ、Mediator 自身は
/// 純粋な遷移だけになるようにする（テストではこれを差し替える）。Services/ の実装は変えない。
/// </summary>
public interface IAppServices
{
    AppSettings Settings { get; }
    DateTime Now { get; }

    DocumentResult Load(string path);
    SaveResult Save(OtmDocument doc, string path);
    OtmDocument CreateEmpty();

    /// <summary>settings.json への書き戻し。</summary>
    void SaveSettings();
    /// <summary>関係の対照表（choices.json）の書き戻し。</summary>
    void SaveRelations(Dictionary<string, string> relations);

    /// <summary>Heksa フォントの読み込み。読めなければ null。</summary>
    FontFamily? LoadHeksaFont(string? path);

    /// <summary>OS のファイルダイアログ。別 HWND だが README で例外として許容されている手段。
    /// 取りやめ（キャンセル）は null。OS が描くので [en] タグは EnTag.Strip で落とす。</summary>
    string? PickOpenPath(string title, string filter);
    string? PickSavePath(string title, string filter, string suggestedFileName);

    bool TryGetGitHubTarget(out GitHubTarget target, out string error);
    /// <summary>GitHub モードで読み書きするローカルのファイル。</summary>
    string GitHubWorkingCopyPath(GitHubTarget target, string? docPath);
    Task<GitHubFileResult> GetGitHubFileAsync(GitHubTarget target, string path);
    Task<GitHubCommitResult> CommitGitHubAsync(
        GitHubTarget target, IReadOnlyList<GitHubFileChange> files, string message);
    void DeleteGitHubToken();
    void SaveGitHubToken(string token);
    bool HasGitHubToken { get; }

    Task<ExampleOfferResult> FetchExampleOfferAsync(string catalog, int number);
    void SaveZpdicApiKey(string key);
    void DeleteZpdicApiKey();
    bool HasZpdicApiKey { get; }

    void AppendChangelog(string csv, IReadOnlyList<ChangeEntry> entries);
    IReadOnlyList<string[]> ReadChangelog(string csv);
    void CopyFile(string from, string to);
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string text, bool withBom);

    void LogError(string where, Exception ex);
}

/// <summary>既存の Services/ をそのまま呼ぶ実装。判断は持たず、例外を結果型へ直すところまでを受け持つ。</summary>
public sealed class AppServices : IAppServices
{
    private readonly Func<string?> _loadToken;
    private readonly Action<string, Exception> _log;

    public AppServices(AppSettings settings, Func<string?>? loadToken = null, Action<string, Exception>? log = null)
    {
        Settings = settings;
        _loadToken = loadToken ?? GitHubApi.LoadToken;
        _log = log ?? ErrorLog.Write;
    }

    public AppSettings Settings { get; }

    public DateTime Now => DateTime.Now;

    public DocumentResult Load(string path)
    {
        try
        {
            return new DocumentResult(true, OtmJsonIo.Load(path), "");
        }
        catch (Exception ex)
        {
            _log($"辞書の読み込み ({path})", ex);
            return new DocumentResult(false, null, ex.Message);
        }
    }

    public SaveResult Save(OtmDocument doc, string path)
    {
        try
        {
            OtmJsonIo.Save(doc, path);
            return new SaveResult(true, "");
        }
        catch (Exception ex)
        {
            _log($"辞書の保存 ({path})", ex);
            return new SaveResult(false, ex.Message);
        }
    }

    public OtmDocument CreateEmpty() => OtmJsonIo.CreateEmpty();

    public void SaveSettings() => Settings.Save();

    public void SaveRelations(Dictionary<string, string> relations)
    {
        Choices.Current.Relations = relations;
        Choices.Current.Save();
    }

    public FontFamily? LoadHeksaFont(string? path) => HeadwordFontState.Load(path);

    public string? PickOpenPath(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = EnTag.Strip(title), Filter = EnTag.Strip(filter) };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickSavePath(string title, string filter, string suggestedFileName)
    {
        var dlg = new SaveFileDialog { Title = EnTag.Strip(title), Filter = EnTag.Strip(filter), FileName = suggestedFileName };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public bool TryGetGitHubTarget(out GitHubTarget target, out string error)
    {
        var owner = Settings.GitHubOwner?.Trim() ?? "";
        var repo = Settings.GitHubRepo?.Trim() ?? "";
        var branch = string.IsNullOrWhiteSpace(Settings.GitHubBranch) ? "main" : Settings.GitHubBranch.Trim();
        var jsonPath = Settings.GitHubJsonPath?.Trim() ?? "";
        var changelogPath = Settings.GitHubChangelogPath?.Trim() ?? "";

        if (owner.Length == 0 || repo.Length == 0 || jsonPath.Length == 0)
        {
            target = default;
            error = Strings.GitHub_ConfigMissingRepo;
            return false;
        }
        // トークンは %APPDATA%\ZasDictWin\github_token にだけ置き、settings.json には書かない。
        if (_loadToken() is not { } token)
        {
            target = default;
            error = Strings.GitHub_ConfigMissingToken;
            return false;
        }

        target = new GitHubTarget(owner, repo, branch, jsonPath, changelogPath, token);
        error = "";
        return true;
    }

    /// <summary>辞書を開いていればそれを上書きする（手元のファイルをそのまま作業コピーとして使えるようにするため）。
    /// 開いていないときだけ %APPDATA% 下の取得先を使い、owner/repo/branch ごとに分けて
    /// 別のリポジトリへ切り替えても前のローカルコピーを踏まないようにする。</summary>
    public string GitHubWorkingCopyPath(GitHubTarget target, string? docPath)
    {
        if (docPath is not null) return docPath;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin", "github",
            $"{SanitizeFileName(target.Owner)}__{SanitizeFileName(target.Repo)}__{SanitizeFileName(target.Branch)}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Path.GetFileName(target.JsonPath));
    }

    private static string SanitizeFileName(string s)
        => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public Task<GitHubFileResult> GetGitHubFileAsync(GitHubTarget target, string path)
        => GitHubApi.GetFileAsync(target.Owner, target.Repo, path, target.Branch, target.Token);

    public Task<GitHubCommitResult> CommitGitHubAsync(GitHubTarget target, IReadOnlyList<GitHubFileChange> files, string message)
        => GitHubApi.CommitFilesAsync(target.Owner, target.Repo, target.Branch, target.Token, files.ToList(), message);

    public void DeleteGitHubToken() => GitHubApi.DeleteToken();

    public void SaveGitHubToken(string token) => GitHubApi.SaveToken(token);

    public bool HasGitHubToken => _loadToken() is not null;

    public async Task<ExampleOfferResult> FetchExampleOfferAsync(string catalog, int number)
    {
        if (ZpdicApi.LoadApiKey() is not { } key)
            return new ExampleOfferResult(false, Strings.Zpdic_KeyMissing);
        return await ZpdicApi.FetchAsync(catalog, number, key);
    }

    public void SaveZpdicApiKey(string key) => ZpdicApi.SaveApiKey(key);

    public void DeleteZpdicApiKey() => ZpdicApi.DeleteApiKey();

    public bool HasZpdicApiKey => ZpdicApi.LoadApiKey() is not null;

    public void AppendChangelog(string csv, IReadOnlyList<ChangeEntry> entries) => ChangelogService.Append(csv, entries);

    public IReadOnlyList<string[]> ReadChangelog(string csv) => ChangelogService.Read(csv);

    public void CopyFile(string from, string to) => File.Copy(from, to, overwrite: true);

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string text, bool withBom) => File.WriteAllText(path, text, new UTF8Encoding(withBom));

    public void LogError(string where, Exception ex) => _log(where, ex);
}
