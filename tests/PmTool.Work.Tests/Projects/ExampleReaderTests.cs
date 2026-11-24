using PmTool.Blueprint;
using PmTool.Work.Blueprints;
using PmTool.Work.Templates;

namespace PmTool.Work.Tests.Projects;

/// <summary>範例檔的讀取與驗證：直接測函式，不必內嵌壞檔。</summary>
[TestFixture]
public sealed class ExampleReaderTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly TemplateCatalog Catalog = new(BlueprintValidator.CreateDefault());

    private static ProjectSchema SchemaOf(string key) => ProjectSchema.From(Catalog.Find(key)!.Blueprint);

    private static ExampleReadResult Read(string yaml, string template = "agency_project") =>
        ExampleReader.Read(template, yaml, SchemaOf(template), Today);

    [Test]
    public void Read_ValidFile_ReturnsItemsInFileOrder()
    {
        // Arrange
        const string yaml = """
            items:
              - ref: req
                type: requirement
                title: 首頁改版
                state: designing
                priority: high
                startInDays: -2
                dueInDays: 14
                fields: { quote_amount: 80000 }
              - type: task
                title: 畫線框圖
                parent: req
            """;

        // Act
        var result = Read(yaml);

        // Assert
        Assert.That(result.Problems, Is.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(i => i.Title), Is.EqualTo(new[] { "首頁改版", "畫線框圖" }));
            Assert.That(result.Items[0].State, Is.EqualTo("designing"));
            Assert.That(result.Items[0].Priority, Is.EqualTo(Priority.High));
            Assert.That(result.Items[1].Parent, Is.EqualTo("req"));
        }
    }

    [Test]
    public void Read_RelativeDates_AreResolvedAgainstToday()
    {
        // Arrange
        const string yaml = """
            items:
              - type: requirement
                title: 改版
                startInDays: -2
                dueInDays: 14
                fields: { client_signoff_date: 7 }
            """;

        // Act
        var item = Read(yaml).Items.Single();
        var resolved = item.Resolve(SchemaOf("agency_project"), Today);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.StartDate, Is.EqualTo(new DateOnly(2026, 11, 16)));
            Assert.That(resolved.DueDate, Is.EqualTo(new DateOnly(2026, 12, 2)));
            Assert.That(resolved.Fields["client_signoff_date"].GetString(), Is.EqualTo("2026-11-25"));
        }
    }

    [TestCase("not: [valid", "YAML")]
    [TestCase("other: 1", "items")]
    [TestCase("items: []", "至少一筆")]
    [TestCase("items: 3", "items")]
    [TestCase("items:\n  - type: requirement\n    title: A\n    color: red", "color")]
    [TestCase("items:\n  - title: A", "type")]
    [TestCase("items:\n  - type: requirement", "title")]
    [TestCase("items:\n  - type: requirement\n    title: A\n    startInDays: soon", "startInDays")]
    public void Read_BadShape_ReportsTemplateAndWhere(string yaml, string expectedPart)
    {
        // Act
        var result = Read(yaml);

        // Assert
        Assert.That(result.Problems, Is.Not.Empty);
        Assert.That(result.Problems[0], Does.Contain("agency_project").And.Contain(expectedPart));
    }

    [Test]
    public void Read_UnknownType_NamesItemNumberAndField()
    {
        // Act
        var result = Read("items:\n  - type: requirement\n    title: A\n  - type: ghost\n    title: B");

        // Assert
        Assert.That(result.Problems, Has.Count.EqualTo(1));
        Assert.That(result.Problems[0], Does.Contain("agency_project").And.Contain("第 2 筆").And.Contain("type").And.Contain("ghost"));
    }

    [Test]
    public void Read_StateNotInWorkflow_Fails()
    {
        // Act（task 用任務流程，沒有 designing）
        var result = Read("items:\n  - type: task\n    title: A\n    state: designing");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("state").And.Contain("designing"));
    }

    [Test]
    public void Read_DuplicateRef_Fails()
    {
        // Act
        var result = Read("items:\n  - ref: a\n    type: requirement\n    title: A\n  - ref: a\n    type: requirement\n    title: B");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 2 筆").And.Contain("ref"));
    }

    [Test]
    public void Read_ParentDefinedLater_Fails()
    {
        // Act
        var result = Read("items:\n  - type: task\n    title: A\n    parent: later\n  - ref: later\n    type: requirement\n    title: B");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("parent").And.Contain("later"));
    }

    [Test]
    public void Read_ParentOfDisallowedType_Fails()
    {
        // Act（需求不能掛在任務底下）
        var result = Read("items:\n  - ref: t\n    type: task\n    title: A\n  - type: requirement\n    title: B\n    parent: t");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 2 筆").And.Contain("parent"));
    }

    [Test]
    public void Read_FieldNotOfThisType_Fails()
    {
        // Act（任務沒有報價金額）
        var result = Read("items:\n  - type: task\n    title: A\n    fields: { quote_amount: 1 }");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("quote_amount"));
    }

    [Test]
    public void Read_FieldValueOfWrongKind_Fails()
    {
        // Act
        var result = Read("items:\n  - type: change_request\n    title: A\n    fields: { impact: huge }");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("impact"));
    }

    [Test]
    public void Read_DateFieldNotAnInteger_Fails()
    {
        // Act
        var result = Read("items:\n  - type: requirement\n    title: A\n    fields: { client_signoff_date: 2026-11-30 }");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("client_signoff_date"));
    }

    [TestCase("-2147483648")]
    [TestCase("2147483647")]
    [TestCase("3651")]
    [TestCase("-3651")]
    public void Read_DaysOutOfRange_ReportsAProblemInsteadOfThrowing(string days)
    {
        // Act（int.MinValue 取絕對值會溢位；開始日、到期日和日期欄位都要擋）
        var start = Read($"items:\n  - type: requirement\n    title: A\n    startInDays: {days}");
        var due = Read($"items:\n  - type: requirement\n    title: A\n    dueInDays: {days}");
        var field = Read($"items:\n  - type: requirement\n    title: A\n    fields: {{ client_signoff_date: {days} }}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(start.Problems.Single(), Does.Contain("startInDays"));
            Assert.That(due.Problems.Single(), Does.Contain("dueInDays"));
            Assert.That(field.Problems.Single(), Does.Contain("client_signoff_date"));
        }
    }

    [Test]
    public void Read_NullForANonDateField_Fails()
    {
        // Act（null 在一般修改裡是「清掉」，範例是新建的，沒有東西可清，多半是寫錯）
        var result = Read("items:\n  - type: requirement\n    title: A\n    fields: { quote_amount: null }");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("quote_amount").And.Contain("null"));
    }

    [Test]
    public void Read_PersonField_IsNotAllowed()
    {
        // Act
        var result = Read("items:\n  - type: task\n    title: A\n    fields: { reviewer: someone }", "simple_tasks");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("reviewer").And.Contain("成員"));
    }

    [Test]
    public void Read_InvalidPriority_Fails()
    {
        // Act
        var result = Read("items:\n  - type: requirement\n    title: A\n    priority: critical");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("priority").And.Contain("critical"));
    }

    [Test]
    public void Read_TitleTooLong_Fails()
    {
        // Act
        var result = Read($"items:\n  - type: requirement\n    title: {new string('長', WorkRules.MaxTitleLength + 1)}");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("title"));
    }

    [Test]
    public void Read_ControlCharacterInDescription_Fails()
    {
        // Act（YAML 雙引號的 \u0001 是控制字元）
        var result = Read("items:\n  - type: requirement\n    title: A\n    description: \"壞\\u0001字\"");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("description"));
    }

    [Test]
    public void Read_DueBeforeStart_Fails()
    {
        // Act
        var result = Read("items:\n  - type: requirement\n    title: A\n    startInDays: 5\n    dueInDays: 1");

        // Assert
        Assert.That(result.Problems.Single(), Does.Contain("第 1 筆").And.Contain("dueInDays"));
    }

    [Test]
    public void Read_ManyProblems_ReportsAllOfThem()
    {
        // Act
        var result = Read("items:\n  - type: ghost\n    title: A\n  - type: task\n    title: B\n    state: nope");

        // Assert
        Assert.That(result.Problems, Has.Count.EqualTo(2));
    }
}
