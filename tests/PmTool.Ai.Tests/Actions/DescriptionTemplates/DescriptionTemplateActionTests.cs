using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Actions.DescriptionTemplates;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions.DescriptionTemplates;

/// <summary>說明格式動作建議：只有組織管理員能提、內建的不能動、建立時的檢查、套用時的服務呼叫。</summary>
[TestFixture]
public sealed class DescriptionTemplateActionTests
{
    private static readonly Actor Admin = new(Guid.NewGuid(), Guid.NewGuid(), IsOrganizationAdmin: true);
    private static readonly Actor Member = Admin with { IsOrganizationAdmin = false };

    private RecordingTemplates _templates = null!;
    private DescriptionTemplateActionHandler _handler = null!;

    [SetUp]
    public void CreateHandler()
    {
        _templates = new RecordingTemplates();
        _handler = new DescriptionTemplateActionHandler(new ServiceCollection().AddSingleton<IProjectDescriptionService>(_templates).BuildServiceProvider());
    }

    private Task<Result<PreparedAction>> PrepareAsync(Actor actor, params object[] changes) =>
        _handler.PrepareAsync(new ActionContext(actor, null, null), JsonSerializer.SerializeToElement(new { changes }), CancellationToken.None);

    private async Task<PreparedAction> PrepareOkAsync(params object[] changes)
    {
        var result = await PrepareAsync(Admin, changes);
        Assert.That(result.Failure, Is.Null, () => result.Failure!.Message + string.Join("；", result.Failure!.Details.Select(d => $"{d.Path}:{d.Message}")));
        return result.Value;
    }

    private Task<ActionStepResult> ApplyAsync(PreparedAction prepared, int index) =>
        _handler.ApplyStepAsync(new ActionContext(Admin, null, null), prepared.State, index, new Dictionary<string, string>(), CancellationToken.None);

    [Test]
    public async Task Prepare_ByANonAdmin_IsForbiddenAndSaysWhoCanDoIt()
    {
        // Arrange / Act
        var result = await PrepareAsync(Member, new { action = "create", name = "新格式", body = "## 內容" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure?.Message, Does.Contain("組織管理員"));
        }
    }

