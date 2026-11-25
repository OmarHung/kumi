namespace PmTool.Ai.Providers;

/// <summary>
/// 成員自己的 OpenAI 相容端點只能連 https 的 <see cref="AiRules.PersonalEndpointPort"/>（AI-04）。存檔時就擋（<c>AiConnectionValidator</c>），
/// 這一層是連線時再擋一次：規則加上之前存下來的網址、或改了資料庫的值，也連不出去。
/// </summary>
internal sealed class PersonalEndpointHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.RequestUri is { Scheme: "https", Port: AiRules.PersonalEndpointPort }
            ? base.SendAsync(request, cancellationToken)
            : throw new HttpRequestException("自己的 AI 帳號的端點只能連 https 的 443。");
    }
}
