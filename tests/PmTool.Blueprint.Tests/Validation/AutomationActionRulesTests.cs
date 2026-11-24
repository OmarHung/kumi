using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class AutomationActionRulesTests
{
    private static readonly string[] InvalidUrls =
    [
        "", "not a url", "/relative/path", "example.com/hook", "ftp://example.com/hook", "mailto:someone@example.com",
        "https://", "https://example.com/hook\n", " https://example.com/hook", "https://example.com/a b",
        "file:///etc/passwd",
    ];

    private static IEnumerable<TestCaseData> WellFormedActions()
    {
        yield return Named("transition", new AutomationAction { Type = ActionType.Transition, To = "done" });
        yield return Named("set_field", new AutomationAction { Type = ActionType.SetField, Field = "impact", Value = "high" });
        yield return Named("set_field empty value", new AutomationAction { Type = ActionType.SetField, Field = "change_reason", Value = "" });
        yield return Named("notify", new AutomationAction { Type = ActionType.Notify, Message = "已轉到內部驗收" });
        yield return Named("webhook https", new AutomationAction { Type = ActionType.Webhook, Url = "https://hooks.example.com/pm?x=1" });
        yield return Named("webhook http", new AutomationAction { Type = ActionType.Webhook, Url = "http://localhost:8080/hook" });
    }

    private static IEnumerable<TestCaseData> ActionsMissingTheirParameter()
    {
        yield return Named("transition", new AutomationAction { Type = ActionType.Transition });
        yield return Named("set_field without value", new AutomationAction { Type = ActionType.SetField, Field = "impact" });
        yield return Named("set_field without field", new AutomationAction { Type = ActionType.SetField, Value = "high" });
        yield return Named("notify", new AutomationAction { Type = ActionType.Notify });
        yield return Named("webhook", new AutomationAction { Type = ActionType.Webhook });
    }

    [TestCaseSource(nameof(WellFormedActions))]
    public void Validate_ActionWithExactlyItsOwnParameters_ReportsNoIssues(AutomationAction action)
    {
        // Arrange
        var blueprint = Agency().WithAction(0, action);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [TestCaseSource(nameof(ActionsMissingTheirParameter))]
    public void Validate_ActionMissingAParameter_ReportsAutomationActionParametersAtTheAction(AutomationAction action)
    {
        // Arrange
        var blueprint = Agency().WithAction(0, action);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationActionParameters, "/automations/0/actions/0") }));
    }

    [Test]
    public void Validate_NotifyWithAnotherActionsParameter_ReportsAutomationActionParametersAtThatParameter()
    {
        // Arrange
        var action = new AutomationAction { Type = ActionType.Notify, Message = "hi", Url = "https://hooks.example.com" };
        var blueprint = Agency().WithAction(0, action);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationActionParameters, "/automations/0/actions/0/url") }));
            Assert.That(issues[0].Message, Does.Contain("url"));
        }
    }

    [Test]
    public void Validate_ActionWithEveryParameterWrong_ReportsThemInPropertyOrder()
    {
        // Arrange：缺 to，又多了 field、value、url。
        var action = new AutomationAction { Type = ActionType.Transition, Field = "impact", Value = "high", Url = "https://example.com" };
        var blueprint = Agency().WithAction(0, action);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.AutomationActionParameters, "/automations/0/actions/0"),
            (IssueCodes.AutomationActionParameters, "/automations/0/actions/0/field"),
            (IssueCodes.AutomationActionParameters, "/automations/0/actions/0/value"),
            (IssueCodes.AutomationActionParameters, "/automations/0/actions/0/url"),
        }));
    }

    [Test]
    public void Validate_TransitionToUnknownState_ReportsAutomationUnknownState()
    {
        // Arrange
        var blueprint = Agency().WithAction(0, new AutomationAction { Type = ActionType.Transition, To = "ghost" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationUnknownState, "/automations/0/actions/0/to") }));
            Assert.That(issues[0].Message, Does.Contain("「ghost」"));
        }
    }

    [TestCase("ghost")]
    [TestCase("priority")]
    [TestCase("state")]
    public void Validate_SetFieldOnFieldThatIsNotACustomField_ReportsAutomationUnknownField(string field)
    {
        // Arrange：核心欄位有自己的規則（狀態要走流程的轉換），SetField 只能改自訂欄位。
        var blueprint = Agency().WithAction(0, new AutomationAction { Type = ActionType.SetField, Field = field, Value = "x" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationUnknownField, "/automations/0/actions/0/field") }));
            Assert.That(issues[0].Message, Does.Contain($"「{field}」"));
        }
    }

    [TestCaseSource(nameof(InvalidUrls))]
    public void Validate_WebhookWithUrlThatIsNotAbsoluteHttp_ReportsAutomationInvalidUrl(string url)
    {
        // Arrange
        var blueprint = Agency().WithAction(0, new AutomationAction { Type = ActionType.Webhook, Url = url });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationInvalidUrl, "/automations/0/actions/0/url") }));
    }

    [Test]
    public void Validate_WebhookWithVeryLongInvalidUrl_ReportsInvalidUrlWithACutMessage()
    {
        // Arrange：長度本身不是問題（.NET 的 Uri 不限長度），這裡要確認的是訊息裡的網址會被截斷。
        var url = "ftp://example.com/" + new string('a', 100_000);
        var blueprint = Agency().WithAction(0, new AutomationAction { Type = ActionType.Webhook, Url = url });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationInvalidUrl, "/automations/0/actions/0/url") }));
            Assert.That(issues[0].Message, Has.Length.LessThan(IssueText.MaxQuotedLength * 4));
        }
    }

    [Test]
    public void Validate_ParameterThatBelongsToAnotherAction_IsNotCheckedForItsValue()
    {
        // Arrange：notify 帶了 to，只回報參數不對，不再檢查 to 指的狀態存不存在。
        var action = new AutomationAction { Type = ActionType.Notify, Message = "hi", To = "ghost" };
        var blueprint = Agency().WithAction(0, action);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.AutomationActionParameters, "/automations/0/actions/0/to") }));
    }

    private static TestCaseData Named(string name, AutomationAction action) =>
        new TestCaseData(action).SetArgDisplayNames(name);
}
