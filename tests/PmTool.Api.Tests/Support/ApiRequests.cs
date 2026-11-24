using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Api.Tests.Support;

internal static class ApiRequests
{
    public const string BlueprintRoute = "/v1/blueprints/validate";
    public const string ChangeSetRoute = "/v1/changesets/validate";
    public const string CsrfHeader = "X-PmTool-Csrf";

    /// <summary>把物件寫成 JSON 內文。測試裡的屬性名稱照合約寫（camelCase），不靠序列化設定轉換。</summary>
    public static HttpContent Json(object body) => Document(JsonSerializer.Serialize(body));

    public static HttpContent Document(string body, string? contentType = "application/json") =>
        Bytes(Encoding.UTF8.GetBytes(body), contentType);

    public static HttpContent Bytes(byte[] body, string? contentType = "application/json")
    {
        var content = new ByteArrayContent(body);
        return WithContentType(content, contentType);
    }

    /// <summary>
    /// chunked 上傳，伺服器看不到 Content-Length。不標 chunked 的話，TestServer 會先把內文讀完、自己補上 Content-Length。
    /// </summary>
    public static HttpRequestMessage Chunked(string route, byte[] body, string? contentType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = WithContentType(new UnknownLengthContent(body), contentType),
        };
        request.Headers.TransferEncodingChunked = true;
        return request;
    }

    public static async Task<JsonObject> ReadEnvelopeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text)?.AsObject()
            ?? throw new AssertionException($"回應不是 JSON 物件：{text}");
    }

    /// <summary>失敗的信封，而且沒有細項（details 是空陣列）。</summary>
    public static async Task AssertErrorEnvelopeAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        var details = await AssertFailureEnvelopeAsync(response, expectedStatus, expectedCode);
        Assert.That(details, Is.Empty, "沒有細項時 details 是空陣列");
    }

    /// <summary>失敗的信封，回傳 details 讓測試自己檢查細項。</summary>
    public static async Task<JsonArray> AssertFailureEnvelopeAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        var envelope = await ReadEnvelopeAsync(response);
        var details = envelope["error"]?["details"] as JsonArray;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(envelope["success"]?.GetValue<bool>(), Is.False);
            Assert.That(envelope.ContainsKey("data") && envelope["data"] is null, Is.True, "data 要寫出 null");
            Assert.That(envelope["error"]?["code"]?.GetValue<string>(), Is.EqualTo(expectedCode));
            Assert.That(envelope["error"]?["message"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
            Assert.That(details, Is.Not.Null, "error 一定要有 details 陣列");
        }

        return details!;
    }

    /// <summary>成功的信封，回傳 data。</summary>
    public static async Task<JsonNode?> AssertSuccessEnvelopeAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        var text = await response.Content.ReadAsStringAsync();
        var envelope = JsonNode.Parse(text)?.AsObject() ?? throw new AssertionException($"回應不是 JSON 物件：{text}");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus), text);
            Assert.That(envelope["success"]?.GetValue<bool>(), Is.True, text);
            Assert.That(envelope.ContainsKey("error") && envelope["error"] is null, Is.True, "error 要寫出 null");
        }

        return envelope["data"];
    }

    /// <summary>描述細項，方便一次比對整個陣列。</summary>
    public static string[] Describe(JsonArray details) =>
        [.. details.Select(detail => $"{detail?["code"]}|{detail?["path"]}")];

    private static HttpContent WithContentType(HttpContent content, string? contentType)
    {
        content.Headers.ContentType = null;
        if (contentType is not null)
        {
            // 不經過 HttpClient 的格式檢查，才能送出大小寫混用、帶引號參數這類真實用戶端會送的值。
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return content;
    }

    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
