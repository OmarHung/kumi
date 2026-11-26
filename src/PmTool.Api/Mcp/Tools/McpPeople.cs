using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 人員的寫法（派工用）：<c>me</c>（授權的人自己）、<c>none</c>、人員編號或 Email，在<strong>整個組織</strong>裡找（停用的找不到）。
/// 負責人還不是專案成員時，助理要能把他放進 addMembers 或 propose_member_changes，所以不能只在專案成員裡找（<see cref="McpAssignees"/> 的做法）；
/// 是不是成員、能不能指派，交給 AI 模組的建立端判斷。
/// 訪客：網頁的人員目錄刻意不給訪客看整個組織（AG-10），在整個組織裡用 Email 找人就能逐一確認誰在組織裡，
/// 所以訪客照舊只在專案的成員裡找（<see cref="McpAssignees.ResolveAsync"/>）。找不到、停用、不在這個組織都回同一句，不透露哪一種。
/// </summary>
internal static class McpPeople
{
    /// <returns>人員編號；<c>none</c> 是 null。</returns>
    public static async Task<Guid?> ResolveAsync(
        McpToolContext context, string projectKey, string text, string notFoundMessage, CancellationToken cancellationToken)
    {
        var value = text.Trim();
        if (McpAssignees.IsNone(value))
        {
            return null;
        }

        if (value.Equals(McpAssignees.Me, StringComparison.OrdinalIgnoreCase))
        {
            return context.Actor.PersonId;
        }

        var self = await context.Services.People.FindManyAsync(context.Actor.OrganizationId, [context.Actor.PersonId], cancellationToken);
        if (self.GetValueOrDefault(context.Actor.PersonId)?.Role == OrganizationRole.Guest)
        {
            return await McpAssignees.ResolveAsync(context, projectKey, "person", value, cancellationToken);
        }

        var notFound = new McpArgumentException($"{notFoundMessage}：{value}");
        if (Guid.TryParse(value, out var personId))
        {
            var found = await context.Services.People.FindManyAsync(context.Actor.OrganizationId, [personId], cancellationToken);
            return found.GetValueOrDefault(personId) is { IsDisabled: false } ? personId : throw notFound;
        }

        var people = await context.Services.People.ListAsync(context.Actor.OrganizationId, cancellationToken);
        var match = people.FirstOrDefault(person => !person.IsDisabled && string.Equals(person.Email, value, StringComparison.OrdinalIgnoreCase));
        return match?.PersonId ?? throw notFound;
    }
}
