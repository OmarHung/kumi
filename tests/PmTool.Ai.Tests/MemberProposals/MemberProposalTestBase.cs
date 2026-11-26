using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.MemberProposals;

/// <summary>
/// 成員建議與新增項目建議（addMembers）測試的共同環境：專案 WEB 有三位成員（管理者甲、執行者乙、編輯者丙），
/// 另有還不是成員的丁、戊與已停用的己。
/// </summary>
public abstract class MemberProposalTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal NamedPeopleDirectory People { get; private set; } = null!;

    internal FakeStaffingQueries Staffing { get; private set; } = null!;

    internal FakePlanningService Planning { get; private set; } = null!;

    internal IMemberProposalCreator Creator => _h.Host.Get<IMemberProposalCreator>();

    internal IMemberProposalService Proposals => _h.Host.Get<IMemberProposalService>();

    internal INewItemProposalCreator NewItemCreator => _h.Host.Get<INewItemProposalCreator>();

    internal INewItemProposalService NewItems => _h.Host.Get<INewItemProposalService>();

    protected Actor Me => _h.Actor;

    protected Guid Manager { get; private set; }

    protected Guid Contributor { get; private set; }

    protected Guid Editor { get; private set; }

    protected Guid Outsider { get; private set; }

    protected Guid OtherOutsider { get; private set; }

    protected Guid Disabled { get; private set; }

    [SetUp]
    public async Task StartAsync()
    {
        People = new NamedPeopleDirectory();
        Staffing = new FakeStaffingQueries();
        Planning = new FakePlanningService();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IPeopleDirectory>(People);
            services.AddSingleton<IProjectStaffingQueries>(Staffing);
            services.AddSingleton<IPlanningService>(Planning);
        });
        Manager = People.Add("甲");
        Contributor = People.Add("乙");
        Editor = People.Add("丙");
        Outsider = People.Add("丁");
        OtherOutsider = People.Add("戊");
        Disabled = People.Add("己", disabled: true);
        UseProject(canManage: true);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    /// <summary>設定專案的詳細資料：呼叫的人有沒有 project.manage。</summary>
    protected void UseProject(bool canManage)
    {
        List<string> permissions = [.. ItemSamples.EditorPermissions, WorkPermissions.ItemCreate];
        if (canManage)
        {
            permissions.Add(WorkPermissions.ProjectManage);
        }

        var detail = ItemSamples.DetailWithMember(permissions) with
        {
            Blueprint = Blueprint(),
            Members = [new(Manager, "admin"), new(Contributor, "contributor"), new(Editor, "editor")],
        };
        _h.Projects.OnGet = (_, _) => detail;
    }

    protected static PmTool.Blueprint.Model.ProjectBlueprint Blueprint() => WorkSamples.Blueprint;

    protected static MemberProposalDraft Draft(params MemberChangeDraft[] changes) => new("WEB", changes);

    protected async Task<MemberProposal> CreatedAsync(params MemberChangeDraft[] changes)
    {
        var created = await Creator.CreateAsync(Me, Draft(changes), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
        return created.Value;
    }

    protected static string Describe(Failure? failure) =>
        failure is null ? string.Empty : failure.Message + string.Join("；", failure.Details.Select(d => d.Path + " " + d.Message));

    protected static string IssueAt<T>(Result<T> result, string path) => result.Failure!.Details.Single(d => d.Path == path).Message;
}
