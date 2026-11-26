using PmTool.Api.OAuth;

namespace PmTool.Api.Endpoints;

internal static class ApiRoutes
{
    public const string Prefix = "/v1";

    /// <summary>
    /// 需要登入的端點都掛在這一組底下，登入的要求只寫一次，新加的端點不會忘了加。
    /// 不用全域的預設授權政策：那會連找不到的路徑與靜態檔都要求登入，404 就變成 401 了。
    /// </summary>
    public static IEndpointRouteBuilder MapSignedInEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup(Prefix)
            .RequireAuthorization()
            .MapProjectEndpoints()
            .MapProjectDescriptionEndpoints()
            .MapItemEndpoints()
            .MapCrossProjectEndpoints()
            .MapItemRecoveryEndpoints()
            .MapItemImportEndpoints()
            .MapHistoryImportEndpoints()
            .MapItemActivityEndpoints()
            .MapAttachmentEndpoints()
            .MapKnowledgeSpaceEndpoints()
            .MapKnowledgePageEndpoints()
            .MapKnowledgeAttachmentEndpoints()
            .MapSavedViewEndpoints()
            .MapMyWorkSavedViewEndpoints()
            .MapPlanningEndpoints()
            .MapOverviewEndpoints()
            .MapNotificationEndpoints()
            .MapEmailEndpoints()
            .MapAiSettingsEndpoints()
            .MapPersonalAiEndpoints()
            .MapAssistantEndpoints()
            .MapAssistantAttachmentEndpoints()
            .MapItemProposalEndpoints()
            .MapNewItemProposalEndpoints()
            .MapMemberProposalEndpoints()
            .MapProjectProposalEndpoints()
            .MapLocalAgentEndpoints()
            .MapHostAgentAdminEndpoints()
            .MapChangeSetEndpoints()
            .MapPeopleEndpoints()
            .MapAuditEndpoints()
            .MapStorageEndpoints()
            .MapAccountEndpoints()
            .MapAccessTokenEndpoints()
            .MapOAuthAccountEndpoints()
            .MapCliLoginEndpoints()
            .MapCliEndpoints()
            .MapClientErrorEndpoints();
        return app;
    }
}
