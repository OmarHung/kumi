using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace PmTool.Ai.Tests.Providers;

/// <summary>接住 SDK 送出的請求：記下方法、網址、標頭與本文，照腳本回應。不連網路。</summary>
internal sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public RecordedRequest Single => _requests.Single();

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage EventStream(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method.Method, request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), body);
        _requests.Add(recorded);
        return respond(recorded);
    }
}

internal sealed record RecordedRequest(string Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonNode Json => JsonNode.Parse(Body)!;
}
