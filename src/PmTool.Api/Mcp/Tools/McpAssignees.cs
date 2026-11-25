using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 負責人的寫法：<c>me</c>（授權的人自己）、<c>none</c>（沒有負責人）、那個專案成員的 Email。
/// 代理不知道人員編號，也不該去猜；只在專案的成員裡找（AG-10：網頁的人員目錄刻意不給訪客看整個組織，
/// 在整個組織裡用 Email 找人，就能逐一確認誰在組織裡）。Email 找不到、不是這個專案的成員、找到的是停用的人、
/// 或呼叫的人看不到這個專案，回同一句錯誤：不透露組織裡有沒有這個人，也不讓代理探測哪些人停用了。
/// </summary>
internal static class McpAssignees
{
    public const string Me = "me";

    public const string None = "none";

    public static bool IsNone(string text) => text.Trim().Equals(None, StringComparison.OrdinalIgnoreCase);

    /// <returns>人員編號；<c>none</c> 是 null。</returns>
    public static async Task<Guid?> ResolveAsync(
        McpToolContext context, string projectKey, string name, string text, CancellationToken cancellationToken)
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

        var notFound = new McpArgumentException($"找不到可以指派的人：{value}");
        var project = await context.Services.Projects.GetAsync(context.Actor, context.Project(projectKey), cancellationToken);
        if (project.Failure is not null)
        {
            throw notFound;
        }

        var memberIds = project.Value.Members.Select(member => member.PersonId).ToHashSet();
        var people = await context.Services.People.ListAsync(context.Actor.OrganizationId, cancellationToken);
        var match = people.FirstOrDefault(person =>
            memberIds.Contains(person.PersonId) && !person.IsDisabled && string.Equals(person.Email, value, StringComparison.OrdinalIgnoreCase));
        return match?.PersonId ?? throw notFound;
    }

    /// <summary>項目編號（<c>WEB-12</c>）的專案代號；格式不對時原樣回（之後查專案會找不到）。</summary>
    public static string ProjectKeyOf(string itemKey)
    {
        var dash = itemKey.LastIndexOf('-');
        return dash > 0 ? itemKey[..dash].Trim() : itemKey;
    }
}
