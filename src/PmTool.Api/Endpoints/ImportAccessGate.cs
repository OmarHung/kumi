using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 匯入端點在讀內文之前的粗略權限檢查。內文可以有十幾 MB，沒權限的人不該讓伺服器先把它讀完；
/// 模組裡完整的檢查（每一列、每個欄位）照舊，這裡只擋「連這個專案都看不到、或做不了這件事」的人。
/// </summary>
internal static class ImportAccessGate
{
    /// <summary>通過回 null；沒通過回要送出的結果。看不到專案是找不到，看得到但權限不夠是 403，封存的專案和模組回同一個 409。</summary>
    public static async Task<IResult?> CheckAsync(HttpContext context, string projectKey, string permission, bool rejectArchived)
    {
        var actor = SessionPrincipal.Read(context.User).ToActor();
        var queries = context.RequestServices.GetRequiredService<IWorkAccessQueries>();
        var project = await queries.FindVisibleProjectAsync(actor, projectKey, context.RequestAborted);
        if (project is null)
        {
            return EndpointResults.Fail(new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, $"找不到專案「{projectKey}」。"));
        }

        // 封存的專案權限已經被收成只剩讀取那幾個：先看封存，才會和模組一樣回 409，而不是「沒有權限」。
        if (rejectArchived && project.IsArchived)
        {
            return EndpointResults.Fail(new Failure(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, $"專案「{project.Name}」已封存，只能看不能改。"));
        }

        return project.Permissions.Contains(permission, StringComparer.Ordinal)
            ? null
            : EndpointResults.Fail(new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你在這個專案沒有匯入的權限。"));
    }
}
