using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Tests.NewItems;

/// <summary>
/// 新增項目建議測試的共同環境：助理的環境加上一個有成員的專案。藍圖有三種類型：
/// epic（只能在最上層）、task（可以掛在 epic 底下，有自訂欄位 category）、bug（只能在最上層）。
/// </summary>
public abstract class NewItemProposalTestBase
{
    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal INewItemProposalCreator Creator => _h.Host.Get<INewItemProposalCreator>();

    internal INewItemProposalService Proposals => _h.Host.Get<INewItemProposalService>();

    protected Actor Me => _h.Actor;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync();
        _h.Projects.OnGet = (_, _) => Detail();
        _h.WorkItems.Items["WEB-12"] = ItemSamples.Item() with { Type = "epic" };
        _h.WorkItems.Items["WEB-20"] = ItemSamples.Item("WEB-20", "一個任務") with { Type = "task" };
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    protected static ProjectDetail Detail(IReadOnlyList<string>? permissions = null) =>
        ItemSamples.DetailWithMember([.. permissions ?? ItemSamples.EditorPermissions, WorkPermissions.ItemCreate]) with { Blueprint = Blueprint() };

    protected static ProjectBlueprint Blueprint() => WorkSamples.Blueprint with
    {
        Fields = [new FieldDefinition { Key = "category", Name = "分類", Type = FieldType.Text }],
        ItemTypes =
        [
            new ItemType { Key = "epic", Name = "史詩", Workflow = "flow" },
            new ItemType { Key = "task", Name = "任務", Workflow = "flow", Fields = ["category"], Parents = ["epic"] },
            new ItemType { Key = "bug", Name = "錯誤", Workflow = "flow" },
        ],
    };

    protected static NewItemDraft Epic(string title = "搬到 AWS", string? reference = "e") => new("epic", title) { Ref = reference };

    protected static NewItemDraft Task(string title, string? parentRef = "e", string? parentKey = null) =>
        new("task", title) { ParentRef = parentRef, ParentKey = parentKey };

    protected static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    protected Task<Result<NewItemProposal>> CreateAsync(params NewItemDraft[] items) => CreateAsync(Me, items);

    protected Task<Result<NewItemProposal>> CreateAsync(Actor actor, params NewItemDraft[] items) =>
        Creator.CreateAsync(actor, new NewItemProposalDraft("WEB", items), CancellationToken.None);

    protected async Task<NewItemProposal> CreatedAsync(params NewItemDraft[] items)
    {
        var created = await CreateAsync(items);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message + string.Join("；", created.Failure?.Details.Select(d => d.Path + " " + d.Message) ?? []));
        return created.Value;
    }

    protected static void AssertFailure(Result<NewItemProposal> result, FailureKind kind, string code)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure?.Code, Is.EqualTo(code));
        }
    }

    /// <summary>檢查不過時，細項裡這個路徑的訊息。</summary>
    protected static string IssueAt(Result<NewItemProposal> result, string path) =>
        result.Failure!.Details.Single(d => d.Path == path).Message;
}
