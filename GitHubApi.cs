using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            return new GitHubFileResult(false, "トークンに使用できない文字が含まれています。入力し直してください。") { AuthFailed = true };

        var url = $"{ApiBase}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(branch)}";
        try
        {
            using var request = NewRequest(HttpMethod.Get, url, token);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new GitHubFileResult(false, $"{path}（{branch}）が見つかりません。") { NotFound = true };
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
                    return new GitHubFileResult(false, $"ファイルが大きすぎて取得できません（encoding: {encoding}）。");

                using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                dlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var dlResponse = await Http.SendAsync(dlRequest).ConfigureAwait(false);
                if (!dlResponse.IsSuccessStatusCode)
                    return new GitHubFileResult(false, $"ダウンロードに失敗しました（HTTP {(int)dlResponse.StatusCode}）。");
                var text = await dlResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new GitHubFileResult(true, "取得成功", text);
            }

            var content = node?["content"]?.GetValue<string>() ?? "";
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Replace("\n", "")));
            return new GitHubFileResult(true, "取得成功", decoded);
        }
        catch (TaskCanceledException)
        {
            return new GitHubFileResult(false, "通信がタイムアウトしました。通信状態を確かめてください。");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        {
            return new GitHubFileResult(false, $"取得に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 複数ファイルへの変更を 1 回のコミットにまとめる（Git Data API）。Contents API と違い個々の
    /// sha は不要で、コミット直前のブランチ先端をそのつど基点にする。辞書 JSON と更新履歴 CSV を
    /// 同時に変更しても、途中経過が別コミットに割れることはない。
    /// </summary>
    public static async Task<GitHubCommitResult> CommitFilesAsync(
        string owner, string repo, string branch, string token,
        IReadOnlyList<GitHubFileChange> files, string message)
    {
        if (!token.All(char.IsAscii))
            return new GitHubCommitResult(false, "トークンに使用できない文字が含まれています。入力し直してください。") { AuthFailed = true };
        if (files.Count == 0)
            return new GitHubCommitResult(false, "コミットする変更がありません。");

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
            if (!refOk) return Failure<GitHubCommitResult>(refStatus, (msg, auth) => new GitHubCommitResult(false, $"ブランチの取得に失敗しました: {msg}") { AuthFailed = auth });
            var currentCommitSha = (JsonNode.Parse(refBody) as JsonObject)?["object"]?["sha"]?.GetValue<string>();
            if (currentCommitSha is null) return new GitHubCommitResult(false, "ブランチ情報を解釈できませんでした。");

            // 2. そのコミットが指すベースツリーの sha
            var commitUrl = $"{repoBase}/git/commits/{currentCommitSha}";
            var (commitOk, commitBody, commitStatus) = await SendAsync(HttpMethod.Get, commitUrl, token, null).ConfigureAwait(false);
            if (!commitOk) return Failure<GitHubCommitResult>(commitStatus, (msg, auth) => new GitHubCommitResult(false, $"コミット情報の取得に失敗しました: {msg}") { AuthFailed = auth });
            var baseTreeSha = (JsonNode.Parse(commitBody) as JsonObject)?["tree"]?["sha"]?.GetValue<string>();
            if (baseTreeSha is null) return new GitHubCommitResult(false, "コミット情報を解釈できませんでした。");

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
            if (!treeOk) return Failure<GitHubCommitResult>(treeStatus, (msg, auth) => new GitHubCommitResult(false, $"ツリーの作成に失敗しました: {msg}") { AuthFailed = auth });
            var newTreeSha = (JsonNode.Parse(treeBody) as JsonObject)?["sha"]?.GetValue<string>();
            if (newTreeSha is null) return new GitHubCommitResult(false, "ツリーの応答を解釈できませんでした。");

            // 4. 新しいコミット
            var commitPayload = new JsonObject
            {
                ["message"] = message,
                ["tree"] = newTreeSha,
                ["parents"] = new JsonArray(currentCommitSha),
            };
            var (newCommitOk, newCommitBody, newCommitStatus) = await SendAsync(HttpMethod.Post, $"{repoBase}/git/commits", token, commitPayload).ConfigureAwait(false);
            if (!newCommitOk) return Failure<GitHubCommitResult>(newCommitStatus, (msg, auth) => new GitHubCommitResult(false, $"コミットの作成に失敗しました: {msg}") { AuthFailed = auth });
            var newCommitSha = (JsonNode.Parse(newCommitBody) as JsonObject)?["sha"]?.GetValue<string>();
            if (newCommitSha is null) return new GitHubCommitResult(false, "コミットの応答を解釈できませんでした。");

            // 5. ブランチを新しいコミットへ進める。force を付けないので、他所が先に進めていたら
            //    fast-forward にならず失敗する（＝安全に弾かれる）。
            var updateRefPayload = new JsonObject { ["sha"] = newCommitSha };
            var (updateOk, updateBody, updateStatus) = await SendAsync(HttpMethod.Patch, updateRefUrl, token, updateRefPayload).ConfigureAwait(false);
            if (!updateOk)
            {
                if (updateStatus is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
                    return new GitHubCommitResult(false, "リモートが更新されています。GitHubから読み込み直してからコミットしてください。") { Conflict = true };
                return Failure<GitHubCommitResult>(updateStatus, (msg, auth) => new GitHubCommitResult(false, $"ブランチの更新に失敗しました: {msg}") { AuthFailed = auth });
            }

            return new GitHubCommitResult(true, "コミット成功");
        }
        catch (TaskCanceledException)
        {
            return new GitHubCommitResult(false, "通信がタイムアウトしました。通信状態を確かめてください。");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new GitHubCommitResult(false, $"コミットに失敗しました: {ex.Message}");
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
        HttpStatusCode.Unauthorized => make("HTTP 401: トークンが正しくありません。入力し直してください。", true),
        HttpStatusCode.Forbidden => make("HTTP 403: 権限が不足しているか、呼び出し回数の上限に達しています。", false),
        _ => make($"HTTP {(int)status}: 通信に失敗しました。", false)
    };

    /// <summary>Contents API の path はスラッシュ区切りのまま、各セグメントだけ escape する。</summary>
    private static string EscapePath(string path)
        => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
