using Microsoft.Extensions.AI;

namespace PmTool.Ai.Providers;

/// <summary>
/// 對供應商的呼叫都經過這裡（internal/HANDOFF-ai-assistant.md 第 4.1 節）。註冊用 TryAdd，測試先放假的，就不會真的連出去。
/// </summary>
internal interface IAiProviderGateway
{
    /// <summary>讀模型清單（第 4.2 節），逾時 <see cref="AiRules.DiscoveryTimeout"/>。失敗丟 <see cref="AiProviderException"/>。</summary>
    Task<IReadOnlyList<AiDiscoveredModel>> DiscoverAsync(AiEndpoint endpoint, CancellationToken cancellationToken);

    /// <summary>這個連線、這個模型的 <see cref="IChatClient"/>。還沒包工具呼叫的迴圈：助理自己包。</summary>
    IChatClient CreateChatClient(AiEndpoint endpoint, string modelId);
}

/// <summary>解開之後的連線。金鑰是明文，<see cref="ToString"/> 一律遮住。</summary>
/// <param name="BaseUrl">只有相容端點有。</param>
/// <param name="Personal">
/// 成員自己的連線。相容端點的位址一律檢查（擋私有網段與本機），不管 <c>Ai:AllowPrivateEndpoints</c>：
/// 那個設定只是讓管理員接內網的 Ollama，不能讓任何成員都能叫伺服器連內網。
/// </param>
internal sealed record AiEndpoint(AiProviderKind Kind, string? BaseUrl, string? ApiKey, bool Personal = false)
{
    /// <summary>組織擁有者開的主機來源的編號（<see cref="AiConnectionOption.Hosted"/>）；不是主機來源是 null。主機來源不經過供應商的 gateway。</summary>
    public Guid? HostSourceId { get; init; }

    public override string ToString() =>
        $"AiEndpoint {{ Kind = {Kind}, BaseUrl = {BaseUrl}, ApiKey = {(ApiKey is null ? "null" : "***")}, Personal = {Personal}, HostSourceId = {HostSourceId} }}";
}

/// <summary>連供應商失敗。<see cref="Exception.Message"/> 是給人看的原因（繁體中文，第 4.5 節），不含金鑰。</summary>
internal sealed class AiProviderException : Exception
{
    public AiProviderException(string message)
        : base(message)
    {
    }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
