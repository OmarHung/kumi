using System.Collections;
using System.Diagnostics;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Validation;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Validation;

[TestFixture]
public sealed class IssueOrderAndLimitTests
{
    private const int Collected = ValidationReport.MaxIssues + 1;

    /// <summary>線性的實作在這個規模下只要幾十毫秒；平方級的寫法會超過很多。</summary>
    private static readonly TimeSpan LargeInputBudget = TimeSpan.FromSeconds(2);

    [Test]
    public void Validate_ProblemsFromSeveralRules_ReportsThemInRuleOrderNotDocumentOrder()
    {
        // Arrange：規則 1 的問題排在規則 2 前面，即使它在文件裡比較後面。
        var blueprint = (Agency() with
        {
            Fields = [.. Agency().Fields, TextField("due_date"), TextField("Bad")],
            Roles = [.. Agency().Roles, Role("admin")],
        }).WithView(ProgressBoard, v => v with { GroupBy = "ghost" });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.KeyFormat, "/fields/5/key"),
            (IssueCodes.FieldCoreNameConflict, "/fields/4/key"),
            (IssueCodes.RoleBuiltInConflict, "/roles/2/key"),
            (IssueCodes.ViewInvalidGroupBy, "/views/0/groupBy"),
        }));
    }

    [Test]
    public void Validate_MoreProblemsThanTheReportLimit_StopsAtOneMoreThanTheLimit()
    {
        // Arrange
        var blueprint = WithMalformedOptionKeys(Agency(), 300);

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(Collected));
            Assert.That(issues[^1].Path, Is.EqualTo($"/fields/3/options/{Collected - 1}/key"));
        }
    }

    [Test]
    public void Validate_LimitReachedEarly_NeverReadsTheRestOfTheDocument()
    {
        // Arrange：檢視排在選項後面，收集夠了就不該再讀。
        var blueprint = WithMalformedOptionKeys(Agency(), 300) with { Views = new UnreadableList<ViewDefinition>() };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues, Has.Count.EqualTo(Collected));
    }

    [Test]
    public void Validate_MoreProblemsAddedLater_KeepsTheEarlierIssuesUnchanged()
    {
        // Arrange
        var fewer = WithMalformedOptionKeys(Agency(), 150);
        var badViews = Enumerable.Range(0, 100).Select(i =>
            new ViewDefinition { Key = $"view_{i}", Name = "x", Type = ViewType.Board, GroupBy = "ghost" });
        var more = fewer with { Views = [.. badViews], Bindings = [] };

        // Act
        var fewerIssues = SemanticValidator.Validate(fewer);
        var moreIssues = SemanticValidator.Validate(more);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fewerIssues, Has.Count.EqualTo(150));
            Assert.That(moreIssues, Has.Count.EqualTo(Collected));
            Assert.That(moreIssues.Take(150), Is.EqualTo(fewerIssues));
        }
    }

    [Test]
    public void Validate_HugeProductOfMissingRequiredFields_StopsAtTheLimit()
    {
        // Arrange：8 個轉換 × 每個 2,000 個要求欄位 × 19 種類型，可能的問題有三十萬個。
        var extraFields = Enumerable.Range(0, BlueprintVocabulary.MaxFields - 4).Select(i => $"field_{i}").ToArray();
        var required = Enumerable.Range(0, 2_000).Select(i => extraFields[i % extraFields.Length]).ToArray();
        var extraTypes = Enumerable.Range(0, BlueprintVocabulary.MaxItemTypes - 3).Select(i => ItemTypeOf($"type_{i}", "delivery"));
        var blueprint = (Agency() with
        {
            Fields = [.. Agency().Fields, .. extraFields.Select(TextField)],
            ItemTypes = [.. Agency().ItemTypes, .. extraTypes],
        }).WithWorkflow(Delivery, w => w with
        {
            Transitions = [.. w.Transitions!.Select(t => t with { RequireFields = required })],
        });

        // Act
        var stopwatch = Stopwatch.StartNew();
        var issues = SemanticValidator.Validate(blueprint);
        stopwatch.Stop();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(Collected));
            Assert.That(issues.Select(i => i.Code), Is.All.EqualTo(IssueCodes.TransitionFieldMissingOnType));
            Assert.That(issues[0].Path, Is.EqualTo("/workflows/0/transitions/0/requireFields/0"));
            Assert.That(issues[0].Message, Does.Contain("「requirement」"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(LargeInputBudget));
        }
    }

    [Test]
    public void ValidateChangeSet_MoreProblemsThanTheReportLimit_StopsAtOneMoreThanTheLimit()
    {
        // Arrange
        var changeSet = SplitAcceptance().WithBlueprint(b => WithMalformedOptionKeys(
            b with { Fields = [new FieldDefinition { Key = "stage", Name = "階段", Type = FieldType.SingleSelect, Options = [] }] },
            300,
            field: 0));

        // Act
        var issues = SemanticValidator.ValidateChangeSet(changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues, Has.Count.EqualTo(Collected));
            Assert.That(issues.Select(i => i.Path), Is.All.StartsWith("/blueprint/"));
        }
    }

    [Test]
    public void Validate_DenseWorkflowWithManyTypes_FinishesQuicklyWithOnlyTheLimitIssues()
    {
        // Arrange：150 個狀態兩兩互通（22,350 個轉換），每個轉換都要求同一個欄位，500 種類型都有它。
        const int stateCount = 150;
        var states = Enumerable.Range(0, stateCount)
            .Select(i => State($"s_{i}", i == 0 ? StateCategory.NotStarted : StateCategory.Done));
        var transitions =
            from source in Enumerable.Range(0, stateCount)
            from target in Enumerable.Range(0, stateCount)
            where source != target
            select Transition($"s_{source}", $"s_{target}") with { RequireFields = ["impact"] };
        var big = new Workflow { Key = "big", Name = "big", States = [.. states], Transitions = [.. transitions] };
        var types = Enumerable.Range(0, 500).Select(i => ItemTypeOf($"type_{i}", "big") with { Fields = ["impact"] });
        var blueprint = Agency() with { Workflows = [.. Agency().Workflows, big], ItemTypes = [.. Agency().ItemTypes, .. types] };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var issues = SemanticValidator.Validate(blueprint);
        stopwatch.Stop();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.LimitExceeded, "/workflows/2/states"),
                (IssueCodes.LimitExceeded, "/itemTypes"),
            }));
            Assert.That(stopwatch.Elapsed, Is.LessThan(LargeInputBudget));
        }
    }

    [Test]
    public void Validate_VeryLongParentChainThatLoopsBack_ReportsTheCycleWithoutRecursing()
    {
        // Arrange：兩萬層的上層關係，最後一個指回第一個。遞迴的走訪會把堆疊用完。
        const int chain = 20_000;
        var types = Enumerable.Range(0, chain)
            .Select(i => ItemTypeOf($"chain_{i}", "task_flow") with { Parents = [$"chain_{(i + 1) % chain}"] });
        var blueprint = Agency() with { ItemTypes = [.. types] };

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.LimitExceeded, "/itemTypes"),
            (IssueCodes.ItemTypeParentCycle, $"/itemTypes/{chain - 1}/parents/0"),
            (IssueCodes.ViewUnknownItemType, "/views/1/filter/itemTypes/0"),
            (IssueCodes.ViewUnknownItemType, "/views/1/filter/itemTypes/1"),
            (IssueCodes.ViewUnknownItemType, "/views/2/filter/itemTypes/0"),
            (IssueCodes.ViewUnknownItemType, "/views/2/filter/itemTypes/1"),
        }));
    }

    [Test]
    public void Validate_VeryLongStateChain_ReachesEveryState()
    {
        // Arrange
        const int chain = 20_000;
        var states = Enumerable.Range(0, chain)
            .Select(i => State($"c_{i}", i == chain - 1 ? StateCategory.Done : StateCategory.InProgress));
        var transitions = Enumerable.Range(0, chain - 1).Select(i => Transition($"c_{i}", $"c_{i + 1}"));
        var blueprint = Agency().WithWorkflow(TaskFlow, w => w with { States = [.. states], Transitions = [.. transitions] });

        // Act
        var issues = SemanticValidator.Validate(blueprint);

        // Assert：只有數量超過上限，沒有走不到或沒有出路的狀態。
        Assert.That(issues.CodesAndPaths(), Is.EqualTo(new[] { (IssueCodes.LimitExceeded, "/workflows/1/states") }));
    }

    private static ProjectBlueprint WithMalformedOptionKeys(ProjectBlueprint blueprint, int count, int field = Impact)
    {
        var options = Enumerable.Range(0, count).Select(i => Option($"Bad_{i}"));
        return blueprint.WithField(field, f => f with { Options = [.. options] });
    }

    /// <summary>讀它就丟例外的清單，用來確認收集夠了之後真的停下來。</summary>
    private sealed class UnreadableList<T> : IReadOnlyList<T>
    {
        public int Count => 1;

        public T this[int index] => throw new InvalidOperationException("收集夠了還在讀文件。");

        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("收集夠了還在讀文件。");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
