using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 用真的驗證器走完整條路：HTTP、讀內文、讀檔、結構檢查、語意規則、信封。
/// 其他測試都把驗證器換成假的，接線有沒有接對、兩邊的上限有沒有對齊，只有這裡看得到。
/// </summary>
[TestFixture]
public sealed class EndToEndTests
{
    private const string MinimalJsonBlueprint =
        """
        {
          "apiVersion": "blueprint/v0",
          "kind": "ProjectBlueprint",
          "metadata": { "key": "demo_project", "name": "示範專案" },
          "fields": [],
          "workflows": [
            {
              "key": "main",
              "name": "主要流程",
              "states": [
                { "key": "todo", "name": "待辦", "category": "not_started" },
                { "key": "done", "name": "完成", "category": "done" }
              ]
            }
          ],
          "itemTypes": [{ "key": "task", "name": "任務", "workflow": "main" }]
        }
        """;

    [TestCase(ApiRequests.BlueprintRoute, "agency-project.blueprint.yaml", "application/yaml")]
    [TestCase(ApiRequests.ChangeSetRoute, "split-acceptance.changeset.yaml", "text/yaml; charset=utf-8")]
    public async Task Validate_ExampleFile_IsValid(string route, string fileName, string contentType)
    {
        // Arrange
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(route, ApiRequests.Document(ReadExample(fileName), contentType));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(envelope["success"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["data"]?["valid"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["data"]?["issues"]?.AsArray(), Is.Empty);
            Assert.That(envelope["data"]?["truncated"]?.GetValue<bool>(), Is.False);
            Assert.That(envelope.ContainsKey("error") && envelope["error"] is null, Is.True, "error 要寫出 null");
        }
    }

    [Test]
    public async Task ValidateBlueprint_JsonDocument_IsValid()
    {
        // Arrange
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document(MinimalJsonBlueprint));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(envelope["data"]?["valid"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["data"]?["issues"]?.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task ValidateBlueprint_StructuralProblem_ReturnsTheIssueFromTheRealValidator()
    {
        // Arrange
        var yaml = ReplaceOnce(ReadExample("agency-project.blueprint.yaml"), "apiVersion: blueprint/v0", "apiVersion: blueprint/v9");
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document(yaml, "application/x-yaml"));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "驗證不通過不是錯誤");
            Assert.That(envelope["success"]?.GetValue<bool>(), Is.True);
            Assert.That(envelope["data"]?["valid"]?.GetValue<bool>(), Is.False);
            Assert.That(CodesAndPaths(envelope), Is.EqualTo(new[] { "structure.const|/apiVersion" }));
            Assert.That(envelope["data"]?["issues"]?[0]?["message"]?.GetValue<string>(), Does.Contain("blueprint/v0"));
        }
    }

    [Test]
    public async Task ValidateChangeSet_SemanticProblem_ReturnsTheIssueFromTheRealValidator()
    {
        // Arrange：搬移的目的狀態改成新藍圖裡沒有的 qa。
        var yaml = ReplaceOnce(ReadExample("split-acceptance.changeset.yaml"), "from: qa, to: internal_qa", "from: qa, to: qa");
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document(yaml, "application/yaml"));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(envelope["data"]?["valid"]?.GetValue<bool>(), Is.False);
            Assert.That(CodesAndPaths(envelope), Is.EqualTo(new[] { "changeset.move_unknown_target|/migrations/moveStates/0/to" }));
        }
    }

    [TestCase("", "application/json", "document.empty")]
    [TestCase("{ \"apiVersion\": ", "application/json", "document.syntax")]
    [TestCase("a: &anchor 1\nb: *anchor", "application/yaml", "document.alias_not_allowed")]
    public async Task ValidateBlueprint_UnreadableBody_ReturnsTheReadingIssueAs200(string body, string contentType, string expectedCode)
    {
        // Arrange
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document(body, contentType));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(CodesAndPaths(envelope), Is.EqualTo(new[] { $"{expectedCode}|" }));
        }
    }

    [Test]
    public async Task ValidateBlueprint_ExactlyAtTheCharacterLimit_IsStillParsed()
    {
        // Arrange：一個 JSON 字串，連引號剛好是上限的長度。
        var body = "\"" + new string('x', ApiLimits.MaxDocumentCharacters - 2) + "\"";
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document(body));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert：有被解析，所以回報的是「根節點不是物件」，不是太大。
        Assert.That(CodesAndPaths(envelope), Is.EqualTo(new[] { "structure.type|" }));
    }

    [Test]
    public async Task ValidateBlueprint_OneCharacterOverTheCharacterLimit_ReportsDocumentTooLarge()
    {
        // Arrange：和上一個測試合起來，確認 ApiLimits.MaxDocumentCharacters 和藍圖模組自己的上限是同一個數字。
        // 模組的常數是 internal，API 另外寫了一份；兩邊脫節的話，這兩個測試會有一個失敗。
        var body = "\"" + new string('x', ApiLimits.MaxDocumentCharacters - 1) + "\"";
        using var factory = CreateRealFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document(body));
        var envelope = await ApiRequests.ReadEnvelopeAsync(response);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(CodesAndPaths(envelope), Is.EqualTo(new[] { "document.too_large|" }));
        }
    }

    private static string ReadExample(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", fileName));

    private static string ReplaceOnce(string text, string find, string replacement)
    {
        var first = text.IndexOf(find, StringComparison.Ordinal);
        Assert.That(first, Is.GreaterThanOrEqualTo(0), $"範例檔裡找不到「{find}」");
        Assert.That(text.IndexOf(find, first + 1, StringComparison.Ordinal), Is.LessThan(0), $"「{find}」在範例檔裡不只出現一次");
        return text.Replace(find, replacement, StringComparison.Ordinal);
    }

    private static string[] CodesAndPaths(JsonObject envelope) =>
        [.. (envelope["data"]?["issues"]?.AsArray() ?? []).Select(issue => $"{issue?["code"]}|{issue?["path"]}")];

    /// <summary>
    /// 驗證器用 Program.cs 註冊的那一個。身分與工作引擎仍然換成假的：
    /// 驗證端點用不到它們，而真的模組啟動時會去連資料庫。
    /// </summary>
    private static ApiFactory CreateRealFactory() =>
        new(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseRealValidator = true });
}
