using PmTool.Ai.Actions;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Actions;

/// <summary>卡片輸入：必填、只傳給處理器、絕不存、不放進回應。</summary>
[TestFixture]
public sealed class ActionProposalInputTests : ActionProposalTestBase
{
    private const string Secret = "S3cret-初始密碼-ZZ9";

    [SetUp]
    public void DeclareInput() =>
        Handler.Inputs = [new ActionInput("password:0", "新同事的初始密碼", ActionInputKind.Password, null)];

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task Apply_WithMissingRequiredInput_IsFieldInvalidAndDoesNothing(string? value)
    {
        // Arrange
        var proposal = await CreatedAsync();
        var inputs = value is null ? NoInputs : new Dictionary<string, string> { ["password:0"] = value };

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, inputs, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(applied.Failure!.Details.Single().Path, Is.EqualTo("inputs.password:0"));
            Assert.That(Handler.Applied, Is.Empty);
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending), "還能再填一次");
        }
    }

    [Test]
    public async Task Apply_WithInput_PassesItToEveryStepButNeverStoresOrReturnsIt()
    {
        // Arrange
        var proposal = await CreatedAsync();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, new Dictionary<string, string> { ["password:0"] = Secret }, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        await using var db = await H.Host.CreateDbContextAsync();
        var row = await db.ActionProposals.AsNoTracking().SingleAsync(p => p.Id == proposal.Id);
        var stored = string.Join('|', row.Steps, row.State, row.Inputs, row.Title, row.Reason, row.Error);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Handler.Applied.Select(a => a.Inputs["password:0"]), Is.All.EqualTo(Secret));
            Assert.That(stored, Does.Not.Contain(Secret));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(applied.Value), Does.Not.Contain(Secret));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(read.Value), Does.Not.Contain(Secret));
            Assert.That(applied.Value.Inputs, Is.Empty, "套用之後不再要輸入");
        }
    }

    [Test]
    public async Task Apply_WhenFirstStepIsRefused_FailureDoesNotEchoTheInput()
    {
        // Arrange
        Handler.OnStep = _ => ActionStepResult.Failed(new Failure(FailureKind.Invalid, "x.bad", "密碼不合規定。"));
        var proposal = await CreatedAsync();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, new Dictionary<string, string> { ["password:0"] = Secret }, CancellationToken.None);

        // Assert
        Assert.That(System.Text.Json.JsonSerializer.Serialize(applied.Failure), Does.Not.Contain(Secret));
    }
}
