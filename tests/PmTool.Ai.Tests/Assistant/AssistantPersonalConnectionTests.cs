using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.Settings;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>組織沒開 AI、但允許個人連線時，成員用自己的連線也能開對話、送訊息；兩者都關才是 ai.unavailable。</summary>
[TestFixture]
public sealed class AssistantPersonalConnectionTests
{
    private static async Task<(AssistantHarness Harness, AiConnection Mine)> StartAsync(bool allowPersonal)
    {
        var harness = await AssistantHarness.StartAsync(enabled: false);
        var admin = harness.Admin;
        await harness.Host.Get<IAiSettingsService>().SaveAsync(
            admin, new AiSettingsInput(false, null) { AllowPersonalConnections = true }, CancellationToken.None);
        var mine = await harness.Host.Get<IPersonalAiConnectionService>().CreateAsync(
            harness.Actor,
            new AiConnectionInput("我的", AiProviderKind.Anthropic, null, "sk-personal-0001", [ScriptedGateway.ModelId], []),
            CancellationToken.None);
        Assert.That(mine.Succeeded, Is.True, () => mine.Failure?.Message ?? string.Empty);
        await harness.Host.Get<IAiSettingsService>().SaveAsync(
            admin, new AiSettingsInput(false, null) { AllowPersonalConnections = allowPersonal }, CancellationToken.None);
        return (harness, mine.Value);
    }

    [Test]
    public async Task OrganizationOffPersonalAllowed_CreatesAConversationAndRepliesWithThePersonalConnection()
    {
        // Arrange
        var (harness, mine) = await StartAsync(allowPersonal: true);
        await using var _ = harness;
        harness.Script.Enqueue(Turn.Create().Text("好").Usage(10, 2).Finish(ChatFinishReason.Stop));

        // Act
        var conversation = await harness.NewConversationAsync();
        var reply = await harness.SendAsync(conversation.Id, selection: new AiSelection(mine.Id, ScriptedGateway.ModelId, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(reply.Text, Is.EqualTo("好"));
            Assert.That(reply.Final.ConnectionName, Is.EqualTo("我的"));
        }
    }

    [Test]
    public async Task OrganizationOffPersonalAllowed_OrganizationsConnectionStaysUnavailable()
    {
        // Arrange
        var (harness, _) = await StartAsync(allowPersonal: true);
        await using var _ = harness;
        var conversation = await harness.NewConversationAsync();

        // Act
        var started = await harness.Service.StartAsync(
            harness.Actor, AssistantHarness.ProjectKey, conversation.Id,
            new AssistantMessageInput("你好", harness.Selection), CancellationToken.None);

        // Assert
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.Unavailable));
    }

    [Test]
    public async Task BothOff_CreatingAConversationIsStillUnavailable()
    {
        // Arrange
        var (harness, _) = await StartAsync(allowPersonal: false);
        await using var _ = harness;

        // Act
        var created = await harness.Service.CreateAsync(harness.Actor, AssistantHarness.ProjectKey, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Unavailable));
        }
    }
}
