using System.Text.Json.Nodes;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 「接案專案」裡掛在需求底下的任務：項目清單用 parent 只列那個項目的直接下層；
/// 任務搬到另一個需求底下時，活動用項目編號記下搬家前後的上層。
/// </summary>
[TestFixture]
public sealed class ChildItemsTests : RealStackTestBase
{
    [Test]
    public async Task List_FilteredByParent_ReturnsOnlyItsDirectChildren()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateTreeAsync(owner, "KIDS");

        // Act
        var children = await ListAsync(owner, "KIDS", "parent=KIDS-1");
        var ofMissingParent = await ListAsync(owner, "KIDS", "parent=KIDS-99");

        // Assert
        var listed = children["items"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RealStackSteps.KeysOf(listed), Is.EqualTo(new[] { "KIDS-3", "KIDS-4" }));
            Assert.That(listed.Select(i => i?["parentKey"]?.GetValue<string>()), Is.All.EqualTo("KIDS-1"));
            Assert.That(ofMissingParent["items"]?.AsArray(), Is.Empty, "不存在的上層回空的清單，不是錯誤");
            Assert.That(ofMissingParent["nextCursor"], Is.Null);
        }
    }

    [Test]
    public async Task Task_MovedToAnotherRequirement_IsListedThereAndTheActivityNamesBothParents()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await CreateTreeAsync(owner, "SHIFT");

        // Act
        var moved = await RealStackSteps.PatchItemAsync(owner.Client, "SHIFT-3", new { parentKey = "SHIFT-2" });
        var underFirst = await ListAsync(owner, "SHIFT", "parent=SHIFT-1");
        var underSecond = await ListAsync(owner, "SHIFT", "parent=SHIFT-2");
        var activity = await RealStackSteps.ActivityAsync(owner.Client, "SHIFT-3");

        // Assert
        var entries = activity["items"]!.AsArray();
        var expected = new JsonArray(new JsonObject { ["field"] = "parent", ["from"] = "SHIFT-1", ["to"] = "SHIFT-2" });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(moved["parentKey"]?.GetValue<string>(), Is.EqualTo("SHIFT-2"));
            Assert.That(RealStackSteps.KeysOf(underFirst["items"]), Is.EqualTo(new[] { "SHIFT-4" }));
            Assert.That(RealStackSteps.KeysOf(underSecond["items"]), Is.EqualTo(new[] { "SHIFT-3", "SHIFT-5" }));
            Assert.That(RealStackSteps.KindsOf(entries), Is.EqualTo(new[] { "created", "updated" }));
            Assert.That(JsonNode.DeepEquals(entries[1]?["changes"], expected), Is.True, entries[1]?["changes"]?.ToJsonString());
        }
    }

    /// <summary>1、2 是需求；3、4 掛在 1 底下，5 掛在 2 底下，6 沒有上層。</summary>
    private static async Task CreateTreeAsync(SignedInClient owner, string key)
    {
        await RealStackSteps.CreateProjectAsync(owner.Client, key, "agency_project");
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "requirement", title = "首頁改版需求" });
        await RealStackSteps.CreateItemAsync(owner.Client, key, new { type = "requirement", title = "會員中心需求" });
        await CreateTaskAsync(owner, key, "首頁的版面", $"{key}-1");
        await CreateTaskAsync(owner, key, "首頁的文案", $"{key}-1");
        await CreateTaskAsync(owner, key, "會員中心的登入", $"{key}-2");
        await CreateTaskAsync(owner, key, "整理共用元件", parentKey: null);
    }

    private static async Task CreateTaskAsync(SignedInClient owner, string projectKey, string title, string? parentKey)
    {
        var item = await RealStackSteps.CreateItemAsync(owner.Client, projectKey, new { type = "task", title, parentKey });
        Assert.That(item["parentKey"]?.GetValue<string>(), Is.EqualTo(parentKey));
    }

    private static Task<JsonNode> ListAsync(SignedInClient owner, string projectKey, string query) =>
        RealStackSteps.GetAsync(owner.Client, $"/v1/projects/{projectKey}/items?{query}");
}
