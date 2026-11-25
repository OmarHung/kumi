using PmTool.Abstractions;

namespace PmTool.Api.Security;

/// <summary>
/// 身分模組問「發出這個要求的工作階段是哪一個」時的答案：從目前請求的 Cookie 票證取權杖。
/// 用存取權杖或主機代理的憑證呼叫的請求沒有工作階段，回 null。型別是 public 的，API 的測試專案直接測它。
/// </summary>
public sealed class HttpCurrentSession(IHttpContextAccessor accessor) : ICurrentSession
{
    public string? Token => SessionPrincipal.TryReadToken(accessor.HttpContext?.User);
}
