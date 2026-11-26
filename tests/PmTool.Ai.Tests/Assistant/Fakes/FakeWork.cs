using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>助理用到的工作引擎介面的假物件：AI 模組的測試不連工作引擎，只看助理怎麼呼叫它、怎麼處理它的回答。</summary>
internal static class WorkSamples
{
    public static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly IReadOnlyList<string> AllPermissions =
        [WorkPermissions.ItemRead, WorkPermissions.BlueprintRead, WorkPermissions.BlueprintPropose, WorkPermissions.BlueprintApply];

    public static ProjectBlueprint Blueprint { get; } = new()
    {
        ApiVersion = "pmtool.dev/v0",
        Kind = "ProjectBlueprint",
        Metadata = new BlueprintMetadata { Key = "web", Name = "網站專案" },
        Fields = [],
        Workflows =
        [
            new Workflow
            {
                Key = "flow",
                Name = "流程",
                States =
                [
                    new WorkflowState { Key = "todo", Name = "待辦", Category = StateCategory.NotStarted },
                    new WorkflowState { Key = "done", Name = "完成", Category = StateCategory.Done },
                ],
            },
        ],
        ItemTypes = [new ItemType { Key = "task", Name = "任務", Workflow = "flow" }],
    };

    public static ProjectDetail Detail(IReadOnlyList<string>? permissions = null, long version = 3) => new(
        new ProjectSummary(ProjectId, "WEB", "網站改版", version, "admin", DateTimeOffset.UnixEpoch),
        Blueprint,
        [],
        permissions ?? AllPermissions);

    public static ChangeSetDetail ProposedDetail(long number) => new(
        new ChangeSetSummary(number, ChangeSetStatus.Pending, 3, "改名", Guid.NewGuid(), DateTimeOffset.UnixEpoch)
        {
            Origin = ChangeSetOrigin.Assistant,
        },
        Blueprint,
        [],
        [],
        [],
        CanApply: true)
    {
        BaseBlueprint = Blueprint,
    };

    public static ChangeSetPreview Preview() => new(3, 3, Blueprint, [], [], [], CanApply: true);

    public static Failure Failure(FailureKind kind, string code, string message, params FailureDetail[] details) =>
        new(kind, code, message) { Details = details };
}

internal sealed class FakeProjectService : IProjectService
{
    private readonly List<(Actor Actor, string Key)> _gets = [];

    public Func<Actor, string, Result<ProjectDetail>> OnGet { get; set; } = (_, _) => WorkSamples.Detail();

    public IReadOnlyList<(Actor Actor, string Key)> Gets => _gets;

    public IReadOnlyList<TemplateSummary> Templates { get; set; } = [];

    public IReadOnlyList<TemplateSummary> ListTemplates() => Templates;

