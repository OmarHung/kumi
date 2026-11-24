using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Planning;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>
/// 計畫對任何輸入都不該丟例外，同樣的輸入每次都要得到同樣的結果。
/// 這裡用隨機改出來的一大批合法藍圖兩兩配對去試，再加上幾份過不了驗證的藍圖。種子固定，失敗可以重現。
/// </summary>
[TestFixture]
public sealed class PlanFuzzTests
{
    private const int Seed = 20261113;
    private const int MutatedDocumentsPerExample = 1_500;
    private const int Pairs = 3_000;

    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    // 產生這一批要驗證幾千份文件，兩個測試共用同一批。
    private static readonly Lazy<List<ProjectBlueprint>> Pool = new(() => ValidBlueprints(new Random(Seed)));

    [Test]
    public void Plan_PairsOfValidBlueprints_NeverThrowAndGiveTheSameResultEveryTime()
    {
        // Arrange
        var random = new Random(Seed + 1);
        var pool = Pool.Value;
        var problems = new List<string>();
        var (withChanges, withImpacts, withIssues) = (0, 0, 0);

        // Act
        for (var round = 0; round < Pairs && problems.Count < 3; round++)
        {
            var current = pool[random.Next(pool.Count)];
            var next = pool[random.Next(pool.Count)];
            var changeSet = new ChangeSetRequest
            {
                BaseVersion = CurrentVersion,
                Blueprint = next,
                Migrations = RandomHints(current, next, random),
            };
            try
            {
                var plan = Planner.Plan(current, CurrentVersion, changeSet);
                withChanges += plan.Changes.Count > 0 ? 1 : 0;
                withImpacts += plan.DataImpacts.Count > 0 ? 1 : 0;
                withIssues += plan.Report.IsValid ? 0 : 1;
                if (plan.Print() != Planner.Plan(current, CurrentVersion, changeSet).Print())
                {
                    problems.Add($"第 {round} 對：同樣的輸入算出不同的計畫");
                }

                if (StatesLeftWithoutAHome(current, next, plan) is { Length: > 0 } stranded)
                {
                    problems.Add($"第 {round} 對：{stranded} 的項目沒有地方待，計畫卻沒有列出來也沒有擋下");
                }
            }
            catch (Exception exception)
            {
                // 這裡就是要抓「任何例外」。
                problems.Add($"第 {round} 對：{exception.GetType().Name}：{exception.Message}");
            }
        }

        // Assert：沒有出狀況；而且這批輸入真的走到了差異、資料影響與擋下這三條路，不是在空轉。
        Assert.That(problems, Is.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool, Has.Count.GreaterThan(100), "合法的藍圖太少，測不出東西");
            Assert.That(withChanges, Is.GreaterThan(Pairs / 2));
            Assert.That(withImpacts, Is.GreaterThan(Pairs / 20));
            Assert.That(withIssues, Is.GreaterThan(Pairs / 20));
        }
    }

    [Test]
    public void Plan_EveryValidBlueprintAgainstAnEqualCopy_HasNoChanges()
    {
        // Act：對上的是另外建出來、內容相同的一份。對上同一個物件的話，比較在「是同一個參考」就結束了，測不到逐項比對。
        var plansWithChanges = Pool.Value
            .Select(blueprint => Planner.Plan(
                blueprint, CurrentVersion, new ChangeSetRequest { BaseVersion = CurrentVersion, Blueprint = Copy(blueprint) }))
            .Count(plan => plan.Changes.Count > 0 || plan.DataImpacts.Count > 0 || !plan.Report.IsValid);

        // Assert
        Assert.That(plansWithChanges, Is.Zero);
    }

    private static IEnumerable<TestCaseData> BlueprintsThatWouldNotPassValidation()
    {
        var agency = SampleBlueprints.Agency();
        yield return new TestCaseData(agency with { Fields = [.. agency.Fields, .. agency.Fields] })
            .SetArgDisplayNames("every field key twice");
        yield return new TestCaseData(agency with { Workflows = [.. agency.Workflows, .. agency.Workflows] })
            .SetArgDisplayNames("every workflow key twice");
        yield return new TestCaseData(agency with { ItemTypes = [.. agency.ItemTypes, .. agency.ItemTypes.Reverse()] })
            .SetArgDisplayNames("every item type key twice");
        yield return new TestCaseData(agency.WithItemType(0, type => type with { Workflow = "ghost" }))
            .SetArgDisplayNames("item type on a workflow that does not exist");
        yield return new TestCaseData(agency.WithWorkflow(0, workflow => workflow with { States = [], Transitions = [] }))
            .SetArgDisplayNames("workflow without states");
        yield return new TestCaseData(agency.WithWorkflow(0, workflow => workflow with
        {
            States = [.. workflow.States, .. workflow.States],
            Transitions = [.. workflow.Transitions!, .. workflow.Transitions!],
        })).SetArgDisplayNames("every state and transition twice");
        yield return new TestCaseData(agency.WithField(3, field => field with { Options = null }))
            .SetArgDisplayNames("select field without options");
        yield return new TestCaseData(agency with { Fields = [], Workflows = [], ItemTypes = [], Views = [], Roles = [], Bindings = [] })
            .SetArgDisplayNames("nearly empty blueprint");
    }

    [TestCaseSource(nameof(BlueprintsThatWouldNotPassValidation))]
    public void Plan_BlueprintThatWouldNotPassValidation_StillDoesNotThrow(ProjectBlueprint broken)
    {
        // Arrange：計畫的輸入照理已經通過驗證；萬一沒有，也只能算出一份不太有意義的計畫，不能丟例外。
        var agency = SampleBlueprints.Agency();
        var hints = RandomHints(agency, broken, new Random(Seed));

        // Act
        TestDelegate bothDirections = () =>
        {
            Planner.Plan(agency, CurrentVersion, new ChangeSetRequest { BaseVersion = CurrentVersion, Blueprint = broken, Migrations = hints });
            Planner.Plan(broken, CurrentVersion, new ChangeSetRequest { BaseVersion = CurrentVersion, Blueprint = agency, Migrations = hints });
            Planner.Plan(broken, CurrentVersion, new ChangeSetRequest { BaseVersion = CurrentVersion, Blueprint = broken });
        };

        // Assert
        Assert.That(bothDirections, Throws.Nothing);
    }

    [Test]
    public void Plan_MoreIssuesThanTheReportLimit_TruncatesTheReport()
    {
        // Arrange
        var agency = SampleBlueprints.Agency();
        var hints = new MigrationHints
        {
            DropFields = [.. Enumerable.Range(0, ValidationReport.MaxIssues + 50).Select(index => $"ghost_{index}")],
        };

        // Act
        var plan = Planner.Plan(
            agency, CurrentVersion, new ChangeSetRequest { BaseVersion = CurrentVersion, Blueprint = agency, Migrations = hints });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Has.Count.EqualTo(ValidationReport.MaxIssues));
            Assert.That(plan.Report.Truncated, Is.True);
            Assert.That(plan.Report.Issues[0].Path, Is.EqualTo("/migrations/dropFields/0"));
        }
    }

    /// <summary>兩個範例與第 12 版的藍圖，加上從它們隨機改出來、仍然通過驗證的藍圖。</summary>
    private static List<ProjectBlueprint> ValidBlueprints(Random random)
    {
        var sources = new (string File, Func<JsonObject, JsonNode> BlueprintOf)[]
        {
            (ExampleDocuments.BlueprintFile, tree => tree),
            (ExampleDocuments.CurrentBlueprintFile, tree => tree),
            (ExampleDocuments.ChangeSetFile, tree => tree["blueprint"]!),
        };
        var pool = new List<ProjectBlueprint>();
        foreach (var (file, blueprintOf) in sources)
        {
            for (var round = 0; round < MutatedDocumentsPerExample; round++)
            {
                var tree = ExampleDocuments.ReadTree(file);
                for (var mutation = random.Next(0, 3); mutation > 0; mutation--)
                {
                    TreeMutator.Mutate(tree, random);
                }

                if (tree["blueprint"] is null && file == ExampleDocuments.ChangeSetFile)
                {
                    continue;
                }

                var result = Validator.ValidateBlueprint(ExampleDocuments.ToJson(blueprintOf(tree)), DocumentFormat.Json);
                if (result.Blueprint is { } valid)
                {
                    pool.Add(valid);
                }
            }
        }

        return pool;
    }

    /// <summary>從兩份藍圖裡出現過的 key 隨機湊出搬移指示，有的用得到、有的用不到。</summary>
    private static MigrationHints RandomHints(ProjectBlueprint current, ProjectBlueprint next, Random random)
    {
        string[] workflows = [.. next.Workflows.Select(workflow => workflow.Key), "ghost"];
        string[] states = [.. current.Workflows.Concat(next.Workflows).SelectMany(workflow => workflow.States).Select(state => state.Key), "ghost"];
        string[] fields = [.. current.Fields.Concat(next.Fields).Select(field => field.Key), "ghost"];
        string Pick(string[] from) => from[random.Next(from.Length)];
        return new MigrationHints
        {
            MoveStates =
            [
                .. Enumerable.Range(0, random.Next(0, 6))
                    .Select(_ => new StateMove { Workflow = Pick(workflows), From = Pick(states), To = Pick(states) }),
            ],
            DropFields = [.. Enumerable.Range(0, random.Next(0, 4)).Select(_ => Pick(fields))],
            ConvertFields =
            [
                .. Enumerable.Range(0, random.Next(0, 4))
                    .Select(_ => new FieldConversion { Field = Pick(fields), OnFailure = ConversionFailurePolicy.LeaveEmpty }),
            ],
        };
    }

    private static ProjectBlueprint Copy(ProjectBlueprint blueprint) =>
        JsonSerializer.Deserialize<ProjectBlueprint>(JsonSerializer.Serialize(blueprint))!;

    /// <summary>
    /// 護欄本身的說法，直接對著結果檢查：既有的類型原本流程裡的每個狀態，只要不在它新的流程裡，
    /// 就要有一筆搬移，不然計畫就要因為缺搬移指示而被擋下。回傳沒被交代到的狀態；都有交代時是空字串。
    /// </summary>
    private static string StatesLeftWithoutAHome(ProjectBlueprint current, ProjectBlueprint next, BlueprintPlan plan)
    {
        var moved = plan.DataImpacts.OfType<StateMoveImpact>().Select(move => (move.Workflow, move.From)).ToHashSet();
        var missingHints = plan.Report.Issues.Count(issue => issue.Code == IssueCodes.PlanStateMoveRequired);
        var stranded = (
            from type in next.ItemTypes
            join before in current.ItemTypes on type.Key equals before.Key
            from state in current.Workflows.Single(workflow => workflow.Key == before.Workflow).States
            where next.Workflows.Single(workflow => workflow.Key == type.Workflow).States.All(kept => kept.Key != state.Key)
            select (type.Workflow, state.Key)).Distinct().Where(pair => !moved.Contains(pair)).ToArray();
        return stranded.Length == missingHints ? string.Empty : string.Join("、", stranded);
    }
}