    [Test]
    public async Task Create_ShowsTheBodyAsMarkdownAndCreatesIt()
    {
        // Arrange / Act
        var prepared = await PrepareOkAsync(new { action = "create", name = "維運案", body = "## 主機\n- 位址：", isDefault = true });
        var applied = await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((step.Label, step.Target, step.After, step.Format), Is.EqualTo(("新增說明格式", "維運案", "## 主機\n- 位址：", ActionTextFormat.Markdown)));
            Assert.That(step.Note, Does.Contain("預設"));
            Assert.That(applied.Failure, Is.Null);
            Assert.That(_templates.Inputs.Single(), Is.EqualTo(new DescriptionTemplateInput("維運案", "## 主機\n- 位址：", true)));
        }
    }

    [Test]
    public async Task Update_KeepsTheValuesThatWereNotGiven_AndShowsTheBodyDifference()
    {
        // Arrange
        var own = RecordingTemplates.Own("舊名稱", "舊內容", isDefault: true);
        _templates.Templates.Insert(0, own);

        // Act
        var prepared = await PrepareOkAsync(new { action = "update", templateId = own.Id, body = "新內容" });
        await ApplyAsync(prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((step.Before, step.After), Is.EqualTo(("舊內容", "新內容")));
            Assert.That(_templates.Inputs.Single(), Is.EqualTo(new DescriptionTemplateInput("舊名稱", "新內容", true)));
            Assert.That(_templates.Calls.Single(), Is.EqualTo($"update:{own.Id}"));
        }
    }

    [Test]
    public async Task Update_AnUpperCaseTemplateId_IsStillFound()
    {
        // Arrange
        var own = RecordingTemplates.Own("舊名稱", "舊內容");
        _templates.Templates.Insert(0, own);

        // Act
        var result = await PrepareAsync(Admin, new { action = "update", templateId = own.Id.ToUpperInvariant(), name = "新名稱" });

        // Assert
        Assert.That(result.Failure, Is.Null);
    }

    [Test]
    public async Task Update_AppliesOnTopOfTheCurrentTemplate_SoSomeoneElsesLaterEditOfOtherFieldsSurvives()
    {
        // Arrange
        var own = RecordingTemplates.Own("舊名稱", "舊內容");
        _templates.Templates.Insert(0, own);
        var prepared = await PrepareOkAsync(new { action = "update", templateId = own.Id, name = "新名稱" });
        _templates.Templates[0] = own with { Body = "別人後來改的內容", IsDefault = true };

        // Act
        var applied = await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure, Is.Null);
            Assert.That(_templates.Inputs.Single(), Is.EqualTo(new DescriptionTemplateInput("新名稱", "別人後來改的內容", true)));
        }
    }

    [Test]
    public async Task Update_WhenTheTemplateWasDeletedMeanwhile_FailsTheStep()
    {
        // Arrange
        var own = RecordingTemplates.Own("舊名稱", "舊內容");
        _templates.Templates.Insert(0, own);
        var prepared = await PrepareOkAsync(new { action = "update", templateId = own.Id, body = "新內容" });
        _templates.Templates.Remove(own);

        // Act
        var applied = await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateNotFound));
            Assert.That(_templates.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Delete_TheDefaultOne_WarnsThatTheBuiltInBecomesTheDefault()
    {
        // Arrange
        var own = RecordingTemplates.Own("預設", "內容", isDefault: true);
        _templates.Templates.Insert(0, own);

        // Act
        var prepared = await PrepareOkAsync(new { action = "delete", templateId = own.Id });
        await ApplyAsync(prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Note, Does.Contain("內建"));
            Assert.That(_templates.Calls.Single(), Is.EqualTo($"delete:{Guid.Parse(own.Id)}"));
        }
    }

    [Test]
    public async Task Prepare_BuiltInTargetBadBodyAndUnknownProperty_AreAllReported()
    {
        // Arrange
        var changes = new object[]
        {
            new { action = "update", templateId = "web_project", name = "改內建" },
            new { action = "create", name = "沒內容", body = "  " },
            new { action = "create", name = "多餘", body = "x", color = "red" },
        };

        // Act
        var result = await PrepareAsync(Admin, changes);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.SupersetOf(
            new[] { "changes[0].templateId", "changes[1].body", "changes[2].color" }));
    }

    [Test]
    public async Task Prepare_ANameThatTheBuiltInOneAlreadyUsesIgnoringCase_IsRejected()
    {
        // Arrange / Act
        var result = await PrepareAsync(Admin, new { action = "create", name = "網站案子", body = "內容" });

        // Assert
        Assert.That(result.Failure?.Details.Single().Message, Does.Contain("不能重複"));
    }

    [Test]
    public async Task Prepare_ABodyWithAPrivateKey_IsRejectedBeforeAnythingIsStored()
    {
        // Arrange
        var body = "-----BEGIN PRIVATE KEY-----\nabc";

        // Act
        var result = await PrepareAsync(Admin, new { action = "create", name = "有鑰匙", body });

        // Assert
        Assert.That(result.Failure?.Details.Single().Path, Is.EqualTo("changes[0].body"));
    }

    [Test]
    public async Task Apply_WhenTheServiceRefuses_ReturnsTheFailureForTheStep()
    {
        // Arrange
        var prepared = await PrepareOkAsync(new { action = "create", name = "新", body = "內容" });
        _templates.FailWith = new Failure(FailureKind.Conflict, WorkErrorCodes.DescriptionTemplateLimitReached, "到上限了。");

        // Act
        var applied = await ApplyAsync(prepared, 0);

        // Assert
        Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.DescriptionTemplateLimitReached));
    }
}
