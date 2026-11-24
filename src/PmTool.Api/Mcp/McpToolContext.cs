using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Mcp;

/// <summary>
/// 工具用到的模組服務。只認合約介面，和 API 的端點一樣。用到才向容器要：一個工具呼叫通常只碰一兩個服務，
/// 不必為了其他工具先建好全部。
/// </summary>
internal sealed class McpServices(IServiceProvider provider)
{
    public IProjectService Projects => provider.GetRequiredService<IProjectService>();

    public IWorkItemService Items => provider.GetRequiredService<IWorkItemService>();

    public IItemActivityService Activity => provider.GetRequiredService<IItemActivityService>();

    public IBlueprintChangeService Changes => provider.GetRequiredService<IBlueprintChangeService>();

    public IBlueprintUsageService Usage => provider.GetRequiredService<IBlueprintUsageService>();

    public ITemplateLibrary Templates => provider.GetRequiredService<ITemplateLibrary>();

    public IItemProposalCreator ItemProposals => provider.GetRequiredService<IItemProposalCreator>();

    public IPeopleDirectory People => provider.GetRequiredService<IPeopleDirectory>();
}

/// <summary>
/// 一次工具呼叫的環境。權限一律用授權的人的 <see cref="Actor"/>：代理不會因為是 AI 就多拿到什麼。
/// </summary>
/// <param name="Issuer">這個部署對外的網址（<c>OAuthServerResolver</c> 算出來的），給「回傳網頁的網址」用。</param>
internal sealed record McpToolContext(Actor Actor, string Issuer, McpServices Services)
{
    /// <summary>
    /// 本機代理的請求限定的專案（<see cref="McpProjectScope"/>）；null 是一般的權杖，不限。
    /// 所有碰專案或項目的工具都要經過 <see cref="Project"/>、<see cref="Item"/>，限制只在這裡檢查一次。
    /// </summary>
    public string? ScopedProject { get; init; }

    /// <summary>專案代號要是限定的那個（沒有限定就都可以）；不是的當作不存在。</summary>
    public string Project(string projectKey) =>
        ScopedProject is null || string.Equals(projectKey.Trim(), ScopedProject, StringComparison.OrdinalIgnoreCase)
            ? projectKey
            : throw new McpArgumentException($"找不到專案「{projectKey}」。");

    /// <summary>項目編號是「專案代號-流水號」：專案代號要是限定的那個；不是的當作不存在。</summary>
    public string Item(string itemKey)
    {
        var dash = itemKey.LastIndexOf('-');
        return ScopedProject is null || (dash > 0 && string.Equals(itemKey[..dash].Trim(), ScopedProject, StringComparison.OrdinalIgnoreCase))
            ? itemKey
            : throw new McpArgumentException($"找不到項目「{itemKey}」。");
    }
}
