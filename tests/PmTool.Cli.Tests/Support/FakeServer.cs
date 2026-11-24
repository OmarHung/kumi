using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace PmTool.Cli.Tests;

internal sealed record RecordedRequest(
    HttpMethod Method, Uri Uri, string? Authorization, string? ContentType, string Body, string? UserAgent,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public string Path => Uri.AbsolutePath;

    public string? Query(string name) => HttpUtility.ParseQueryString(Uri.Query)[name];

    public IReadOnlyList<string> QueryAll(string name) => HttpUtility.ParseQueryString(Uri.Query).GetValues(name) ?? [];

    public string? Header(string name) => Headers is not null && Headers.TryGetValue(name, out var value) ? value : null;

    public JsonNode? Json => Body.Length == 0 ? null : JsonNode.Parse(Body);
}

/// <summary>
/// 假的伺服器：照「方法 加 路徑」回事先設定好的信封，記下每個請求。
/// 沒設定的路徑直接讓測試失敗，免得指令呼叫了不該呼叫的 API 卻沒人發現。
/// </summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Dictionary<string, Func<RecordedRequest, HttpResponseMessage>> _routes = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<(string Method, System.Text.RegularExpressions.Regex Path, Func<RecordedRequest, HttpResponseMessage> Respond)> _patterns = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            // 長駐指令的回報與登記在不同的執行緒上同時送。
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>設成例外時，每個請求都丟它（模擬連不上）。</summary>
    public Exception? Failure { get; set; }

    public FakeServer On(string method, string path, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _routes[$"{method} {path}"] = respond;
        return this;
    }

    /// <summary>路徑裡有變動的部分（項目編號）時用：整條路徑要符合正規表示式。精確的路徑優先。</summary>
    public FakeServer OnPattern(string method, string pathPattern, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _patterns.Add((method, new System.Text.RegularExpressions.Regex($"^{pathPattern}$"), respond));
        return this;
    }

    public FakeServer On(string method, string path, object? data, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => Envelope(status, data));

    public FakeServer OnError(string method, string path, HttpStatusCode status, string code, string message, params (string Path, string Message)[] details) =>
        On(method, path, _ => new HttpResponseMessage(status)
        {
            Content = Json(new
            {
                success = false,
                data = (object?)null,
                error = new { code, message, details = details.Select(d => new { code = "detail", path = d.Path, message = d.Message }).ToArray() },
            }),
        });

    public IReadOnlyList<RecordedRequest> To(string method, string path) =>
        [.. Requests.Where(r => r.Method.Method == method && r.Path == path)];

    public static HttpResponseMessage Envelope(HttpStatusCode status, object? data) =>
        new(status) { Content = Json(new { success = true, data, error = (object?)null }) };

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Headers.UserAgent.ToString(),
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase));
        lock (_requests)
        {
            _requests.Add(recorded);
        }

        if (_routes.TryGetValue($"{request.Method.Method} {recorded.Path}", out var respond))
        {
            return respond(recorded);
        }

        var pattern = _patterns.FirstOrDefault(entry => entry.Method == request.Method.Method && entry.Path.IsMatch(recorded.Path));
        return pattern.Respond is not null
            ? pattern.Respond(recorded)
            : throw new InvalidOperationException($"測試沒有設定 {request.Method.Method} {recorded.Path}。");
    }
}
