namespace PmTool.Blueprint.Model;

public enum ConnectorType
{
    Gitlab,
    Github,
    Webhook,
}

/// <summary>這個專案連到哪個外部資源。憑證不放在藍圖裡。</summary>
public sealed record IntegrationBinding
{
    public required ConnectorType Connector { get; init; }
    public required string Resource { get; init; }
}
