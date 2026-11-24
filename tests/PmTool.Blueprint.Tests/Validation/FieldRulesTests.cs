using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class FieldRulesTests
{
    private static readonly string[] CoreFieldKeys =
        ["title", "description", "state", "assignee", "priority", "start_date", "due_date", "estimate", "parent", "item_type"];

    // 活動紀錄與事件用 cycle、milestone 指稱項目上的週期與里程碑，自訂欄位不能撞名。
    private static readonly string[] PlanningFieldKeys = ["cycle", "milestone"];

    private static readonly string[] MalformedCurrencyCodes = ["", "twd", "TW", "TWDX", "TWD\n", "\nTWD", " TWD", "TW1", "ＴＷＤ"];

    private static readonly FieldType[] NonSelectTypes =
    [
        FieldType.Text, FieldType.LongText, FieldType.Number, FieldType.Currency,
        FieldType.Date, FieldType.Person, FieldType.Checkbox, FieldType.Url,
    ];

    [TestCaseSource(nameof(CoreFieldKeys))]
    public void Validate_CustomFieldNamedAfterCoreField_ReportsFieldCoreNameConflict(string key)
    {
        // Arrange
        var blueprint = Agency() with { Fields = [.. Agency().Fields, TextField(key)] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldCoreNameConflict, "/fields/4/key") }));
            Assert.That(issues[0].Message, Does.Contain($"「{key}」"));
        }
    }

    [TestCaseSource(nameof(PlanningFieldKeys))]
    public void Validate_CustomFieldNamedAfterPlanningField_ReportsFieldCoreNameConflict(string key)
    {
        // Arrange
        var blueprint = Agency() with { Fields = [.. Agency().Fields, TextField(key)] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldCoreNameConflict, "/fields/4/key") }));
    }

    [TestCase(FieldType.SingleSelect)]
    [TestCase(FieldType.MultiSelect)]
    public void Validate_SelectFieldWithoutOptions_ReportsFieldOptionsRequiredAtTheField(FieldType type)
    {
        // Arrange
        var field = new FieldDefinition { Key = "stage", Name = "階段", Type = type };
        var blueprint = Agency() with { Fields = [.. Agency().Fields, field] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldOptionsRequired, "/fields/4") }));
            Assert.That(issues[0].Message, Does.Contain("「stage」"));
        }
    }

    [TestCase(FieldType.SingleSelect)]
    [TestCase(FieldType.MultiSelect)]
    public void Validate_SelectFieldWithEmptyOptions_ReportsFieldOptionsRequiredAtTheOptions(FieldType type)
    {
        // Arrange
        var field = new FieldDefinition { Key = "stage", Name = "階段", Type = type, Options = [] };
        var blueprint = Agency() with { Fields = [.. Agency().Fields, field] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldOptionsRequired, "/fields/4/options") }));
    }

    [TestCaseSource(nameof(NonSelectTypes))]
    public void Validate_NonSelectFieldWithEmptyOptions_ReportsFieldOptionsNotAllowed(FieldType type)
    {
        // Arrange：寫了空清單也算寫了。
        var field = new FieldDefinition
        {
            Key = "extra",
            Name = "額外",
            Type = type,
            Options = [],
            Currency = type == FieldType.Currency ? "USD" : null,
        };
        var blueprint = Agency() with { Fields = [.. Agency().Fields, field] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldOptionsNotAllowed, "/fields/4/options") }));
            Assert.That(issues[0].Message, Does.Contain("「extra」"));
        }
    }

    [Test]
    public void Validate_NonSelectFieldWithOptions_ReportsFieldOptionsNotAllowed()
    {
        // Arrange
        var blueprint = Agency().WithField(ClientSignoffDate, f => f with { Options = [Option("soon")] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldOptionsNotAllowed, "/fields/1/options") }));
    }

    [Test]
    public void Validate_CurrencyFieldWithoutCurrency_ReportsFieldCurrencyRequired()
    {
        // Arrange
        var blueprint = Agency().WithField(QuoteAmount, f => f with { Currency = null });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldCurrencyRequired, "/fields/0") }));
            Assert.That(issues[0].Message, Does.Contain("「quote_amount」"));
        }
    }

    [Test]
    public void Validate_NonCurrencyFieldWithCurrency_ReportsFieldCurrencyNotAllowedOnly()
    {
        // Arrange：幣別本身也不合格式，但這個型別根本不該有幣別，只回報這一件事。
        var blueprint = Agency().WithField(ClientSignoffDate, f => f with { Currency = "twd" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldCurrencyNotAllowed, "/fields/1/currency") }));
            Assert.That(issues[0].Message, Does.Contain("「client_signoff_date」"));
        }
    }

    [TestCaseSource(nameof(MalformedCurrencyCodes))]
    public void Validate_MalformedCurrencyCode_ReportsFieldCurrencyCode(string code)
    {
        // Arrange
        var blueprint = Agency().WithField(QuoteAmount, f => f with { Currency = code });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.FieldCurrencyCode, "/fields/0/currency") }));
    }

    [TestCase("USD")]
    [TestCase("JPY")]
    public void Validate_WellFormedCurrencyCode_ReportsNoIssues(string code)
    {
        // Arrange
        var blueprint = Agency().WithField(QuoteAmount, f => f with { Currency = code });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_MalformedCurrencyCode_MessageQuotesTheCode()
    {
        // Arrange
        var blueprint = Agency().WithField(QuoteAmount, f => f with { Currency = "dollars" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.Single().Message, Does.Contain("「dollars」"));
    }

    [Test]
    public void Validate_FieldWithOptionAndCurrencyProblems_ReportsOptionsBeforeCurrency()
    {
        // Arrange
        var blueprint = Agency().WithField(ChangeReason, f => f with { Options = [], Currency = "TWD" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.FieldOptionsNotAllowed, "/fields/2/options"),
            (IssueCodes.FieldCurrencyNotAllowed, "/fields/2/currency"),
        }));
    }
}
