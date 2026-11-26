using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

/// <summary>助理與附件共用的檢查：看得到專案、有讀設定的權限、組織有開 AI。</summary>
internal static class AssistantAccess
{
    /// <summary>看得到專案、而且有讀設定的權限。助理的所有操作都先過這裡。</summary>
    public static async Task<Result<ProjectDetail>> OpenProjectAsync(
        IProjectService projects, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } failure)
        {
            return failure;
        }

        return project.Value.MyPermissions.Contains(WorkPermissions.BlueprintRead) ? project : AssistantFailures.Forbidden();
    }

    public static bool CanReadAllItems(ProjectDetail project) => project.MyPermissions.Contains(WorkPermissions.ItemRead);

    /// <summary>
    /// 組織沒開 AI 但允許個人連線時，成員也能開對話、用自己的連線送訊息；選的連線能不能用，送訊息時由 AiSelectionResolver 把關。
    /// 組織擁有者開了主機來源時，組織的有效成員也能開對話。
    /// </summary>
    public static async Task<bool> IsAvailableAsync(
        AiDbContext db, HostSourceOptions hostSources, Actor actor, CancellationToken cancellationToken) =>
        await db.Settings.AnyAsync(s => s.OrganizationId == actor.OrganizationId && (s.Enabled || s.AllowPersonalConnections), cancellationToken)
        || await hostSources.AnyAsync(actor, cancellationToken);
}
