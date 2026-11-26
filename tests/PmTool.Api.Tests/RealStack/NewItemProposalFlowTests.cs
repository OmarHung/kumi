using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 新增項目的建議從建立到套用，用真的工作引擎與資料庫：項目真的建立、parentRef 掛對、活動記在套用的人名下、
/// 別人的建議找不到、套用過不能再套用、部分失敗時已建立的留著。建立走 AI 模組的 <see cref="INewItemProposalCreator"/>
/// （內建助理與 MCP 的 propose_new_items 都用它）。用 scrum 模板：史詩 → 故事 → 任務。
/// </summary>
[TestFixture]
public sealed class NewItemProposalFlowTests : RealStackTestBase
{
    private SignedInClient _owner = null!;
    private Actor _actor = null!;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _owner = await SignInAsOwnerAsync();
        var session = await RealStackSteps.GetAsync(_owner.Client, "/v1/session");
        _actor = new Actor(session["user"]!["organizationId"]!.GetValue<Guid>(), _owner.PersonId, IsOrganizationAdmin: true);
    }

    [OneTimeTearDown]
    public void TearDown() => _owner.Dispose();

    [Test]
    public async Task Apply_CreatesEveryItemForRealWithParentsAndTheActivityIsTheApplyingPersons()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "NIA", "scrum");
        var proposal = await CreateAsync(
            "NIA",
            new NewItemDraft("epic", "搬遷") { Ref = "e", Priority = "high" },
            new NewItemDraft("story", "盤點") { Ref = "s", ParentRef = "e", AssigneeId = _owner.PersonId },
            new NewItemDraft("task", "列清單") { ParentRef = "s" });

        // Act
        var applied = await ApplyAsync(proposal);
        var keys = applied["createdKeys"]!.AsArray().Select(key => key!.GetValue<string>()).ToArray();
        var epic = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{keys[0]}");
        var story = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{keys[1]}");
        var task = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{keys[2]}");
        var activity = await RealStackSteps.ActivityAsync(_owner.Client, keys[1]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(applied["error"], Is.Null);
            Assert.That(keys, Is.EqualTo(new[] { "NIA-1", "NIA-2", "NIA-3" }));
            Assert.That(epic["priority"]!.GetValue<string>(), Is.EqualTo("high"));
            Assert.That(story["parentKey"]?.GetValue<string>(), Is.EqualTo(keys[0]));
            Assert.That(task["parentKey"]?.GetValue<string>(), Is.EqualTo(keys[1]));
            Assert.That(activity["items"]!.AsArray().Select(e => e?["actor"]?["personId"]?.GetValue<Guid>()), Is.All.EqualTo(_owner.PersonId));
        }
    }

    [Test]
    public async Task Create_TypeNotAllowedUnderTheParent_IsRefusedAndNothingIsStored()
    {
        // Arrange：任務不能直接掛在史詩底下。
        await RealStackSteps.CreateProjectAsync(_owner.Client, "NIB", "scrum");
        using var scope = Factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<INewItemProposalCreator>();

        // Act
        var result = await creator.CreateAsync(
            _actor,
            new NewItemProposalDraft("NIB", [new NewItemDraft("epic", "E") { Ref = "e" }, new NewItemDraft("task", "T") { ParentRef = "e" }]),
            CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(result.Failure?.Details?.Select(detail => detail.Path), Does.Contain("items[1].parentRef"));
        }
    }

    [Test]
    public async Task Proposal_OfAnotherPerson_IsNotFoundForEveryoneElse()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "NID", "scrum");
        var proposal = await CreateAsync("NID", new NewItemDraft("epic", "E"));
        using var other = await AddMemberAsync(_owner, "NID", "editor", "Editor Example", RealStackSteps.UniqueEmail("new-items-editor"));

        // Act
        using var read = await other.Client.GetAsync($"/v1/ai/new-item-proposals/{proposal}");
        using var apply = await other.Client.PostAsync($"/v1/ai/new-item-proposals/{proposal}/apply", null);
        using var dismiss = await other.Client.PostAsync($"/v1/ai/new-item-proposals/{proposal}/dismiss", null);
        var items = await RealStackSteps.GetAsync(_owner.Client, "/v1/projects/NID/items");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(read, HttpStatusCode.NotFound, AiErrorCodes.NewItemProposalNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(apply, HttpStatusCode.NotFound, AiErrorCodes.NewItemProposalNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(dismiss, HttpStatusCode.NotFound, AiErrorCodes.NewItemProposalNotFound);
            Assert.That(RealStackSteps.KeysOf(items["items"]), Is.Empty);
        }
    }

    [Test]
    public async Task ApplyAndDismiss_AfterTheyWereDecided_Are409Closed_AndTheItemsAreCreatedOnlyOnce()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "NIE", "scrum");
        var applied = await CreateAsync("NIE", new NewItemDraft("epic", "E"));
        var dismissed = await CreateAsync("NIE", new NewItemDraft("epic", "不要的"));
        await ApplyAsync(applied);
        using var dismissRequest = await _owner.Client.PostAsync($"/v1/ai/new-item-proposals/{dismissed}/dismiss", null);

        // Act
        using var applyAgain = await _owner.Client.PostAsync($"/v1/ai/new-item-proposals/{applied}/apply", null);
        using var applyDismissed = await _owner.Client.PostAsync($"/v1/ai/new-item-proposals/{dismissed}/apply", null);
        var items = await RealStackSteps.GetAsync(_owner.Client, "/v1/projects/NIE/items");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissRequest.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await ApiRequests.AssertErrorEnvelopeAsync(applyAgain, HttpStatusCode.Conflict, AiErrorCodes.NewItemProposalClosed);
            await ApiRequests.AssertErrorEnvelopeAsync(applyDismissed, HttpStatusCode.Conflict, AiErrorCodes.NewItemProposalClosed);
            Assert.That(RealStackSteps.KeysOf(items["items"]), Is.EqualTo(new[] { "NIE-1" }));
        }
    }

    private async Task<Guid> CreateAsync(string projectKey, params NewItemDraft[] items)
    {
        using var scope = Factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<INewItemProposalCreator>().CreateAsync(
            _actor, new NewItemProposalDraft(projectKey, items), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }

    private async Task<System.Text.Json.Nodes.JsonNode> ApplyAsync(Guid proposalId)
    {
        using var response = await _owner.Client.PostAsync($"/v1/ai/new-item-proposals/{proposalId}/apply", null);
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK) ?? throw new AssertionException("套用的 data 是 null");
    }
}
