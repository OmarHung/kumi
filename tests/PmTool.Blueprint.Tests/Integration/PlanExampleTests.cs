using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Planning;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;

namespace PmTool.Blueprint.Tests.Integration;

/// <summary>
/// 範例檔走驗證再走計畫：「官網改版」的第 12 版藍圖，加上把驗收拆成兩個狀態的變更集。
/// 這是文件裡拿來說明整條管線的例子，所以用真的檔案，不用程式組出來的模型。
/// </summary>
[TestFixture]
public sealed class PlanExampleTests
{
    private static readonly BlueprintValidator Validator = BlueprintValidator.CreateDefault();

    [Test]
    public void Plan_SplitAcceptanceAgainstVersionTwelve_ListsTheSplitAndMovesTheItemsOutOfQa()
    {
        // Arrange
        var changeSet = ReadChangeSet(ExampleDocuments.ReadYaml(ExampleDocuments.ChangeSetFile));

        // Act
        var plan = Planner.Plan(WebsiteVersion12(), CurrentVersion, changeSet);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Is.Empty);
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new StateMoveImpact("main", "qa", "internal_qa") }));
            Assert.That(plan.Changes.DescribeWithPaths(), Is.EqualTo(new[]
            {
                "Added State main/internal_qa at /workflows/0/states/2",
                "Added State main/client_qa at /workflows/0/states/3",
                "Removed State main/qa from /workflows/0/states/2",
                "Added Transition main/doing->internal_qa at /workflows/0/transitions/1",
                "Added Transition main/internal_qa->doing at /workflows/0/transitions/2",
                "Added Transition main/internal_qa->client_qa at /workflows/0/transitions/3",
                "Added Transition main/client_qa->doing at /workflows/0/transitions/4",
                "Added Transition main/client_qa->done at /workflows/0/transitions/5",
                "Removed Transition main/doing->qa from /workflows/0/transitions/1",
                "Removed Transition main/qa->doing from /workflows/0/transitions/2",
                "Removed Transition main/qa->done from /workflows/0/transitions/3",
                "Added Automation mr_merged_to_internal_qa at /automations/0",
                "Removed Automation mr_merged_to_qa from /automations/0",
            }));
        }
    }

    [Test]
    public void Plan_SplitAcceptanceWithoutItsMigrations_IsBlockedBecauseQaItemsHaveNowhereToGo()
    {
        // Arrange：同一份變更集，拿掉 migrations。它仍然通過驗證，問題要到計畫這一步才看得出來。
        var tree = ExampleDocuments.ReadTree(ExampleDocuments.ChangeSetFile);
        tree.Remove("migrations");
        var changeSet = ReadChangeSet(ExampleDocuments.ToYaml(tree));

        // Act
        var plan = Planner.Plan(WebsiteVersion12(), CurrentVersion, changeSet);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanStateMoveRequired, "/migrations/moveStates"),
        }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues[0].Message, Does.Contain("「主要流程」").And.Contain("「驗收」"));
            Assert.That(plan.DataImpacts, Is.Empty);
        }
    }

    [Test]
    public void Plan_SplitAcceptanceAgainstAnotherVersion_IsBlocked()
    {
        // Arrange：變更集是照第 12 版寫的，但專案已經到第 13 版。
        var changeSet = ReadChangeSet(ExampleDocuments.ReadYaml(ExampleDocuments.ChangeSetFile));

        // Act
        var plan = Planner.Plan(WebsiteVersion12(), currentVersion: 13, changeSet);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanBaseVersionMismatch, "/baseVersion"),
        }));
    }

    [Test]
    public void SampleBlueprints_BuiltFromRecords_AreTheSameAsTheExampleFiles()
    {
        // Arrange：測試裡用 record 組的兩份範例，和範例檔是同一份內容的兩個來源；這裡確認它們沒有走樣。
        var agencyFile = Validator.ValidateBlueprint(ExampleDocuments.ReadYaml(ExampleDocuments.BlueprintFile), DocumentFormat.Yaml);
        var changeSetFile = ReadChangeSet(ExampleDocuments.ReadYaml(ExampleDocuments.ChangeSetFile));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonSerializer.Serialize(SampleBlueprints.Agency()), Is.EqualTo(JsonSerializer.Serialize(agencyFile.Blueprint)));
            Assert.That(JsonSerializer.Serialize(SampleBlueprints.SplitAcceptance()), Is.EqualTo(JsonSerializer.Serialize(changeSetFile)));
        }
    }

    private static ChangeSetRequest ReadChangeSet(string yaml)
    {
        var result = Validator.ValidateChangeSet(yaml, DocumentFormat.Yaml);
        Assert.That(result.Report.Issues, Is.Empty, "變更集要先通過驗證才能做計畫");
        return result.ChangeSet!;
    }
}
