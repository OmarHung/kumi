using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Blueprint.Tests.Validation;
using static PmTool.Blueprint.Tests.Planning.PlanTestSupport;
using static PmTool.Blueprint.Tests.Validation.SampleBlueprints;

namespace PmTool.Blueprint.Tests.Planning;

/// <summary>護欄：拿掉欄位要確認封存；改欄位型別只允許有定義的轉換，可能轉不過去的要寫明怎麼處理。</summary>
[TestFixture]
public sealed class PlanFieldMigrationTests
{
    private const int ProbeIndex = 4;

    [Test]
    public void Plan_RemovedFieldListedInDropFields_BecomesAFieldDrop()
    {
        // Act
        var plan = PlanFrom(Agency(), WithoutChangeReason(), Drops("change_reason"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new FieldDropImpact("change_reason") }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_RemovedFieldNotListedInDropFields_IsBlockedAndNamesTheField()
    {
        // Act
        var plan = PlanFrom(Agency(), WithoutChangeReason());

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanFieldDropRequired, "/migrations/dropFields"),
        }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues[0].Message, Does.Contain("「變更原因」").And.Not.Contain("change_reason"));
            Assert.That(plan.DataImpacts, Is.Empty);
        }
    }

    [Test]
    public void Plan_RemovedFieldWithoutAName_IsNamedByItsKey()
    {
        // Arrange：名稱是空的時候，訊息裡至少要有 key，看得出是哪一個欄位。
        var current = Agency().WithField(ChangeReason, field => field with { Name = string.Empty });

        // Act
        var plan = PlanFrom(current, WithoutChangeReason());

        // Assert
        Assert.That(plan.Report.Issues.Single().Message, Does.Contain("「change_reason」"));
    }

    [Test]
    public void Plan_DropFieldsEntryThatIsNotNeeded_IsBlocked()
    {
        // Arrange：ghost 從來不是這份藍圖的欄位；change_reason 列了兩次。
        var hints = Drops("ghost", "change_reason", "change_reason");

        // Act
        var plan = PlanFrom(Agency(), WithoutChangeReason(), hints);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new FieldDropImpact("change_reason") }));
            Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.PlanUnusedMigration, "/migrations/dropFields/0"),
                (IssueCodes.PlanUnusedMigration, "/migrations/dropFields/2"),
            }));
        }
    }

    [TestCase(FieldType.SingleSelect, FieldType.MultiSelect)]
    [TestCase(FieldType.Text, FieldType.LongText)]
    [TestCase(FieldType.Number, FieldType.Text)]
    [TestCase(FieldType.Date, FieldType.Text)]
    [TestCase(FieldType.Url, FieldType.Text)]
    public void Plan_ConversionThatCannotLoseData_NeedsNoHint(FieldType from, FieldType to)
    {
        // Act
        var plan = PlanFrom(WithProbe(from), WithProbe(to));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new FieldConversionImpact("probe", from, to, OnFailure: null) }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [TestCase(FieldType.Text, FieldType.Number)]
    [TestCase(FieldType.Text, FieldType.Date)]
    [TestCase(FieldType.MultiSelect, FieldType.SingleSelect)]
    [TestCase(FieldType.LongText, FieldType.Text)]
    public void Plan_ConversionThatMayFail_NeedsAFailurePolicy(FieldType from, FieldType to)
    {
        // Act
        var withoutHint = PlanFrom(WithProbe(from), WithProbe(to));
        var withHint = PlanFrom(WithProbe(from), WithProbe(to), Converts(("probe", ConversionFailurePolicy.Abort)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutHint.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.PlanFieldConversionRequired, "/migrations/convertFields"),
            }));
            Assert.That(withoutHint.Report.Issues[0].Message, Does.Contain("「試驗」"));
            Assert.That(withoutHint.DataImpacts, Is.Empty);
            Assert.That(withHint.Report.Issues, Is.Empty);
            Assert.That(withHint.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new FieldConversionImpact("probe", from, to, ConversionFailurePolicy.Abort),
            }));
        }
    }

    [TestCase(FieldType.Date, FieldType.Number)]
    [TestCase(FieldType.Number, FieldType.Currency)]
    [TestCase(FieldType.Currency, FieldType.Number)]
    [TestCase(FieldType.SingleSelect, FieldType.Text)]
    [TestCase(FieldType.Text, FieldType.SingleSelect)]
    [TestCase(FieldType.Person, FieldType.Text)]
    [TestCase(FieldType.Checkbox, FieldType.Number)]
    [TestCase(FieldType.Text, FieldType.Url)]
    public void Plan_ConversionThatIsNotDefined_IsBlockedEvenWithAHint(FieldType from, FieldType to)
    {
        // Act
        var plan = PlanFrom(WithProbe(from), WithProbe(to), Converts(("probe", ConversionFailurePolicy.LeaveEmpty)));

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanFieldConversionNotSupported, $"/blueprint/fields/{ProbeIndex}/type"),
        }));
        Assert.That(plan.DataImpacts, Is.Empty);
    }

    [Test]
    public void Plan_ConversionMessages_UseTheTypeNamesWrittenInDocuments()
    {
        // Act
        var plan = PlanFrom(WithProbe(FieldType.SingleSelect), WithProbe(FieldType.LongText));

        // Assert：文件裡寫的是 single_select、long_text，不是程式裡的列舉名稱。
        Assert.That(plan.Report.Issues[0].Message, Does.Contain("single_select").And.Contain("long_text"));
    }

    [Test]
    public void Plan_ConversionThatCannotLoseData_AcceptsAnUnnecessaryHint()
    {
        // Act
        var plan = PlanFrom(
            WithProbe(FieldType.Text), WithProbe(FieldType.LongText), Converts(("probe", ConversionFailurePolicy.Abort)));

        // Assert：欄位確實換了型別，這條指示只是用不到失敗的處理方式，不算寫錯。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Is.Empty);
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new FieldConversionImpact("probe", FieldType.Text, FieldType.LongText, OnFailure: null),
            }));
        }
    }

    [Test]
    public void Plan_ConvertFieldsEntryThatIsNotNeeded_IsBlocked()
    {
        // Arrange：quote_amount 的型別沒變；probe 寫了兩次。
        var hints = Converts(
            ("quote_amount", ConversionFailurePolicy.Abort),
            ("probe", ConversionFailurePolicy.LeaveEmpty),
            ("probe", ConversionFailurePolicy.Abort));

        // Act
        var plan = PlanFrom(WithProbe(FieldType.Text), WithProbe(FieldType.Number), hints);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new FieldConversionImpact("probe", FieldType.Text, FieldType.Number, ConversionFailurePolicy.LeaveEmpty),
            }));
            Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
            {
                (IssueCodes.PlanUnusedMigration, "/migrations/convertFields/0"),
                (IssueCodes.PlanUnusedMigration, "/migrations/convertFields/2"),
            }));
        }
    }

    [Test]
    public void Plan_CurrencyFieldChangesItsCurrency_IsBlocked()
    {
        // Arrange：既有的金額不會跟著換算，換了幣別數字的意思就變了。
        var next = Agency().WithField(QuoteAmount, field => field with { Currency = "USD" });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanFieldConversionNotSupported, "/blueprint/fields/0/currency"),
        }));
        Assert.That(plan.Report.Issues[0].Message, Does.Contain("「報價金額」").And.Contain("「TWD」").And.Contain("「USD」"));
    }

    [Test]
    public void Plan_SelectFieldLosesAnOption_IsBlockedAndNamesTheOption()
    {
        // Arrange：可能還有項目選了 low，而變更集沒有地方寫這些項目要改成哪個選項。
        var next = Agency().WithField(Impact, field => field with { Options = [.. field.Options!.Skip(1), Option("critical")] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanOptionRemovalNotSupported, "/blueprint/fields/3/options"),
        }));
        Assert.That(plan.Report.Issues[0].Message, Does.Contain("「影響程度」").And.Contain("「低」"));
    }

    [Test]
    public void Plan_SelectFieldGainsAndReordersOptions_IsNotBlocked()
    {
        // Arrange
        var next = Agency().WithField(Impact, field => field with { Options = [Option("critical"), .. field.Options!.Reverse()] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Is.Empty);
            Assert.That(plan.DataImpacts, Is.Empty);
        }
    }

    [Test]
    public void Plan_RemovedItemType_IsListedAsARemovalAndNeedsNoStateMoves()
    {
        // Arrange：task 被拿掉。它原本的流程還在，但已經沒有既有的類型在用。
        var next = Agency() with { ItemTypes = Agency().ItemTypes.WithoutAt(TaskType) };

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new ItemTypeRemovalImpact("task") }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_FieldTakenOffAnItemTypeButStillDefined_DetachesItWithoutAHint()
    {
        // Arrange：欄位的定義還在，只是這個類型不再有它；這個類型的項目上的值要封存，不需要變更集另外寫明。
        var next = Agency().WithItemType(ChangeRequest, type => type with { Fields = [.. type.Fields.Skip(1)] });

        // Act
        var plan = PlanFrom(Agency(), next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Changes.Describe(), Is.EqualTo(new[] { "Modified ItemType change_request [fields]" }));
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new FieldDetachImpact("change_request", "change_reason") }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_FieldsTakenOffSeveralItemTypes_AreListedInTheNewTypeOrderThenTheOldFieldOrder()
    {
        // Arrange：新藍圖把需求變更單排在需求前面；需求變更單拿掉影響程度與報價金額，需求拿掉客戶驗收日。
        var current = Agency();
        var changeRequest = current.ItemTypes[ChangeRequest] with { Fields = ["client_signoff_date", "change_reason"] };
        var requirement = current.ItemTypes[Requirement] with { Fields = ["quote_amount"] };
        var next = current with { ItemTypes = [changeRequest, requirement, current.ItemTypes[TaskType]] };

        // Act
        var plan = PlanFrom(current, next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new FieldDetachImpact("change_request", "impact"),
                new FieldDetachImpact("change_request", "quote_amount"),
                new FieldDetachImpact("requirement", "client_signoff_date"),
            }));
            Assert.That(plan.Report.Issues, Is.Empty);
        }
    }

    [Test]
    public void Plan_FieldRemovedEntirely_IsOnlyADropNotAlsoADetach()
    {
        // Act：change_reason 整個刪除，也從需求變更單的欄位清單拿掉。
        var plan = PlanFrom(Agency(), WithoutChangeReason(), Drops("change_reason"));

        // Assert
        Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new FieldDropImpact("change_reason") }));
    }

    [Test]
    public void Plan_FieldListOfANewOrRemovedItemType_DetachesNothing()
    {
        // Arrange：task 被拿掉，另外新增一個沒有欄位的 bug；需求的欄位清單只是換了順序。
        var current = Agency().WithItemType(TaskType, type => type with { Fields = ["impact"] });
        var next = (Agency() with { ItemTypes = [.. Agency().ItemTypes.WithoutAt(TaskType), ItemTypeOf("bug", "task_flow")] })
            .WithItemType(Requirement, type => type with { Fields = [.. type.Fields.Reverse()] });

        // Act
        var plan = PlanFrom(current, next);

        // Assert
        Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[] { new ItemTypeRemovalImpact("task") }));
    }

    [Test]
    public void Plan_SeveralKindsOfImpact_AreListedStateMovesFirstThenFieldsThenDetachesThenItemTypes()
    {
        // Arrange
        var current = WithProbe(FieldType.Text);
        var next = WithProbe(FieldType.LongText) with
        {
            Fields = [.. Agency().Fields.WithoutAt(ChangeReason), Probe(FieldType.LongText)],
            ItemTypes = Agency().ItemTypes.WithoutAt(TaskType),
        };
        next = next
            .WithItemType(ChangeRequest, type => type with { Fields = [.. type.Fields.Skip(2)] })
            .WithWorkflow(Delivery, workflow => workflow with { States = workflow.States.WithoutAt(1) });
        var hints = new MigrationHints
        {
            MoveStates = [new StateMove { Workflow = "delivery", From = "quoting", To = "confirming" }],
            DropFields = ["change_reason"],
        };

        // Act
        var plan = PlanFrom(current, next, hints);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Report.Issues, Is.Empty);
            Assert.That(plan.DataImpacts, Is.EqualTo(new DataImpact[]
            {
                new StateMoveImpact("delivery", "quoting", "confirming"),
                new FieldDropImpact("change_reason"),
                new FieldConversionImpact("probe", FieldType.Text, FieldType.LongText, OnFailure: null),
                new FieldDetachImpact("change_request", "impact"),
                new ItemTypeRemovalImpact("task"),
            }));
        }
    }

    [Test]
    public void Plan_NewProjectWithFieldHints_IsBlockedBecauseThereIsNoDataYet()
    {
        // Arrange
        var hints = new MigrationHints
        {
            DropFields = ["old_field"],
            ConvertFields = [new FieldConversion { Field = "impact", OnFailure = ConversionFailurePolicy.Abort }],
        };

        // Act
        var plan = Planner.Plan(current: null, currentVersion: 0, ChangeSetFor(Agency(), hints, baseVersion: 0));

        // Assert
        Assert.That(plan.Report.Issues.CodesAndPaths(), Is.EqualTo(new[]
        {
            (IssueCodes.PlanUnusedMigration, "/migrations/dropFields/0"),
            (IssueCodes.PlanUnusedMigration, "/migrations/convertFields/0"),
        }));
    }

    private static ProjectBlueprint WithoutChangeReason() =>
        (Agency() with { Fields = Agency().Fields.WithoutAt(ChangeReason) })
            .WithItemType(ChangeRequest, type => type with { Fields = [.. type.Fields.Skip(1)] });

    private static ProjectBlueprint WithProbe(FieldType type) => Agency() with { Fields = [.. Agency().Fields, Probe(type)] };

    private static FieldDefinition Probe(FieldType type) => new()
    {
        Key = "probe",
        Name = "試驗",
        Type = type,
        Options = type is FieldType.SingleSelect or FieldType.MultiSelect ? [Option("a"), Option("b")] : null,
        Currency = type == FieldType.Currency ? "TWD" : null,
    };
}