    public Task<Result<ProjectDetail>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _gets.Add((actor, projectKey));
        return Task.FromResult(OnGet(actor, projectKey));
    }

    public Func<Actor, IReadOnlyList<ProjectSummary>> OnList { get; set; } = _ => throw new NotSupportedException();

    public Task<IReadOnlyList<ProjectSummary>> ListAsync(Actor actor, CancellationToken cancellationToken) => Task.FromResult(OnList(actor));

    public Func<Actor, NewProject, Result<ProjectSummary>> OnCreate { get; set; } = (_, _) => throw new NotSupportedException();

    public Func<Actor, NewProject, Result<Unit>> OnCheckNewProject { get; set; } = (_, _) => Unit.Value;

    public Task<Result<ProjectSummary>> CreateAsync(Actor actor, NewProject project, CancellationToken cancellationToken) =>
        Task.FromResult(OnCreate(actor, project));

    public Task<Result<Unit>> CheckNewProjectAsync(Actor actor, NewProject project, CancellationToken cancellationToken) =>
        Task.FromResult(OnCheckNewProject(actor, project));

    private readonly List<(Actor Actor, string Key, Guid PersonId, string? Role)> _memberWrites = [];

    /// <summary>設了就用它決定每一次成員寫入的結果（參數是第幾次、從 0 起算，人與角色；角色 null 是移出）；沒設時成功。</summary>
    public Func<int, Guid, string?, Result<ProjectMember>>? OnMemberWrite { get; set; }

    /// <summary>每一次 SetMember（角色有值）與 RemoveMember（角色 null）的呼叫。</summary>
    public IReadOnlyList<(Actor Actor, string Key, Guid PersonId, string? Role)> MemberWrites => _memberWrites;

    public Task<Result<ProjectMember>> SetMemberAsync(
        Actor actor, string projectKey, Guid personId, string role, CancellationToken cancellationToken) =>
        WriteMember(actor, projectKey, personId, role);

    public Task<Result<ProjectMember>> RemoveMemberAsync(
        Actor actor, string projectKey, Guid personId, CancellationToken cancellationToken) =>
        WriteMember(actor, projectKey, personId, null);

    private Task<Result<ProjectMember>> WriteMember(Actor actor, string projectKey, Guid personId, string? role)
    {
        int index;
        lock (_memberWrites)
        {
            index = _memberWrites.Count;
            _memberWrites.Add((actor, projectKey, personId, role));
        }

        return Task.FromResult(OnMemberWrite?.Invoke(index, personId, role) ?? new ProjectMember(personId, role ?? "removed"));
    }

    public Task<Result<ProjectSummary>> RenameAsync(Actor actor, string projectKey, string name, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ProjectSummary>> ArchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ProjectSummary>> UnarchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeBlueprintChangeService : IBlueprintChangeService
{
    private readonly List<ProposeCall> _proposals = [];
    private readonly List<string> _previews = [];

    public Func<string, Result<ChangeSetPreview>> OnPreview { get; set; } = _ => WorkSamples.Preview();

    public Func<string, Result<ChangeSetDetail>> OnPropose { get; set; } = _ => WorkSamples.ProposedDetail(12);

    public IReadOnlyList<ProposeCall> Proposals => _proposals;

    public IReadOnlyList<string> Previews => _previews;

    public Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken) =>
        ProposeAsync(actor, projectKey, document, format, ChangeSetOrigin.Direct, cancellationToken);

    public Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, ChangeSetOrigin origin, CancellationToken cancellationToken)
    {
        _proposals.Add(new ProposeCall(actor, projectKey, document, format, origin));
        return Task.FromResult(OnPropose(document));
    }

    public Task<Result<ChangeSetPreview>> PreviewAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken)
    {
        _previews.Add(document);
        return Task.FromResult(OnPreview(document));
    }

    public Task<Result<IReadOnlyList<ChangeSetSummary>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ChangeSetDetail>> GetAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ChangeSetDetail>> ApplyAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ChangeSetDetail>> RejectAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed record ProposeCall(Actor Actor, string ProjectKey, string Document, DocumentFormat Format, ChangeSetOrigin Origin);

internal sealed class FakeBlueprintUsageService : IBlueprintUsageService
{
    public Func<Result<BlueprintUsage>> OnDescribe { get; set; } = () => new BlueprintUsage(
        3, 5, [new StateUsage("task", "todo", 5)], [new FieldUsage("category", 2)]);

    /// <summary>設了就用它（可以卡住等取消）；沒設用 <see cref="OnDescribe"/>。</summary>
    public Func<CancellationToken, Task<Result<BlueprintUsage>>>? OnDescribeAsync { get; set; }

    public Task<Result<BlueprintUsage>> DescribeAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        OnDescribeAsync is { } slow ? slow(cancellationToken) : Task.FromResult(OnDescribe());
}

internal sealed class FakeTemplateLibrary : ITemplateLibrary
{
    public IReadOnlyList<TemplateSummary> Summaries { get; set; } =
    [
        new TemplateSummary(
            "simple_tasks", "簡單任務", "最少的設定",
            [new TemplateWorkflowPreview("任務流程", ["任務"], ["待辦", "完成"], ["取消"])],
            [],
            ["分類"],
            0),
    ];

    public IReadOnlyList<TemplateSummary> List() => Summaries;

    public ProjectBlueprint? FindBlueprint(string templateKey) => templateKey == "simple_tasks" ? WorkSamples.Blueprint : null;
}
