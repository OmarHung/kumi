using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class ClaudeModelsTests
{
    [TestCase("claude-opus-5-5", "Opus 5.5")]
    [TestCase("claude-sonnet-5-5", "Sonnet 5.5")]
    [TestCase("claude-haiku-5-5", "Haiku 5.5")]
    [TestCase("claude-fable-5-2", "Fable 5.2")]
    [TestCase("claude-opus-5", "Opus 5")]
    [TestCase("claude-opus-4-1-20250805", "Opus 4.1")]
    [TestCase("claude-sonnet-4-5[1m]", "Sonnet 4.5")]
    [TestCase("claude-3-5-haiku-20241022", "claude-3-5-haiku-20241022")]
    [TestCase("gpt-something", "gpt-something")]
    [TestCase("", "")]
    public void DisplayName_TurnsModelIdsIntoReadableNamesAndKeepsWhatItCannotRead(string id, string expected)
    {
        // Assert
        Assert.That(ClaudeModels.DisplayName(id), Is.EqualTo(expected));
    }

    [Test]
    public void WithResolvedNames_PutsTheVersionOnlyInTheDisplayName()
    {
        // Act
        var models = ClaudeModels.WithResolvedNames(new Dictionary<string, string> { ["default"] = "claude-opus-5-5", ["sonnet"] = "claude-sonnet-5-5" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Select(model => model.Id), Is.EqualTo(new[] { "default", "opus", "sonnet", "haiku" }));
            Assert.That(models.Select(model => model.DisplayName), Is.EqualTo(new[] { "預設（Opus 5.5）", "Opus", "Sonnet 5.5", "Haiku" }));
        }
    }

    [TestCase("claude-fable-5-2", "medium")]
    [TestCase("claude-opus-5-5", "medium")]
    [TestCase("claude-sonnet-5-5[1m]", "medium")]
    [TestCase("claude-haiku-5-5-20260101", "medium")]
    [TestCase("claude-opus-4-7", "xhigh")]
    [TestCase("claude-sonnet-4-5", "high")]
    [TestCase("claude-opus-4-1-20250805", "high")]
    [TestCase("gpt-something", null)]
    [TestCase("", null)]
    public void DefaultEffort_FollowsTheDocumentedDefaultOfTheResolvedModel_AndIsNullForNonClaudeIds(string id, string? expected)
    {
        // Assert
        Assert.That(ClaudeModels.DefaultEffortOf(id), Is.EqualTo(expected));
    }

    [Test]
    public void WithResolvedNames_CarriesTheDefaultEffortOnlyForResolvedModelsWhoseEffortsContainIt()
    {
        // Act
        var models = ClaudeModels.WithResolvedNames(new Dictionary<string, string>
        {
            ["default"] = "claude-opus-5-5", ["opus"] = "claude-opus-4-7", ["haiku"] = "claude-opus-4-7",
        });

        // Assert
        Assert.That(models.Select(model => model.DefaultEffort), Is.EqualTo(new[] { "medium", "xhigh", null, null }),
            "sonnet 探不到是 null；haiku 的選項裡沒有 xhigh 也是 null");
    }

    [Test]
    public void EngineModelJson_WritesDefaultEffortOrNull()
    {
        // Act
        var json = new EngineModel("m", "M", ["low", "high"], "high").ToJson();

        // Assert
        Assert.That(json["defaultEffort"]!.GetValue<string>(), Is.EqualTo("high"));
        Assert.That(new EngineModel("m", "M", ["low"]).ToJson()["defaultEffort"], Is.Null);
    }

    [Test]
    public void WithResolvedNames_WhenNothingResolved_KeepsTheFixedList()
    {
        // Assert
        Assert.That(ClaudeModels.WithResolvedNames(new Dictionary<string, string>()), Is.EqualTo(ClaudeCodeSpec.Models));
    }
}
