using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using ZasDictWin.Mediator;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class GitHubApiTests
{
    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static Task<GitHubFileResult> Get(string path = "d.json", string branch = "main", string token = "tok")
        => GitHubApi.GetFileAsync("own er", "repo", path, branch, token);

    // ---- ファイルの取得 ------------------------------------------------------------

    [Fact]
    public async Task GetFile_DecodesBase64AndSendsHeaders()
    {
        // Contents API は 60 文字ごとに改行を挟んだ Base64 を返す。
        var content = Base64("{\"words\":\"辞書\"}").Insert(8, "\\n");
        using var http = FakeHttp.Install(_ => FakeHttp.Json($$"""{ "encoding": "base64", "content": "{{content}}" }"""));

        var result = await Get("dir/日本.json", "feat/x");

        Assert.True(result.Ok);
        Assert.Equal("{\"words\":\"辞書\"}", result.Content);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.github.com/repos/own%20er/repo/contents/dir/%E6%97%A5%E6%9C%AC.json?ref=feat%2Fx", request.Url);
        Assert.Equal("Bearer tok", request.Headers["Authorization"]);
        Assert.Equal("2022-11-28", request.Headers["X-GitHub-Api-Version"]);
        Assert.Contains("ZasDictWin", request.Headers["User-Agent"]);
    }

    [Fact]
    public async Task GetFile_NonAsciiToken_IsRejectedWithoutSending()
    {
        using var http = FakeHttp.Install(_ => throw new InvalidOperationException("must not send"));

        var result = await Get(token: "トークン");

        Assert.True(result.AuthFailed);
        Assert.Equal(Strings.GitHub_TokenInvalidChars, result.Message);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true, false)]
    [InlineData(HttpStatusCode.Unauthorized, false, true)]
    [InlineData(HttpStatusCode.Forbidden, false, false)]
    [InlineData(HttpStatusCode.InternalServerError, false, false)]
    public async Task GetFile_HttpErrors(HttpStatusCode status, bool notFound, bool authFailed)
    {
        using var http = FakeHttp.Install(_ => FakeHttp.Status(status));

        var result = await Get();

        Assert.False(result.Ok);
        Assert.Equal((notFound, authFailed), (result.NotFound, result.AuthFailed));
        var expected = status switch
        {
            HttpStatusCode.NotFound => string.Format(Strings.GitHub_FileNotFound, "d.json", "main"),
            HttpStatusCode.Unauthorized => Strings.GitHub_Http401,
            HttpStatusCode.Forbidden => Strings.GitHub_Http403,
            _ => string.Format(Strings.GitHub_HttpGeneric, 500),
        };
        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public async Task GetFile_LargeFile_FallsBackToDownloadUrl()
    {
        using var http = FakeHttp.Install(r => r.RequestUri!.Host == "raw.example"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("生のテキスト") }
            : FakeHttp.Json("""{ "encoding": "none", "download_url": "https://raw.example/d.json" }"""));

        var result = await Get();

        Assert.Equal("生のテキスト", result.Content);
        Assert.Equal("Bearer tok", http.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task GetFile_LargeFile_PrefersBlobApiOverDownloadUrl()
    {
        using var http = FakeHttp.Install(r => r.RequestUri!.AbsolutePath.Contains("/git/blobs/")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("生のテキスト")) }
            : FakeHttp.Json("""{ "encoding": "none", "sha": "abc123", "download_url": "https://raw.example/d.json" }"""));

        var result = await Get();

        Assert.Equal("生のテキスト", result.Content);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal("https://api.github.com/repos/own%20er/repo/git/blobs/abc123", http.Requests[1].Url);
        Assert.Equal("application/vnd.github.raw+json", http.Requests[1].Headers["Accept"]);
        Assert.Equal("Bearer tok", http.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task GetFile_LargeFileWithoutDownloadUrl_Fails()
    {
        using var http = FakeHttp.Install(_ => FakeHttp.Json("""{ "encoding": "none" }"""));
        var result = await Get();
        Assert.Equal(string.Format(Strings.GitHub_FileTooLarge, "none"), result.Message);
    }

    [Fact]
    public async Task GetFile_DownloadFailure_ReportsStatus()
    {
        using var http = FakeHttp.Install(r => r.RequestUri!.Host == "raw.example"
            ? FakeHttp.Status(HttpStatusCode.BadGateway)
            : FakeHttp.Json("""{ "encoding": "none", "download_url": "https://raw.example/d.json" }"""));

        var result = await Get();

        Assert.Equal(string.Format(Strings.GitHub_DownloadFailed, 502), result.Message);
    }

    [Fact]
    public async Task GetFile_Timeout_AndBrokenResponse_AreResultsNotExceptions()
    {
        using (FakeHttp.Install(_ => throw new TaskCanceledException()))
            Assert.Equal(Strings.Network_Timeout, (await Get()).Message);

        using (FakeHttp.Install(_ => throw new HttpRequestException("dns")))
            Assert.Equal(string.Format(Strings.GitHub_FetchFailed, "dns"), (await Get()).Message);

        using (FakeHttp.Install(_ => FakeHttp.Json("""{ "content": "***" }""")))
            Assert.False((await Get()).Ok);
    }

    // ---- コミット ---------------------------------------------------------------

    private static HttpResponseMessage CommitFlow(HttpRequestMessage r, HttpStatusCode patchStatus = HttpStatusCode.OK)
    {
        var path = r.RequestUri!.AbsolutePath;
        return (r.Method.Method, path) switch
        {
            ("GET", var p) when p.Contains("/git/ref/") => FakeHttp.Json("""{ "object": { "sha": "c1" } }"""),
            ("GET", var p) when p.EndsWith("/git/commits/c1") => FakeHttp.Json("""{ "tree": { "sha": "t1" } }"""),
            ("POST", var p) when p.EndsWith("/git/trees") => FakeHttp.Json("""{ "sha": "t2" }"""),
            ("POST", var p) when p.EndsWith("/git/commits") => FakeHttp.Json("""{ "sha": "c2" }"""),
            ("PATCH", _) => FakeHttp.Json("{}", patchStatus),
            _ => FakeHttp.Status(HttpStatusCode.NotFound),
        };
    }

    private static readonly GitHubFileChange[] TwoFiles =
    {
        new("d.json", "{}"),
        new("log.csv", "timestamp\n"),
    };

    [Fact]
    public async Task Commit_BuildsOneTreeAndOneCommitThenMovesTheBranch()
    {
        using var http = FakeHttp.Install(r => CommitFlow(r));

        var result = await GitHubApi.CommitFilesAsync("o", "r", "feat/x", "tok", TwoFiles, "msg");

        Assert.True(result.Ok);
        Assert.Equal(new[] { "GET", "GET", "POST", "POST", "PATCH" }, http.Requests.Select(q => q.Method.Method));
        // 参照の取得は git/ref（単数）、更新は git/refs（複数）。
        Assert.EndsWith("/repos/o/r/git/ref/heads/feat/x", http.Requests[0].Url);
        Assert.EndsWith("/repos/o/r/git/refs/heads/feat/x", http.Requests[4].Url);

        var tree = JsonNode.Parse(http.Requests[2].Body!)!;
        Assert.Equal("t1", tree["base_tree"]!.GetValue<string>());
        Assert.Equal(new[] { "d.json", "log.csv" }, tree["tree"]!.AsArray().Select(e => e!["path"]!.GetValue<string>()));
        var commit = JsonNode.Parse(http.Requests[3].Body!)!;
        Assert.Equal(("msg", "t2", "c1"), (commit["message"]!.GetValue<string>(), commit["tree"]!.GetValue<string>(), commit["parents"]![0]!.GetValue<string>()));
        Assert.Equal("c2", JsonNode.Parse(http.Requests[4].Body!)!["sha"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Commit_BranchMovedMeanwhile_IsAConflict(HttpStatusCode status)
    {
        using var http = FakeHttp.Install(r => CommitFlow(r, status));

        var result = await GitHubApi.CommitFilesAsync("o", "r", "main", "tok", TwoFiles, "msg");

        Assert.True(result.Conflict);
        Assert.Equal(Strings.GitHub_RemoteUpdatedConflict, result.Message);
    }

    [Fact]
    public async Task Commit_NoFiles_SendsNothing()
    {
        using var http = FakeHttp.Install(_ => throw new InvalidOperationException("must not send"));
        var result = await GitHubApi.CommitFilesAsync("o", "r", "main", "tok", Array.Empty<GitHubFileChange>(), "m");
        Assert.Equal(Strings.GitHub_NoChangesToCommit, result.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Commit_Unauthorized_StopsAtFirstStep()
    {
        using var http = FakeHttp.Install(_ => FakeHttp.Status(HttpStatusCode.Unauthorized));

        var result = await GitHubApi.CommitFilesAsync("o", "r", "main", "tok", TwoFiles, "m");

        Assert.True(result.AuthFailed);
        Assert.Equal(string.Format(Strings.GitHub_BranchFetchFailed, Strings.GitHub_Http401), result.Message);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Commit_UnexpectedResponseShape_StopsWithParseMessage()
    {
        using var http = FakeHttp.Install(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/git/trees")
            ? FakeHttp.Json("{}")
            : CommitFlow(r));

        var result = await GitHubApi.CommitFilesAsync("o", "r", "main", "tok", TwoFiles, "m");

        Assert.Equal(Strings.GitHub_TreeParseFailed, result.Message);
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task AppServices_PassesTargetThrough()
    {
        using var http = FakeHttp.Install(r => CommitFlow(r));
        var services = new AppServices(new AppSettings());
        var target = new GitHubTarget("o", "r", "dev", "d.json", "", "tok");

        Assert.True((await services.CommitGitHubAsync(target, TwoFiles, "m")).Ok);
        Assert.Contains("/heads/dev", http.Requests[0].Url);
    }
}

public class ZpdicApiTests
{
    [Fact]
    public async Task Fetch_ReadsOfferAndSendsKey()
    {
        using var http = FakeHttp.Install(_ => FakeHttp.Json("""
            { "exampleOffer": { "translation": "訳", "supplement": "補足", "author": "Ziphil" } }
            """));

        var result = await ZpdicApi.FetchAsync("apple Alpha", 3, "key");

        Assert.True(result.Ok);
        Assert.Equal(("訳", "補足", "Ziphil"), (result.Translation, result.Supplement, result.Author));
        Assert.Equal(string.Format(Strings.Zpdic_FetchOkWithAuthor, "Ziphil"), result.Message);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://zpdic.ziphil.com/api/v0/exampleOffer/apple%20Alpha/3", request.Url);
        Assert.Equal("key", request.Headers["X-Api-Key"]);
    }

    [Fact]
    public async Task Fetch_WithoutAuthor_UsesPlainMessage()
    {
        using var http = FakeHttp.Install(_ => FakeHttp.Json("""{ "exampleOffer": { "translation": "訳" } }"""));
        var result = await ZpdicApi.FetchAsync("meat", 1, "key");
        Assert.Equal((Strings.Zpdic_FetchOk, ""), (result.Message, result.Supplement));
    }

    [Fact]
    public async Task Fetch_NonAsciiKey_IsRejectedWithoutSending()
    {
        using var http = FakeHttp.Install(_ => throw new InvalidOperationException("must not send"));
        var result = await ZpdicApi.FetchAsync("meat", 1, "キー");
        Assert.True(result.KeyRejected);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false, false)]
    [InlineData(HttpStatusCode.Unauthorized, true, false)]
    [InlineData(HttpStatusCode.NotFound, false, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, false)]
    public async Task Fetch_HttpErrors(HttpStatusCode status, bool keyRejected, bool notFound)
    {
        using var http = FakeHttp.Install(_ => FakeHttp.Status(status));

        var result = await ZpdicApi.FetchAsync("meat", 9, "key");

        Assert.False(result.Ok);
        Assert.Equal((keyRejected, notFound), (result.KeyRejected, result.NotFound));
        var expected = status switch
        {
            HttpStatusCode.BadRequest => Strings.Zpdic_Http400,
            HttpStatusCode.Unauthorized => Strings.Zpdic_Http401,
            HttpStatusCode.NotFound => string.Format(Strings.Zpdic_Http404, 9),
            HttpStatusCode.TooManyRequests => Strings.Zpdic_Http429,
            _ => string.Format(Strings.Zpdic_HttpGeneric, 503),
        };
        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public async Task Fetch_MissingOffer_AndFailures_AreResultsNotExceptions()
    {
        using (FakeHttp.Install(_ => FakeHttp.Json("{}")))
            Assert.Equal(Strings.Zpdic_ResponseParseFailed, (await ZpdicApi.FetchAsync("m", 1, "k")).Message);

        using (FakeHttp.Install(_ => throw new TaskCanceledException()))
            Assert.Equal(Strings.Zpdic_Timeout, (await ZpdicApi.FetchAsync("m", 1, "k")).Message);

        using (FakeHttp.Install(_ => FakeHttp.Json("not json")))
            Assert.False((await ZpdicApi.FetchAsync("m", 1, "k")).Ok);
    }
}
