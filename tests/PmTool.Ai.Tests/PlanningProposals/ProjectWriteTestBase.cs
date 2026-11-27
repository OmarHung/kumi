using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.MemberProposals;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.PlanningProposals;

/// <summary>
/// 週期與里程碑、改專案兩種建議測試的共同環境：專案 WEB（三位成員：管理者甲、編輯者乙、執行者丙），
/// 專案、專案說明、週期與里程碑都是假的記憶體物件，建議存在真的 PostgreSQL。
/// </summary>
public abstract class ProjectWriteTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal WritablePlanningService Planning { get; private set; } = null!;

    internal WritableDescriptions Descriptions { get; private set; } = null!;

    internal WritableProjectService Projects { get; private set; } = null!;

    internal NamedPeopleDirectory People { get; private set; } = null!;

    internal IPlanningProposalCreator PlanningCreator => _h.Host.Get<IPlanningProposalCreator>();

    internal IPlanningProposalService PlanningService => _h.Host.Get<IPlanningProposalService>();

    internal IProjectChangeProposalCreator ChangeCreator => _h.Host.Get<IProjectChangeProposalCreator>();

    internal IProjectChangeProposalService ChangeService => _h.Host.Get<IProjectChangeProposalService>();

    protected Actor Me => _h.Actor;

    protected Guid Manager { get; private set; }

    protected Guid Editor { get; private set; }

    [SetUp]
    public async Task StartAsync()
    {
        People = new NamedPeopleDirectory();
        Planning = new WritablePlanningService();
        Descriptions = new WritableDescriptions();
        Projects = new WritableProjectService();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IPeopleDirectory>(People);
            services.AddSingleton<IProjectStaffingQueries>(new PmTool.Ai.Tests.MemberProposals.FakeStaffingQueries());
            services.AddSingleton<IPlanningService>(Planning);
            services.AddSingleton<IProjectDescriptionService>(Descriptions);
            services.AddSingleton<IProjectService>(Projects);
        });
        Manager = People.Add("甲");
        Editor = People.Add("乙");
        UseProject();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    /// <summary>設定專案的詳細資料：呼叫的人的權限、有沒有封存。預設是管理者（改項目、看得到全部、管理專案）。</summary>
    protected void UseProject(bool canManage = true, bool canUpdate = true, bool readsAll = true, bool archived = false, string name = "網站改版")
    {
        // 開助理對話要有讀藍圖的權限，和這兩種建議要的權限無關。
        List<string> permissions = [WorkPermissions.BlueprintRead];
        if (canManage)
        {
            permissions.Add(WorkPermissions.ProjectManage);
        }

        if (canUpdate)
        {
            permissions.Add(WorkPermissions.ItemUpdate);
        }

        if (readsAll)
        {
            permissions.Add(WorkPermissions.ItemRead);
        }

        var detail = new ProjectDetail(
            new ProjectSummary(WorkSamples.ProjectId, "WEB", name, 3, "admin", DateTimeOffset.UnixEpoch)
            {
                ArchivedAt = archived ? DateTimeOffset.UnixEpoch : null,
            },
            WorkSamples.Blueprint,
            [new(Manager, "admin"), new(Editor, "editor")],
            permissions);
        Projects.OnGet = (_, _) => detail;
    }

    protected static string Describe(Failure? failure) =>
        failure is null ? string.Empty : failure.Message + "；" + string.Join("；", failure.Details.Select(d => d.Path + " " + d.Message));

    protected static string IssueAt<T>(Result<T> result, string path) => result.Failure!.Details.Single(d => d.Path == path).Message;

    /// <summary>讓模型呼叫一個工具，回傳下一次呼叫模型時帶上的那個工具結果（也就是模型看到的）。</summary>
    protected async Task<JsonElement> RunToolAsync(string tool, object arguments)
    {
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", tool, arguments)).Reply("好");
        await H.SendAsync(conversation.Id);
        var result = H.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }

    /// <summary>把週期與里程碑建議直接改成「建立中」（模擬套用到一半程序結束）。</summary>
    protected async Task StickPlanningInApplyingAsync(Guid id, string changesJson)
    {
        await using var db = await H.Host.CreateDbContextAsync();
        await db.PlanningProposals.Where(p => p.Id == id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, PlanningProposalRow.ApplyingStatus)
                .SetProperty(p => p.DecidedAt, H.Clock.GetUtcNow())
                .SetProperty(p => p.Changes, changesJson));
    }

    /// <summary>把一則助理的回覆（帶這個建議的工具段落）直接寫進對話，帳本才找得到它。</summary>
    internal async Task SeedToolReplyAsync(Guid conversationId, string tool, Guid proposalId)
    {
        await using var db = await H.Host.CreateDbContextAsync();
        db.Messages.Add(new AiMessageRow
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            OrganizationId = H.OrganizationId,
            Sequence = 1,
            Role = AssistantMessageMapper.AssistantRoleName,
            Parts = AssistantMessageMapper.WriteParts([new AssistantToolPart("c1", tool, AssistantToolStatus.Done, null, null) { ProposalId = proposalId }]),
            Transcript = "[]",
            Status = "complete",
            CreatedAt = H.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(set => set.SetProperty(c => c.MessageCount, 1));
    }

    internal async Task<PlanningProposalRow> PlanningRowAsync(Guid id)
    {
        await using var db = await H.Host.CreateDbContextAsync();
        return await db.PlanningProposals.AsNoTracking().SingleAsync(p => p.Id == id);
    }
}
