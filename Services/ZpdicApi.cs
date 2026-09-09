using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZasDictWin.Resources;

namespace ZasDictWin.Services;

/// <summary>照会の結果。<see cref="Ok"/> が偽なら <see cref="Message"/> をそのまま画面に出す。</summary>
public sealed record ExampleOfferResult(
    bool Ok,
    string Message,
    string Translation = "",
    string Supplement = "",
    string Author = "")
{
    /// <summary>APIキーが原因の失敗。呼び出し側は保存済みのキーを捨てて入力し直させる。</summary>
    public bool KeyRejected { get; init; }

    /// <summary>その番号の例文が無い。呼び出し側は番号を 0 に戻す。</summary>
    public bool NotFound { get; init; }
}

/// <summary>
/// ZpDIC Online の例文提供（exampleOffer）API。番号を渡すと訳と補足が返る。
///
/// APIキーは設定ファイル（settings.json）ではなく専用のファイルに置く。settings.json は
/// 辞書のパスと表示設定だけを持つ約束で、秘密情報を混ぜると添付やバックアップの扱いが変わるため。
/// </summary>
public static class ZpdicApi
{
    private const string Endpoint = "https://zpdic.ziphil.com/api/v0/exampleOffer";

    /// <summary>接続とヘッダを使い回す。HttpClient は都度 new するとソケットを食い潰す。</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string ApiKeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin", "zpdic_api_key");

    public static string? LoadApiKey()
    {
        try
        {
            if (!File.Exists(ApiKeyPath)) return null;
            var key = File.ReadAllText(ApiKeyPath).Trim();
            return key.Length == 0 ? null : key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 画面には「APIキーが未設定です」としか出せないので、読めなかった事実は記録に残す。
            ErrorLog.Write($"APIキーの読み込み ({ApiKeyPath})", ex);
            return null;
        }
    }

    public static void SaveApiKey(string key)
    {
        var dir = Path.GetDirectoryName(ApiKeyPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(ApiKeyPath, key.Trim());
    }

    public static void DeleteApiKey()
    {
        try { File.Delete(ApiKeyPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 消せなくても次の照会で弾かれるだけ */ }
    }

    /// <summary>出典の例文を 1 つ引く。例外は投げず、失敗も結果として返す。</summary>
    public static async Task<ExampleOfferResult> FetchAsync(string catalog, int number, string apiKey)
    {
        // HTTP ヘッダーは latin-1 しか通らないので、非 ASCII のキーは送る前に弾く。
        if (!apiKey.All(char.IsAscii))
        {
            return new ExampleOfferResult(false, Strings.Zpdic_KeyInvalidChars)
            { KeyRejected = true };
        }

        var url = $"{Endpoint}/{Uri.EscapeDataString(catalog)}/{number}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", apiKey);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK) return Failure(response.StatusCode, number);

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var offer = JsonNode.Parse(body)?["exampleOffer"] as JsonObject;
            if (offer is null) return new ExampleOfferResult(false, Strings.Zpdic_ResponseParseFailed);

            var author = offer["author"]?.GetValue<string>() ?? "";
            return new ExampleOfferResult(
                true,
                author.Length > 0 ? string.Format(Strings.Zpdic_FetchOkWithAuthor, author) : Strings.Zpdic_FetchOk,
                offer["translation"]?.GetValue<string>() ?? "",
                offer["supplement"]?.GetValue<string>() ?? "",
                author);
        }
        catch (TaskCanceledException)
        {
            return new ExampleOfferResult(false, Strings.Zpdic_Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new ExampleOfferResult(false, string.Format(Strings.Zpdic_FetchFailed, ex.Message));
        }
    }

    private static ExampleOfferResult Failure(HttpStatusCode status, int number) => status switch
    {
        HttpStatusCode.BadRequest =>
            new ExampleOfferResult(false, Strings.Zpdic_Http400),
        HttpStatusCode.Unauthorized =>
            new ExampleOfferResult(false, Strings.Zpdic_Http401) { KeyRejected = true },
        HttpStatusCode.NotFound =>
            new ExampleOfferResult(false, string.Format(Strings.Zpdic_Http404, number)) { NotFound = true },
        HttpStatusCode.TooManyRequests =>
            new ExampleOfferResult(false, Strings.Zpdic_Http429),
        _ => new ExampleOfferResult(false, string.Format(Strings.Zpdic_HttpGeneric, (int)status))
    };
}
