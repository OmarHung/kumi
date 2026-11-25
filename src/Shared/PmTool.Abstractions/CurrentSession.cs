namespace PmTool.Abstractions;

/// <summary>
/// 呼叫的人目前用的工作階段。模組不碰 HTTP；要在交易裡確認「發出這個要求的工作階段還有效」
/// （發權杖、授權之前）時問它，權杖就不必一路當參數傳進每一個方法。
/// </summary>
public interface ICurrentSession
{
    /// <summary>工作階段的權杖（明文）。不是用 Cookie 的工作階段呼叫的、或沒有請求時（背景工作、測試）是 null，模組不做這項檢查。</summary>
    string? Token { get; }
}

/// <summary>沒有請求可看的環境用它：永遠回 null。</summary>
public sealed class NullCurrentSession : ICurrentSession
{
    public static readonly NullCurrentSession Instance = new();

    private NullCurrentSession()
    {
    }

    public string? Token => null;
}
