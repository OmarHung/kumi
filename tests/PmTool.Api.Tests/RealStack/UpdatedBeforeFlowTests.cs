using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>整個堆疊（真的資料庫）上的「最後更新早於」：非 UTC 的時區寫進 timestamptz 的比對不能壞。</summary>
[TestFixture]
public sealed class UpdatedBeforeFlowTests : RealStackTestBase
{
    [Test]
    public async Task UpdatedBefore_WithANonUtcOffset_FiltersByTheInstantInTheProjectAndAcrossProjects()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        await RealStackSteps.CreateProjectAsync(owner.Client, "UBA", "simple_tasks");
        await RealStackSteps.CreateItemAsync(owner.Client, "UBA", new { type = "task", title = "剛建立的" });
        var taipei = TimeSpan.FromHours(8);
        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToOffset(taipei).ToString("yyyy-MM-ddTHH:mm:sszzz"));
        var past = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToOffset(taipei).ToString("yyyy-MM-ddTHH:mm:sszzz"));

        // Act
        var inProjectFuture = await RealStackSteps.GetAsync(owner.Client, $"/v1/projects/UBA/items?updatedBefore={future}");
        var inProjectPast = await RealStackSteps.GetAsync(owner.Client, $"/v1/projects/UBA/items?updatedBefore={past}");
        var acrossFuture = await RealStackSteps.GetAsync(owner.Client, $"/v1/items?project=UBA&updatedBefore={future}");
        var acrossPast = await RealStackSteps.GetAsync(owner.Client, $"/v1/items?project=UBA&updatedBefore={past}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Keys(inProjectFuture), Is.EqualTo(new[] { "UBA-1" }));
            Assert.That(Keys(inProjectPast), Is.Empty);
            Assert.That(Keys(acrossFuture), Is.EqualTo(new[] { "UBA-1" }));
            Assert.That(Keys(acrossPast), Is.Empty);
        }
    }

    private static string[] Keys(JsonNode page) =>
        [.. page["items"]!.AsArray().Select(i => i!["key"]!.GetValue<string>())];
}
