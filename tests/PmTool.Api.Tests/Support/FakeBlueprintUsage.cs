using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的設定使用狀況服務：記住呼叫的參數，回傳值由測試設定。API 的端點不用它，只有 MCP 的 describe_usage 用。</summary>
internal sealed class FakeBlueprintUsageService : IBlueprintUsageService
{
    private readonly List<(Actor Actor, string ProjectKey)> _calls = [];

    public Func<Actor, string, Result<BlueprintUsage>> OnDescribe { get; set; } =
        (_, _) => new BlueprintUsage(1, 0, [], []);

    public IReadOnlyList<(Actor Actor, string ProjectKey)> Calls => _calls;

    public Task<Result<BlueprintUsage>> DescribeAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _calls.Add((actor, projectKey));
        return Task.FromResult(OnDescribe(actor, projectKey));
    }
}

/// <summary>測試用的內建模板：真的模板庫要用真的驗證器載入，API 的測試用的是假的驗證器，所以換成這個。</summary>
internal sealed class FakeTemplateLibrary : ITemplateLibrary
{
    public IReadOnlyList<TemplateSummary> Templates { get; set; } =
        [new TemplateSummary("simple_tasks", "簡單任務", "待辦、進行中、完成", [], [], ["優先順序"], 0)];

    public IReadOnlyList<TemplateSummary> List() => Templates;

    public PmTool.Blueprint.Model.ProjectBlueprint? FindBlueprint(string templateKey) =>
        Templates.Any(template => template.Key == templateKey) ? TestData.Blueprint() : null;
}
