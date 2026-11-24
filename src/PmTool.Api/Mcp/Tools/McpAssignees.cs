using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 負責人的寫法：<c>me</c>（授權的人自己）、<c>none</c>（沒有負責人）、組織成員的 Email。
/// 代理不知道人員編號，也不該去猜；Email 在組織裡找不到，與找到的是停用的人，回同一句錯誤：
/// 不透露別的組織有沒有這個人，也不讓代理探測哪些人停用了。
/// </summary>
internal static class McpAssignees
{
    public const string Me = "me";

    public const string None = "none";

    public static bool IsNone(string text) => text.Trim().Equals(None, StringComparison.OrdinalIgnoreCase);

    /// <returns>人員編號；<c>none</c> 是 null。</returns>
    public static async Task<Guid?> ResolveAsync(McpToolContext context, string name, string text, CancellationToken cancellationToken)
    {
        var value = text.Trim();
        if (IsNone(value))
        {
            return null;
        }

        if (value.Equals(Me, StringComparison.OrdinalIgnoreCase))
        {
            return context.Actor.PersonId;
        }

        var people = await context.Services.People.ListAsync(context.Actor.OrganizationId, cancellationToken);
        var match = people.FirstOrDefault(person => !person.IsDisabled && string.Equals(person.Email, value, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? throw new McpArgumentException($"找不到可以指派的人：{value}")
            : match.PersonId;
    }
}
