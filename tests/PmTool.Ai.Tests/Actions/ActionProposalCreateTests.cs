using PmTool.Ai.Actions;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions;

/// <summary>建立動作建議：依種類找處理器、專案要看得到、理由、步數、輸入宣告。</summary>
[TestFixture]
public sealed class ActionProposalCreateTests : ActionProposalTestBase
{
    [Test]
    public async Task Create_WithKnownKind_StoresPendingProposalWithStepsAndTitle()
    {
        // Arrange
        Handler.StepCount = 2;

        // Act
        var created = await Creator.CreateAsync(Me, Draft() with { Reason = "整理一下" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
            Assert.That(created.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(created.Value.Kind, Is.EqualTo(FakeActionHandler.TestKind));
            Assert.That(created.Value.Title, Is.EqualTo("建議調整"));
            Assert.That(created.Value.Reason, Is.EqualTo("整理一下"));
            Assert.That(created.Value.Steps, Has.Count.EqualTo(2));
            Assert.That(created.Value.Steps.Select(s => s.Done), Is.All.False);
        }
    }

    [Test]
    public async Task Create_ThenGet_ReadsTheSameProposalIncludingInputDeclarations()
    {
        // Arrange
        Handler.Inputs = [new ActionInput("password:0", "新同事的初始密碼", ActionInputKind.Password, "至少 12 個字")];
        var created = await CreatedAsync();

        // Act
        var read = await Proposals.GetAsync(Me, created.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Succeeded, Is.True);
            Assert.That(read.Value.Inputs, Is.EqualTo(new[] { new ActionInput("password:0", "新同事的初始密碼", ActionInputKind.Password, "至少 12 個字") }));
            Assert.That(read.Value.Steps[0].Label, Is.EqualTo("第 1 步"));
        }
    }

    [Test]
    public async Task Create_WithUnknownKind_IsFieldInvalid()
    {
        // Act
        var created = await Creator.CreateAsync(Me, Draft("nope"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.False);
            Assert.That(created.Failure!.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
        }
    }

    [Test]
    public async Task Create_WithPayloadThatIsNotAnObject_IsFieldInvalid()
    {
        // Act
        var created = await Creator.CreateAsync(Me, new ActionProposalDraft(FakeActionHandler.TestKind, null, JsonSerializer.SerializeToElement(new[] { 1 })), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
    }

    [Test]
    public async Task Create_WithProjectTheActorCannotSee_ReturnsTheProjectFailureAndStoresNothing()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, "project.not_found", "找不到專案。");

        // Act
        var created = await Creator.CreateAsync(Me, Draft(projectKey: "WEB"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo("project.not_found"));
            Assert.That(Handler.Applied, Is.Empty);
        }
    }

    [Test]
    public async Task Create_WithVisibleProject_PassesTheProjectKeyToTheHandler()
    {
        // Arrange
        ActionContext? seen = null;
        Handler.OnPrepare = context =>
        {
            seen = context;
            return Unit.Value;
        };
        var conversationId = Guid.NewGuid();

        // Act
        var created = await Creator.CreateAsync(Me, Draft(projectKey: "web") with { ConversationId = conversationId }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True);
            Assert.That(seen!.ProjectKey, Is.EqualTo("WEB"), "用專案回的正式代號");
            Assert.That(seen.ConversationId, Is.EqualTo(conversationId));
            Assert.That(seen.Actor, Is.EqualTo(Me));
            Assert.That(created.Value.ProjectKey, Is.EqualTo("WEB"));
        }
    }

    [Test]
    public async Task Create_WhenHandlerRejects_ReturnsItsFailureAndStoresNothing()
    {
        // Arrange
        Handler.OnPrepare = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "只有組織管理員能做這件事。");

        // Act
        var created = await Creator.CreateAsync(Me, Draft(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(created.Failure?.Message, Does.Contain("組織管理員"));
        }
    }

    [TestCase(0)]
    [TestCase(21)]
    public async Task Create_WithStepCountOutsideTheLimit_IsFieldInvalid(int count)
    {
        // Arrange
        Handler.StepCount = count;

        // Act
        var created = await Creator.CreateAsync(Me, Draft(), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
    }

    [Test]
    public async Task Create_WithMaximumStepCount_Succeeds()
    {
        // Arrange
        Handler.StepCount = AiRules.MaxActionSteps;

        // Act
        var created = await Creator.CreateAsync(Me, Draft(), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
    }

    [TestCase("第一行\n第二行")]
    [TestCase("有\u0000控制字元")]
    public async Task Create_WithReasonThatIsNotOneCleanLine_IsFieldInvalid(string reason)
    {
        // Act
        var created = await Creator.CreateAsync(Me, Draft() with { Reason = reason }, CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Details.Single().Path, Is.EqualTo("reason"));
    }

    [Test]
    public async Task Create_WithReasonOver200Characters_IsFieldInvalid()
    {
        // Act
        var created = await Creator.CreateAsync(Me, Draft() with { Reason = new string('字', 201) }, CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Details.Single().Path, Is.EqualTo("reason"));
    }

    [Test]
    public async Task Create_WithBlankReason_StoresNone()
    {
        // Act
        var created = await Creator.CreateAsync(Me, Draft() with { Reason = "   " }, CancellationToken.None);

        // Assert
        Assert.That(created.Value.Reason, Is.Null);
    }
}
