using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Actions.Knowledge;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Ai.Tests.Actions.Knowledge;

/// <summary>知識庫動作建議處理器的測試底：知識庫與人員目錄用假的，一個使用者（一般成員或管理員）。</summary>
internal abstract class KnowledgeActionTestBase
{
    protected FakeKnowledgeSpaces Spaces { get; private set; } = null!;

    protected FakeKnowledgePages Pages { get; private set; } = null!;

    protected FakePeopleDirectory People { get; private set; } = null!;

    protected Actor Member { get; } = new(Guid.NewGuid(), Guid.NewGuid(), IsOrganizationAdmin: false);

    protected Actor Admin => Member with { IsOrganizationAdmin = true };

    private KnowledgeActionHandler _handler = null!;

    [SetUp]
    public void CreateHandler()
    {
        Spaces = new FakeKnowledgeSpaces();
        Pages = new FakeKnowledgePages();
        People = new FakePeopleDirectory { DefaultRole = null };
        var services = new ServiceCollection()
            .AddSingleton<IKnowledgeSpaceService>(Spaces)
            .AddSingleton<IKnowledgePageService>(Pages)
            .AddSingleton<IPeopleDirectory>(People)
            .BuildServiceProvider();
        _handler = new KnowledgeActionHandler(services);
    }

    protected static JsonElement Payload(params object[] changes) => JsonSerializer.SerializeToElement(new { changes });

    protected Task<Result<PreparedAction>> PrepareAsync(JsonElement payload, Actor? actor = null) =>
        _handler.PrepareAsync(new ActionContext(actor ?? Member, null, null), payload, CancellationToken.None);

    protected async Task<PreparedAction> PrepareOkAsync(JsonElement payload, Actor? actor = null)
    {
        var result = await PrepareAsync(payload, actor);
        Assert.That(result.Failure, Is.Null, () => result.Failure!.Message + " " + string.Join("；", result.Failure!.Details.Select(d => $"{d.Path}:{d.Message}")));
        return result.Value;
    }

    protected Task<ActionStepResult> ApplyAsync(PreparedAction prepared, int index, Actor? actor = null) =>
        _handler.ApplyStepAsync(new ActionContext(actor ?? Member, null, null), prepared.State, index, new Dictionary<string, string>(), CancellationToken.None);

    protected static IReadOnlyList<string> IssuePaths(Result<PreparedAction> result) => [.. result.Failure!.Details.Select(d => d.Path)];
}
