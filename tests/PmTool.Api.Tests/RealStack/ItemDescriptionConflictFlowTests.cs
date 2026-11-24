using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests.RealStack;

/// <summary>兩個人從同一個版本開始改同一段說明：先存的成功，後存的被擋下來，什麼都沒有被蓋掉。</summary>
[TestFixture]
public sealed class ItemDescriptionConflictFlowTests : RealStackTestBase
{
    [Test]
    public async Task Patch_WithAStaleDescriptionRevision_Returns409AndKeepsTheOtherPersonsText()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DCF", "simple_tasks");
        var created = await RealStackSteps.CreateItemAsync(owner.Client, "DCF", new { type = "task", title = "任務", description = "原本" });
        var startedAt = created["descriptionRevision"]!.GetValue<string>();

        // Act：B 先存；A 用同一個起點的版本、連標題一起改。
        var first = await RealStackSteps.PatchItemAsync(owner.Client, "DCF-1", new { description = "B 的", expectedDescriptionRevision = startedAt });
        using var second = await owner.Client.PatchAsync(
            "/v1/items/DCF-1", ApiRequests.Json(new { title = "A 的標題", description = "A 的", expectedDescriptionRevision = startedAt }));
        var current = await RealStackSteps.GetAsync(owner.Client, "/v1/items/DCF-1");

        // Assert
        await ApiRequests.AssertFailureEnvelopeAsync(second, HttpStatusCode.Conflict, WorkErrorCodes.ItemDescriptionConflict);
        Assert.That(first["descriptionRevision"]!.GetValue<string>(), Is.Not.EqualTo(startedAt));
        Assert.That((current["title"]!.GetValue<string>(), current["description"]!.GetValue<string>()), Is.EqualTo(("任務", "B 的")));
        Assert.That(current["descriptionRevision"]!.GetValue<string>(), Is.EqualTo(first["descriptionRevision"]!.GetValue<string>()));
    }

    [Test]
    public async Task Patch_WithTheCurrentRevisionOrNone_Succeeds()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "DCG", "simple_tasks");
        var created = await RealStackSteps.CreateItemAsync(owner.Client, "DCG", new { type = "task", title = "任務" });

        // Act
        var withRevision = await RealStackSteps.PatchItemAsync(
            owner.Client, "DCG-1", new { description = "第一次", expectedDescriptionRevision = created["descriptionRevision"]!.GetValue<string>() });
        var without = await RealStackSteps.PatchItemAsync(owner.Client, "DCG-1", new { description = "不檢查" });

        // Assert
        Assert.That(withRevision["description"]!.GetValue<string>(), Is.EqualTo("第一次"));
        Assert.That(without["description"]!.GetValue<string>(), Is.EqualTo("不檢查"));
    }
}
