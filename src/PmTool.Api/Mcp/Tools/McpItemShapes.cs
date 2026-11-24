using PmTool.Api.Endpoints;
using PmTool.Work;

namespace PmTool.Api.Mcp.Tools;

/// <summary>項目在 MCP 上的形狀：和 API 同一份（<see cref="ItemData"/>），負責人帶顯示名稱，代理不必另外查人員。</summary>
internal static class McpItemShapes
{
    public static async Task<ItemDetailData> DetailAsync(McpToolContext context, WorkItem item, CancellationToken cancellationToken)
    {
        var list = await ListAsync(context, [item], cancellationToken);
        return new ItemDetailData(list[0], item.Description);
    }

    public static Task<IReadOnlyList<ItemData>> ListAsync(
        McpToolContext context, IReadOnlyList<WorkItem> items, CancellationToken cancellationToken) =>
        ItemData.FromManyAsync(items, context.Services.People, context.Actor.OrganizationId, cancellationToken);
}
