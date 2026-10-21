using System.Net;
using System.Net.Http;
using System.Text;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

/// <summary>
/// GitHubApi と ZpdicApi の HttpClient を差し替え、届いた要求を記録して決め打ちの応答を返す。
/// Dispose で元の HttpClient に戻すので、テストから実際の通信は出ない。
/// </summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    private readonly HttpClient _savedGitHub = GitHubApi.Http;
    private readonly HttpClient _savedZpdic = ZpdicApi.Http;

    /// <summary>要求は送った側で破棄されるので、確かめたい値はここで写し取っておく。</summary>
    public sealed record Request(HttpMethod Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);

    public List<Request> Requests { get; } = new();

    private FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public static FakeHttp Install(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var fake = new FakeHttp(respond);
        var client = new HttpClient(fake);
        GitHubApi.Http = client;
        ZpdicApi.Http = client;
        return fake;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content?.ReadAsStringAsync(cancellationToken).Result;
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new Request(request.Method, request.RequestUri!.AbsoluteUri, body, headers));
        return Task.FromResult(_respond(request));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GitHubApi.Http = _savedGitHub;
            ZpdicApi.Http = _savedZpdic;
        }
        base.Dispose(disposing);
    }
}
