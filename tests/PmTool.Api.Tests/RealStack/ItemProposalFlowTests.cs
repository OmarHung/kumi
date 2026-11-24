using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 修改建議從建立到套用，用真的工作引擎與資料庫：項目真的被改、活動記在套用的人名下、說明被別人改過就整份不套用、
/// 別人的建議找不到。建立走 AI 模組的 <see cref="IItemProposalCreator"/>（內建助理與 MCP 的工具都用它）。
/// </summary>
[TestFixture]
public sealed class ItemProposalFlowTests : RealStackTestBase
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
    public async Task Apply_ChangesTheItemForRealAndTheActivityIsTheApplyingPersons()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "PRA", "simple_tasks");
        await RealStackSteps.CreateItemAsync(_owner.Client, "PRA", new { type = "task", title = "任務", description = "原本" });
        var proposal = await CreateAsync("PRA", "PRA-1", ["title:新標題", "priority:high", "description:新說明", "status:doing"], "進行中了");

        // Act
        var applied = await ApplyAsync(proposal);
        var item = await RealStackSteps.GetAsync(_owner.Client, "/v1/items/PRA-1");
        var activity = await RealStackSteps.ActivityAsync(_owner.Client, "PRA-1");

        // Assert
        var entries = activity["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied["status"]!.GetValue<string>(), Is.EqualTo("applied"));
            Assert.That(applied["error"], Is.Null);
            Assert.That(item["title"]!.GetValue<string>(), Is.EqualTo("新標題"));
            Assert.That(item["description"]!.GetValue<string>(), Is.EqualTo("新說明"));
            Assert.That(item["priority"]!.GetValue<string>(), Is.EqualTo("high"));
            Assert.That(item["state"]!.GetValue<string>(), Is.EqualTo("doing"));
            Assert.That(entries.Select(e => e?["actor"]?["personId"]?.GetValue<Guid>()), Is.All.EqualTo(_owner.PersonId));
            Assert.That(entries.Select(e => e?["comment"]?.GetValue<string>()), Does.Contain("進行中了"));
        }
    }

    [Test]
    public async Task Apply_DescriptionChangedByAnotherPersonSinceTheProposal_Is409AndNothingChanges()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "PRB", "simple_tasks");
        await RealStackSteps.CreateItemAsync(_owner.Client, "PRB", new { type = "task", title = "任務", description = "原本" });
        var proposal = await CreateAsync("PRB", "PRB-1", ["title:新標題", "description:我的說明", "status:doing"], "留言");
        await RealStackSteps.PatchItemAsync(_owner.Client, "PRB-1", new { description = "別人改的" });

        // Act
        using var response = await _owner.Client.PostAsync($"/v1/ai/item-proposals/{proposal}/apply", null);
        var item = await RealStackSteps.GetAsync(_owner.Client, "/v1/items/PRB-1");
        var read = await RealStackSteps.GetAsync(_owner.Client, $"/v1/ai/item-proposals/{proposal}");
        var activity = await RealStackSteps.ActivityAsync(_owner.Client, "PRB-1");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.Conflict, WorkErrorCodes.ItemDescriptionConflict);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((item["title"]!.GetValue<string>(), item["description"]!.GetValue<string>(), item["state"]!.GetValue<string>()), Is.EqualTo(("任務", "別人改的", "todo")));
            Assert.That(read["status"]!.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(RealStackSteps.KindsOf(activity["items"]), Does.Not.Contain("commented"));
        }
    }

    [Test]
    public async Task Proposal_OfAnotherPerson_IsNotFoundForEveryoneElse()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "PRD", "simple_tasks");
        await RealStackSteps.CreateItemAsync(_owner.Client, "PRD", new { type = "task", title = "任務" });
        var proposal = await CreateAsync("PRD", "PRD-1", ["title:改名"], null);
        using var other = await AddMemberAsync(_owner, "PRD", "editor", "Editor Example", RealStackSteps.UniqueEmail("proposal-editor"));

        // Act
        using var read = await other.Client.GetAsync($"/v1/ai/item-proposals/{proposal}");
        using var apply = await other.Client.PostAsync($"/v1/ai/item-proposals/{proposal}/apply", null);
        using var dismiss = await other.Client.PostAsync($"/v1/ai/item-proposals/{proposal}/dismiss", null);
        var item = await RealStackSteps.GetAsync(_owner.Client, "/v1/items/PRD-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            await ApiRequests.AssertErrorEnvelopeAsync(read, HttpStatusCode.NotFound, AiErrorCodes.ItemProposalNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(apply, HttpStatusCode.NotFound, AiErrorCodes.ItemProposalNotFound);
            await ApiRequests.AssertErrorEnvelopeAsync(dismiss, HttpStatusCode.NotFound, AiErrorCodes.ItemProposalNotFound);
            Assert.That(item["title"]!.GetValue<string>(), Is.EqualTo("任務"));
        }
    }

    [Test]
    public async Task ApplyAndDismiss_AfterTheyWereDecided_Are409Closed()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "PRE", "simple_tasks");
        await RealStackSteps.CreateItemAsync(_owner.Client, "PRE", new { type = "task", title = "任務" });
        var applied = await CreateAsync("PRE", "PRE-1", ["title:改名"], null);
        var dismissed = await CreateAsync("PRE", "PRE-1", ["title:別的名字"], null);
        await ApplyAsync(applied);
        using var dismissRequest = await _owner.Client.PostAsync($"/v1/ai/item-proposals/{dismissed}/dismiss", null);

        // Act
        using var applyAgain = await _owner.Client.PostAsync($"/v1/ai/item-proposals/{applied}/apply", null);
        using var applyDismissed = await _owner.Client.PostAsync($"/v1/ai/item-proposals/{dismissed}/apply", null);
        var item = await RealStackSteps.GetAsync(_owner.Client, "/v1/items/PRE-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissRequest.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await ApiRequests.AssertErrorEnvelopeAsync(applyAgain, HttpStatusCode.Conflict, AiErrorCodes.ItemProposalClosed);
            await ApiRequests.AssertErrorEnvelopeAsync(applyDismissed, HttpStatusCode.Conflict, AiErrorCodes.ItemProposalClosed);
            Assert.That(item["title"]!.GetValue<string>(), Is.EqualTo("改名"));
        }
    }

    [Test]
    public async Task Create_ItemInAnotherProjectOrBadStatus_IsRefusedAndNothingIsStored()
    {
        // Arrange
        await RealStackSteps.CreateProjectAsync(_owner.Client, "PRF", "simple_tasks");
        await RealStackSteps.CreateProjectAsync(_owner.Client, "PRG", "simple_tasks");
        await RealStackSteps.CreateItemAsync(_owner.Client, "PRG", new { type = "task", title = "別的專案" });
        await RealStackSteps.CreateItemAsync(_owner.Client, "PRF", new { type = "task", title = "任務" });
        using var scope = Factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<IItemProposalCreator>();

        // Act
        var otherProject = await creator.CreateAsync(
            _actor, new ItemProposalDraft("PRF", "PRG-1", [Change("title:x")], null), CancellationToken.None);
        var badStatus = await creator.CreateAsync(
            _actor, new ItemProposalDraft("PRF", "PRF-1", [Change("status:nope")], null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(otherProject.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemNotFound));
            Assert.That(badStatus.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
        }
    }

    private async Task<Guid> CreateAsync(string projectKey, string itemKey, string[] changes, string? comment)
    {
        // 模型改說明時帶它讀項目時拿到的說明版本。
        var item = await RealStackSteps.GetAsync(_owner.Client, $"/v1/items/{itemKey}");
        using var scope = Factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IItemProposalCreator>().CreateAsync(
            _actor,
            new ItemProposalDraft(projectKey, itemKey, [.. changes.Select(Change)], comment)
            {
                DescriptionRevision = item["descriptionRevision"]!.GetValue<string>(),
            },
            CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }

    private async Task<System.Text.Json.Nodes.JsonNode> ApplyAsync(Guid proposalId)
    {
        using var response = await _owner.Client.PostAsync($"/v1/ai/item-proposals/{proposalId}/apply", null);
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK) ?? throw new AssertionException("套用的 data 是 null");
    }

    private static ItemFieldChange Change(string spec)
    {
        var index = spec.IndexOf(':', StringComparison.Ordinal);
        return new ItemFieldChange(spec[..index], JsonSerializer.SerializeToElement(spec[(index + 1)..]));
    }
}
