using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Work.Templates;

namespace PmTool.Work.Tests.Projects;

[TestFixture]
public sealed class TemplateTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public void ListTemplates_BuiltIn_IncludesAgencyAndSimpleTasks()
    {
        // Act
        var templates = _host.Projects.ListTemplates();

        // Assert
        Assert.That(templates.Select(t => t.Key), Is.SupersetOf(new[] { "agency_project", "simple_tasks" }));
        Assert.That(templates.Select(t => t.Name), Has.All.Not.Empty);
    }

    [Test]
    public void ListTemplates_EveryTemplate_HasAPreviewAndExamples()
    {
        // Act
        var templates = _host.Projects.ListTemplates();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var template in templates)
            {
                Assert.That(template.Workflows, Is.Not.Empty, template.Key);
                Assert.That(template.Workflows.SelectMany(w => w.ItemTypeNames), Is.Not.Empty, template.Key);
                Assert.That(template.Workflows.SelectMany(w => w.StateNames), Is.Not.Empty, template.Key);
                Assert.That(template.ExampleCount, Is.InRange(4, 8), template.Key);
            }
        }
    }

    [Test]
    public void ListTemplates_AgencyProject_PreviewsWorkflowsAndFieldsInBlueprintOrder()
    {
        // Act
        var agency = _host.Projects.ListTemplates().Single(t => t.Key == "agency_project");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(agency.Workflows.Select(w => w.Name), Is.EqualTo(new[] { "交付流程", "任務流程" }));
            Assert.That(agency.Workflows[0].ItemTypeNames, Is.EqualTo(new[] { "需求", "需求變更單" }));
            Assert.That(
                agency.Workflows[0].StateNames,
                Is.EqualTo(new[] { "需求確認", "報價", "設計", "開發", "內部驗收", "客戶驗收", "結案" }));
            Assert.That(agency.Workflows[1].ItemTypeNames, Is.EqualTo(new[] { "任務", "子任務" }));
            Assert.That(
                agency.Workflows[1].StateNames,
                Is.EqualTo(new[] { "待辦", "進行中", "內部驗收", "等客戶", "待上線", "完成", "暫停" }));
            Assert.That(agency.Workflows[1].CanceledStateNames, Is.EqualTo(new[] { "取消" }));
            Assert.That(agency.FieldNames, Is.EqualTo(new[] { "報價金額", "客戶驗收日", "變更原因", "影響程度", "實際工時" }));
        }
    }

    // 寫死數字：加減模板是有意的決定，要連這個數字一起改，才不會誤刪或漏內嵌而沒人發現。
    private const int ExpectedTemplateCount = 12;

    [Test]
    public void Catalog_BuiltIn_LoadsAllTemplatesAndEveryExamplesFileBelongsToABlueprint()
    {
        // Arrange
        var names = typeof(TemplateCatalog).Assembly.GetManifestResourceNames()
            .Select(name => name.Replace("PmTool.Work.Templates.", string.Empty, StringComparison.Ordinal))
            .ToArray();
        var blueprintKeys = names.Where(n => n.EndsWith(".blueprint.yaml", StringComparison.Ordinal)).Select(n => n[..^".blueprint.yaml".Length]).ToArray();
        var exampleKeys = names.Where(n => n.EndsWith(".examples.yaml", StringComparison.Ordinal)).Select(n => n[..^".examples.yaml".Length]).ToArray();

        // Act
        var catalog = new TemplateCatalog(BlueprintValidator.CreateDefault());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Summaries, Has.Count.EqualTo(ExpectedTemplateCount));
            Assert.That(catalog.Summaries.Select(s => s.Key), Is.EquivalentTo(blueprintKeys));
            Assert.That(blueprintKeys, Is.SupersetOf(exampleKeys), "有範例檔卻沒有對應的藍圖（孤兒）");
        }
    }

    [Test]
    public void ListTemplates_ClientMaintenance_PreviewsOneQueueThatTellsWhoIsWaited()
    {
        // Act
        var maintenance = _host.Projects.ListTemplates().Single(t => t.Key == "client_maintenance");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(maintenance.Name, Is.EqualTo("客戶維運"));
            Assert.That(maintenance.Workflows.Select(w => w.Name), Is.EqualTo(new[] { "維運流程" }));
            Assert.That(
                maintenance.Workflows[0].ItemTypeNames,
                Is.EqualTo(new[] { "修改", "問題", "資安", "例行", "評估", "任務" }));
            Assert.That(
                maintenance.Workflows[0].StateNames,
                Is.EqualTo(new[] { "待辦", "進行中", "內部驗收", "等客戶", "待上線", "完成", "暫停" }));
            Assert.That(maintenance.Workflows[0].CanceledStateNames, Is.EqualTo(new[] { "取消" }));
            Assert.That(maintenance.FieldNames, Is.EqualTo(new[] { "客戶", "實際工時", "網址" }));
            Assert.That(maintenance.Parents.Select(p => p.ItemTypeName), Is.EqualTo(new[] { "任務" }));
            Assert.That(maintenance.Parents[0].ParentTypeNames, Is.EqualTo(new[] { "修改", "問題", "資安", "例行", "評估" }));
        }
    }

    [Test]
    public void Catalog_ClientMaintenance_ExamplesSplitCrossSiteWorkIntoOneTaskPerClient()
    {
        // Arrange
        var template = new TemplateCatalog(BlueprintValidator.CreateDefault()).Find("client_maintenance")!;

        // Act：跨客戶的工作是一個上層項目，每個客戶一個下層任務，各自有狀態，上層的下層清單就看得出哪幾站還沒做。
        var crossSite = template.Examples.Single(e => e.Type == "security" && e.Ref is not null
            && template.Examples.Any(child => child.Parent == e.Ref));
        var children = template.Examples.Where(e => e.Parent == crossSite.Ref).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children, Has.Length.GreaterThanOrEqualTo(2));
            Assert.That(children.Select(c => c.Type), Has.All.EqualTo("task"));
            Assert.That(children.Select(c => c.Fields["client"].GetString()), Is.Unique);
            Assert.That(children.Select(c => c.State), Is.Unique, "每一站的進度不同，才看得出拆開的用處");
            Assert.That(template.Examples.Where(e => e.Cycle is null), Is.Empty, "維運的項目都放進當月或下個月的週期");
        }
    }

    [Test]
    public void ListTemplates_Preview_SeparatesCanceledStatesAndListsParents()
    {
        // Act
        var design = _host.Projects.ListTemplates().Single(t => t.Key == "design_project");
        var agency = _host.Projects.ListTemplates().Single(t => t.Key == "agency_project");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(design.Workflows.SelectMany(w => w.CanceledStateNames), Is.EqualTo(new[] { "擱置" }));
            Assert.That(design.Workflows.SelectMany(w => w.StateNames), Does.Not.Contain("擱置"));
            Assert.That(agency.Parents.Select(p => p.ItemTypeName), Is.EqualTo(new[] { "任務", "子任務" }));
            Assert.That(agency.Parents[0].ParentTypeNames, Is.EqualTo(new[] { "需求", "需求變更單" }));
            Assert.That(agency.Parents[1].ParentTypeNames, Is.EqualTo(new[] { "任務" }));
        }
    }

    [Test]
    public void ListTemplates_Preview_GivesEachMainStateItsCategory()
    {
        // Act
        var taskFlow = _host.Projects.ListTemplates().Single(t => t.Key == "agency_project").Workflows.Single(w => w.Name == "任務流程");

        // Assert：預覽靠類別畫狀態的形狀，也靠它把「完成」之後的「暫停」和主線分開。
        Assert.That(
            taskFlow.StateNames.Zip(taskFlow.StateCategories),
            Is.EqualTo(new[]
            {
                ("待辦", StateCategory.NotStarted), ("進行中", StateCategory.InProgress), ("內部驗收", StateCategory.InProgress),
                ("等客戶", StateCategory.InProgress), ("待上線", StateCategory.InProgress), ("完成", StateCategory.Done),
                ("暫停", StateCategory.Backlog),
            }));
    }

    [Test]
    public void Catalog_AgencyProject_TaskFlowHasTheSameStatesAsClientMaintenance()
    {
        // Arrange
        var catalog = new TemplateCatalog(BlueprintValidator.CreateDefault());

        // Act
        var taskFlow = catalog.Find("agency_project")!.Blueprint.Workflows.Single(w => w.Key == "task_flow");
        var maintenance = catalog.Find("client_maintenance")!.Blueprint.Workflows.Single(w => w.Key == "maintenance_flow");

        // Assert：實際用的「內部驗收」「等客戶」「待上線」要裝得下，名稱與類別和維運一致，團隊兩邊切換才不用重新記。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                taskFlow.States.Select(s => (s.Key, s.Name, s.Category)),
                Is.EqualTo(maintenance.States.Select(s => (s.Key, s.Name, s.Category))));
            Assert.That(taskFlow.Transitions, Is.Null.Or.Empty, "任務常常跳步，不限制轉換");
        }
    }

    [Test]
    public void Catalog_AgencyProject_SubtaskOnlyUnderTaskAndBothTypesTrackActualHours()
    {
        // Arrange
        var catalog = new TemplateCatalog(BlueprintValidator.CreateDefault());

        // Act
        var blueprint = catalog.Find("agency_project")!.Blueprint;
        var task = blueprint.ItemTypes.Single(t => t.Key == "task");
        var subtask = blueprint.ItemTypes.Single(t => t.Key == "subtask");
        var hours = blueprint.Fields.Single(f => f.Key == "actual_hours");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(subtask.Name, Is.EqualTo("子任務"));
            Assert.That(subtask.Workflow, Is.EqualTo("task_flow"));
            Assert.That(subtask.Parents, Is.EqualTo(new[] { "task" }));
            Assert.That(task.Fields, Does.Contain("actual_hours"));
            Assert.That(subtask.Fields, Is.EqualTo(task.Fields));
            Assert.That(hours.Name, Is.EqualTo("實際工時"));
        }
    }

    [Test]
    public void Catalog_AgencyProject_ExamplesIncludeASubtaskUnderATask()
    {
        // Arrange
        var template = new TemplateCatalog(BlueprintValidator.CreateDefault()).Find("agency_project")!;

        // Act
        var subtasks = template.Examples.Where(e => e.Type == "subtask").ToArray();

        // Assert
        Assert.That(subtasks, Is.Not.Empty);
        Assert.That(
            subtasks.Select(s => template.Examples.Single(e => e.Ref == s.Parent).Type),
            Has.All.EqualTo("task"));
    }

    [Test]
    public void Catalog_EveryTemplate_LoadsItsExamplesAndValidatesThem()
    {
        // Act（建構時就整份驗證，壞了會丟例外）
        var catalog = new TemplateCatalog(BlueprintValidator.CreateDefault());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var summary in catalog.Summaries)
            {
                var template = catalog.Find(summary.Key)!;
                Assert.That(template.Examples, Has.Count.EqualTo(summary.ExampleCount), summary.Key);
                Assert.That(template.Examples, Is.Not.Empty, summary.Key);
            }
        }
    }

    [Test]
    public void Catalog_Examples_FillRequiredFieldsOfTheStateTheyAreIn()
    {
        // Arrange
        var catalog = new TemplateCatalog(BlueprintValidator.CreateDefault());

        // Act + Assert：狀態是 requireFields 轉換的終點時，範例要把那個欄位也填上，看起來才合理。
        using (Assert.EnterMultipleScope())
        {
            foreach (var summary in catalog.Summaries)
            {
                var template = catalog.Find(summary.Key)!;
                foreach (var example in template.Examples)
                {
                    var type = template.Blueprint.ItemTypes.Single(t => t.Key == example.Type);
                    var workflow = template.Blueprint.Workflows.Single(w => w.Key == type.Workflow);
                    var state = example.State ?? workflow.States[0].Key;
                    var required = (workflow.Transitions ?? [])
                        .Where(t => t.To == state)
                        .SelectMany(t => t.RequireFields)
                        .Distinct();
                    Assert.That(example.Fields.Keys, Is.SupersetOf(required), $"{summary.Key}：{example.Title}");
                }
            }
        }
    }

    [Test]
    public void Catalog_Examples_ContainNoRealLookingData()
    {
        // Arrange
        var catalog = new TemplateCatalog(BlueprintValidator.CreateDefault());

        // Act
        var urls = catalog.Summaries
            .SelectMany(s => catalog.Find(s.Key)!.Examples)
            .SelectMany(e => e.Fields.Values.Where(v => v.ValueKind == System.Text.Json.JsonValueKind.String).Select(v => v.GetString()!))
            .Where(text => text.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        // Assert
        Assert.That(urls, Has.All.Contain("example.com"));
    }
}
