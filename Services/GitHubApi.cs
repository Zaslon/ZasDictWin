using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZasDictWin.Resources;

namespace ZasDictWin.Services;

/// <summary>ファイル 1 つの取得結果。<see cref="Ok"/> が偽なら <see cref="Message"/> をそのまま画面に出す。</summary>
public sealed record GitHubFileResult(bool Ok, string Message, string Content = "")
{
    /// <summary>指定パスがリポジトリに無い（404）。更新履歴 CSV はこれが「まだ履歴が無い」の意味を兼ねる。</summary>
    public bool NotFound { get; init; }

    /// <summary>トークンが原因の失敗（401）。呼び出し側は保存済みのトークンを捨てて入力し直させる。</summary>
    public bool AuthFailed { get; init; }
}

/// <summary>コミット 1 回の結果。</summary>
public sealed record GitHubCommitResult(bool Ok, string Message)
{
    /// <summary>コミットの間にブランチが先へ進んでいた（fast-forward 失敗）。読み込み直してからコミットし直す必要がある。</summary>
    public bool Conflict { get; init; }

    public bool AuthFailed { get; init; }
}

/// <summary>コミットに含める 1 ファイル分の変更。</summary>
public sealed record GitHubFileChange(string Path, string Content);

/// <summary>
/// GitHub と辞書ファイルをやり取りする。読み取りは Contents API（ファイル 1 つを Base64 で取得。
/// 1MB を超えるファイルは encoding が base64 でなくなるので、その場合だけ download_url から生で取る）。
/// 書き込みは Git Data API（blob を積まず、複数ファイルをまとめて 1 本のツリー・1 回のコミットにする）。
/// 辞書 JSON と更新履歴 CSV を毎回まとめてコミットするのはこれがあるため。ZasDictAndroid の
/// GitHubApiClient.commitFiles と同じ組み立て方（ref → commit → tree → commit → ref 更新）を踏襲している。
/// </summary>
public static class GitHubApi
{
    private const string ApiBase = "https://api.github.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin", "github_token");

    public static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var token = File.ReadAllText(TokenPath).Trim();
            return token.Length == 0 ? null : token;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write($"GitHubトークンの読み込み ({TokenPath})", ex);
            return null;
        }
    }

    public static void SaveToken(string token)
    {
        var dir = Path.GetDirectoryName(TokenPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(TokenPath, token.Trim());
    }

    public static void DeleteToken()
    {
        try { File.Delete(TokenPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 消せなくても次の通信で弾かれるだけ */ }
    }

    /// <summary>ファイル 1 つの内容を取得する（Contents API）。</summary>
    public static async Task<GitHubFileResult> GetFileAsync(string owner, string repo, string path, string branch, string token)
    {
        if (!token.All(char.IsAscii))
            return new GitHubFileResult(false, Strings.GitHub_TokenInvalidChars) { AuthFailed = true };

        var url = $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(branch)}";
        try
        {
            using var request = NewRequest(HttpMethod.Get, url, token);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new GitHubFileResult(false, string.Format(Strings.GitHub_FileNotFound, path, branch)) { NotFound = true };
            if (response.StatusCode != HttpStatusCode.OK)
                return Failure<GitHubFileResult>(response.StatusCode, (msg, auth) => new GitHubFileResult(false, msg) { AuthFailed = auth });

            var node = JsonNode.Parse(body) as JsonObject;
            var encoding = node?["encoding"]?.GetValue<string>() ?? "base64";

            // 1MB を超えるファイルは Contents API が content を返さず encoding だけ変わる。
            // その場合は download_url から生のテキストを直接取りに行く（ZasDictAndroid と同じフォールバック）。
            if (encoding != "base64")
            {
                var downloadUrl = node?["download_url"]?.GetValue<string>();
                if (string.IsNullOrEmpty(downloadUrl))
                    return new GitHubFileResult(false, string.Format(Strings.GitHub_FileTooLarge, encoding));

                using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                dlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var dlResponse = await Http.SendAsync(dlRequest).ConfigureAwait(false);
                if (!dlResponse.IsSuccessStatusCode)
                    return new GitHubFileResult(false, string.Format(Strings.GitHub_DownloadFailed, (int)dlResponse.StatusCode));
                var text = await dlResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new GitHubFileResult(true, Strings.GitHub_FetchOk, text);
            }

            var content = node?["content"]?.GetValue<string>() ?? "";
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
            return new GitHubFileResult(true, Strings.GitHub_FetchOk, decoded);
        }
        catch (TaskCanceledException)
        {
            return new GitHubFileResult(false, Strings.Network_Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        {
            return new GitHubFileResult(false, string.Format(Strings.GitHub_FetchFailed, ex.Message));
        }
    }

    /// <summary>
    /// 複数ファイルへの変更を 1 回のコミットにまとめる（Git Data API）。辞書 JSON と更新履歴 CSV をまとめるためのもの。
    /// </summary>
    public static async Task<GitHubCommitResult> CommitFilesAsync(
        string owner, string repo, string branch, string token,
        IReadOnlyList<GitHubFileChange> files, string message)
    {
        if (!token.All(char.IsAscii))
            return new GitHubCommitResult(false, Strings.GitHub_TokenInvalidChars) { AuthFailed = true };
        if (files.Count == 0)
            return new GitHubCommitResult(false, Strings.GitHub_NoChangesToCommit);

        var repoBase = $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";
        try
        {
            // 1. ブランチ先端のコミット sha
            //    参照の取得は /git/ref/（単数）、更新は /git/refs/（複数）と別のルートになっている。
            //    URL を使い回すと PATCH だけルート無しの 404 になり、読み込みだけ通る状態になる。
            var branchRef = $"heads/{EscapePath(branch)}";
            var getRefUrl = $"{repoBase}/git/ref/{branchRef}";
            var updateRefUrl = $"{repoBase}/git/refs/{branchRef}";
            var (refOk, refBody, refStatus) = await SendAsync(HttpMethod.Get, getRefUrl, token, null).ConfigureAwait(false);
            if (!refOk) return Failure<GitHubCommitResult>(refStatus, (msg, auth) => new GitHubCommitResult(false, string.Format(Strings.GitHub_BranchFetchFailed, msg)) { AuthFailed = auth });
            var currentCommitSha = (JsonNode.Parse(refBody) as JsonObject)?["object"]?["sha"]?.GetValue<string>();
            if (currentCommitSha is null) return new GitHubCommitResult(false, Strings.GitHub_BranchParseFailed);

            // 2. そのコミットが指すベースツリーの sha
            var commitUrl = $"{repoBase}/git/commits/{currentCommitSha}";
            var (commitOk, commitBody, commitStatus) = await SendAsync(HttpMethod.Get, commitUrl, token, null).ConfigureAwait(false);
            if (!commitOk) return Failure<GitHubCommitResult>(commitStatus, (msg, auth) => new GitHubCommitResult(false, string.Format(Strings.GitHub_CommitInfoFetchFailed, msg)) { AuthFailed = auth });
            var baseTreeSha = (JsonNode.Parse(commitBody) as JsonObject)?["tree"]?["sha"]?.GetValue<string>();
            if (baseTreeSha is null) return new GitHubCommitResult(false, Strings.GitHub_CommitInfoParseFailed);

            // 3. 変更したファイルだけを乗せた新しいツリー（他のファイルはベースツリーからそのまま引き継がれる）
            var treeEntries = new JsonArray();
            foreach (var file in files)
            {
                treeEntries.Add(new JsonObject
                {
                    ["path"] = file.Path,
                    ["mode"] = "100644",
                    ["type"] = "blob",
                    ["content"] = file.Content,
                });
            }
            var treePayload = new JsonObject { ["base_tree"] = baseTreeSha, ["tree"] = treeEntries };
            var (treeOk, treeBody, treeStatus) = await SendAsync(HttpMethod.Post, $"{repoBase}/git/trees", token, treePayload).ConfigureAwait(false);
            if (!treeOk) return Failure<GitHubCommitResult>(treeStatus, (msg, auth) => new GitHubCommitResult(false, string.Format(Strings.GitHub_TreeCreateFailed, msg)) { AuthFailed = auth });
            var newTreeSha = (JsonNode.Parse(treeBody) as JsonObject)?["sha"]?.GetValue<string>();
            if (newTreeSha is null) return new GitHubCommitResult(false, Strings.GitHub_TreeParseFailed);

            // 4. 新しいコミット
            var commitPayload = new JsonObject
            {
                ["message"] = message,
                ["tree"] = newTreeSha,
                ["parents"] = new JsonArray(currentCommitSha),
            };
            var (newCommitOk, newCommitBody, newCommitStatus) = await SendAsync(HttpMethod.Post, $"{repoBase}/git/commits", token, commitPayload).ConfigureAwait(false);
            if (!newCommitOk) return Failure<GitHubCommitResult>(newCommitStatus, (msg, auth) => new GitHubCommitResult(false, string.Format(Strings.GitHub_CommitCreateFailed, msg)) { AuthFailed = auth });
            var newCommitSha = (JsonNode.Parse(newCommitBody) as JsonObject)?["sha"]?.GetValue<string>();
            if (newCommitSha is null) return new GitHubCommitResult(false, Strings.GitHub_CommitParseFailed);

            // 5. ブランチを新しいコミットへ進める。force を付けないので、他所が先に進めていたら
            //    fast-forward にならず失敗する（＝安全に弾かれる）。
            var updateRefPayload = new JsonObject { ["sha"] = newCommitSha };
            var (updateOk, updateBody, updateStatus) = await SendAsync(HttpMethod.Patch, updateRefUrl, token, updateRefPayload).ConfigureAwait(false);
            if (!updateOk)
            {
                if (updateStatus is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
                    return new GitHubCommitResult(false, Strings.GitHub_RemoteUpdatedConflict) { Conflict = true };
                return Failure<GitHubCommitResult>(updateStatus, (msg, auth) => new GitHubCommitResult(false, string.Format(Strings.GitHub_BranchUpdateFailed, msg)) { AuthFailed = auth });
            }

            return new GitHubCommitResult(true, Strings.GitHub_CommitOk);
        }
        catch (TaskCanceledException)
        {
            return new GitHubCommitResult(false, Strings.Network_Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new GitHubCommitResult(false, string.Format(Strings.GitHub_CommitFailedStatus, ex.Message));
        }
    }

    private static async Task<(bool Ok, string Body, HttpStatusCode Status)> SendAsync(
        HttpMethod method, string url, string token, JsonNode? jsonBody)
    {
        using var request = NewRequest(method, url, token);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (response.IsSuccessStatusCode, body, response.StatusCode);
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ZasDictWin", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    /// <summary>401 / 403 などをメッセージへ落とす。401 だけトークン起因として呼び出し側に伝える。</summary>
    private static T Failure<T>(HttpStatusCode status, Func<string, bool, T> make) => status switch
    {
        HttpStatusCode.Unauthorized => make(Strings.GitHub_Http401, true),
        HttpStatusCode.Forbidden => make(Strings.GitHub_Http403, false),
        _ => make(string.Format(Strings.GitHub_HttpGeneric, (int)status), false)
    };

    /// <summary>Contents API の path はスラッシュ区切りのまま、各セグメントだけ escape する。</summary>
    private static string EscapePath(string path)
        => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
