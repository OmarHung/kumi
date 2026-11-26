using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 一個專案裡現在能被指派、被提及的人：專案成員、帳號沒有停用。get_item 回給模型（對人要用編號），
/// 負責人的編號也只在這份名單裡找。只有編號、顯示名稱、專案角色與職稱：不給 Email，名單不會變成整個組織的通訊錄（AG-10）。
/// </summary>
internal sealed record McpMember(Guid Id, string Name, string Role, string? JobTitle = null);

internal static class McpMembers
{
    public static async Task<IReadOnlyList<McpMember>> OfAsync(
        McpToolContext context, ProjectDetail project, CancellationToken cancellationToken)
    {
        var ids = project.Members.Select(member => member.PersonId).ToArray();
        var people = await context.Services.People.FindManyAsync(context.Actor.OrganizationId, ids, cancellationToken);
        return
        [
            .. project.Members
                .Select(member => (member, person: people.GetValueOrDefault(member.PersonId)))
                .Where(pair => pair.person is { IsDisabled: false })
                .Select(pair => new McpMember(pair.member.PersonId, pair.person!.DisplayName, pair.member.Role, pair.person.JobTitle)),
        ];
    }
}
