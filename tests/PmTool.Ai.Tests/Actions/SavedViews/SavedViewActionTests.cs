using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Actions;
using PmTool.Ai.Actions.SavedViews;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions.SavedViews;

/// <summary>存的檢視動作建議：建立時的檢查與卡片步驟（專案的、「我的工作」的），套用時的服務呼叫。</summary>
[TestFixture]
public sealed class SavedViewActionTests
{
    private static readonly Actor Member = new(Guid.NewGuid(), Guid.NewGuid(), IsOrganizationAdmin: false);

    private RecordingSavedViews _views = null!;
    private SavedViewActionHandler _handler = null!;

    [SetUp]
    public void CreateHandler()
    {
        _views = new RecordingSavedViews();
        _handler = new SavedViewActionHandler(new ServiceCollection().AddSingleton<ISavedViewService>(_views).BuildServiceProvider());
    }

    private static JsonElement Payload(params object[] changes) => JsonSerializer.SerializeToElement(new { changes });

    private Task<Result<PreparedAction>> PrepareAsync(string? projectKey, params object[] changes) =>
        _handler.PrepareAsync(new ActionContext(Member, projectKey, null), Payload(changes), CancellationToken.None);

    private async Task<PreparedAction> PrepareOkAsync(string? projectKey, params object[] changes)
    {
        var result = await PrepareAsync(projectKey, changes);
        Assert.That(result.Failure, Is.Null, () => result.Failure!.Message + string.Join("；", result.Failure!.Details.Select(d => $"{d.Path}:{d.Message}")));
        return result.Value;
    }

    private Task<ActionStepResult> ApplyAsync(string? projectKey, PreparedAction prepared, int index) =>
        _handler.ApplyStepAsync(new ActionContext(Member, projectKey, null), prepared.State, index, new Dictionary<string, string>(), CancellationToken.None);

    [Test]
    public async Task Create_InAProject_ShowsTheConditionsAndCreatesTheViewThroughTheProjectService()
    {
        // Arrange
        var change = new { action = "create", name = "本週到期", layout = "board", due = "week", assignee = "me", fields = new { client = "ACME" } };

        // Act
        var prepared = await PrepareOkAsync("WEB", change);
        var applied = await ApplyAsync("WEB", prepared, 0);

        // Assert
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Title, Is.EqualTo("建議調整檢視（WEB）"));
            Assert.That((step.Label, step.Target, step.Before, step.Format), Is.EqualTo(("新增檢視", "本週到期", (string?)null, ActionTextFormat.Text)));
            Assert.That(step.After, Does.Contain("版面：看板").And.Contain("到期：week").And.Contain("負責人：me").And.Contain("欄位 client：ACME"));
            Assert.That(applied.Failure, Is.Null);
            Assert.That(applied.Link, Is.EqualTo("/projects/WEB"));
            Assert.That(_views.Calls, Is.EqualTo(new[] { "create:WEB" }));
            Assert.That(_views.Drafts.Single().Query.Fields, Is.EqualTo(new Dictionary<string, string> { ["client"] = "ACME" }));
        }
    }

    [Test]
    public async Task Create_InMyWork_UsesTheMyWorkServiceAndRejectsProjectOnlyConditions()
    {
        // Arrange
        var ok = new { action = "create", name = "我建立的", mine = "created", category = "all" };
        var bad = new { action = "create", name = "壞的", cycle = "current" };

        // Act
        var prepared = await PrepareOkAsync(null, ok);
        await ApplyAsync(null, prepared, 0);
        var rejected = await PrepareAsync(null, bad);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Title, Is.EqualTo("建議調整「我的工作」的檢視"));
            Assert.That(_views.Calls, Is.EqualTo(new[] { "create:my_work" }));
            Assert.That(rejected.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "changes[0].cycle" }));
        }
    }

    [Test]
    public async Task Update_OnlyTheGivenPropertiesChange_AndNullClearsACondition()
    {
        // Arrange
        var existing = RecordingSavedViews.View("舊名稱", SavedViewLayout.Table, new SavedViewQuery("task", null, "me", null, null, "week", null));
        _views.Project.Add(existing);

        // Act
        var prepared = await PrepareOkAsync("WEB", new { action = "update", viewId = existing.Id, name = "新名稱", due = (string?)null });
        await ApplyAsync("WEB", prepared, 0);

        // Assert
        var draft = _views.Drafts.Single();
        var step = prepared.Steps.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(draft.Name, Is.EqualTo("新名稱"));
            Assert.That(draft.Query.Type, Is.EqualTo("task"), "沒給的條件維持原樣");
            Assert.That(draft.Query.Assignee, Is.EqualTo("me"));
            Assert.That(draft.Query.Due, Is.Null, "給 null 是清掉");
            Assert.That(step.Before, Does.Contain("到期：week"));
            Assert.That(step.After, Does.Not.Contain("到期"));
            Assert.That(_views.Calls.Single(), Is.EqualTo($"replace:WEB:{existing.Id}"));
        }
    }

    [Test]
    public async Task Delete_AnExistingView_ShowsItsConditionsAndDeletesIt()
    {
        // Arrange
        var existing = RecordingSavedViews.View("不用了", SavedViewLayout.Calendar);
        _views.MyWork.Add(existing);

        // Act
        var prepared = await PrepareOkAsync(null, new { action = "delete", viewId = existing.Id });
        var applied = await ApplyAsync(null, prepared, 0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(prepared.Steps.Single().Before, Does.Contain("版面：月曆"));
            Assert.That(prepared.Steps.Single().After, Is.Null);
            Assert.That(applied.Failure, Is.Null);
            Assert.That(_views.Calls.Single(), Is.EqualTo($"delete:my_work:{existing.Id}"));
        }
    }

    [Test]
    public async Task Prepare_ManyProblems_AreAllReportedTogetherWithTheirPaths()
    {
        // Arrange
        var changes = new object[]
        {
            new { action = "create", name = "", layout = "grid" },
            new { action = "update", viewId = Guid.NewGuid(), name = "找不到" },
            new { action = "explode" },
            new { action = "create", name = "多餘", surprise = 1 },
        };

        // Act
        var result = await PrepareAsync("WEB", changes);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EquivalentTo(
            new[] { "changes[0].name", "changes[0].layout", "changes[1].viewId", "changes[2].action", "changes[3].surprise" }));
    }

    [Test]
    public async Task Prepare_MoreViewsThanTheLimit_IsRejected()
    {
        // Arrange
        _views.Project.AddRange(Enumerable.Range(0, SavedViewRules.MaxPerProject).Select(i => RecordingSavedViews.View($"檢視 {i}")));

        // Act
        var result = await PrepareAsync("WEB", new { action = "create", name = "第 51 個" });

        // Assert
        Assert.That(result.Failure?.Details.Single().Message, Does.Contain("最多"));
    }

    [Test]
    public async Task Apply_WhenTheServiceRefuses_ReturnsTheFailureForTheStep()
    {
        // Arrange
        var prepared = await PrepareOkAsync("WEB", new { action = "create", name = "重複" });
        _views.FailWith = new Failure(FailureKind.Conflict, "view.name_taken", "名稱重複了。");

        // Act
        var applied = await ApplyAsync("WEB", prepared, 0);

        // Assert
        Assert.That(applied.Failure?.Code, Is.EqualTo("view.name_taken"));
    }
}
