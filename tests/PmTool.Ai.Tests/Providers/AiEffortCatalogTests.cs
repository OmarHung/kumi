using PmTool.Ai.Providers;

namespace PmTool.Ai.Tests.Providers;

/// <summary>能選的思考深度的規則（第 4.3 節的表）。</summary>
[TestFixture]
public sealed class AiEffortCatalogTests
{
    private static readonly AiEffort[] LowToHigh = [AiEffort.Low, AiEffort.Medium, AiEffort.High];

    [TestCase("claude-opus-5-5", "medium")]
    [TestCase("claude-opus-5-5-20261001", "medium")]
    [TestCase("claude-haiku-5-5", "medium")]
    [TestCase("claude-sonnet-5-5", "high")]
    [TestCase("claude-fable-5-2", "high")]
    [TestCase("claude-opus-5", "high")]
    [TestCase("claude-sonnet-5-20260101", "high")]
    [TestCase("claude-opus-4-7", "high")]
    [TestCase("claude-opus-4-1", "")]
    [TestCase("claude-opus-5-6", "")]
    public void Anthropic_DefaultEffort_FollowsTheDocumentedTable(string modelId, string expected) =>
        Assert.That(
            AiEffortCatalog.DefaultForAnthropic(modelId, [AiEffort.Low, AiEffort.Medium, AiEffort.High])?.ToString().ToLowerInvariant() ?? string.Empty,
            Is.EqualTo(expected));

    [Test]
    public void Anthropic_DefaultEffort_IsNullWhenTheModelCannotSelectIt() =>
        Assert.That(AiEffortCatalog.DefaultForAnthropic("claude-opus-5-5", [AiEffort.Low, AiEffort.High]), Is.Null);

    [TestCase("o1", "low,medium,high")]
    [TestCase("o3-mini", "low,medium,high")]
    [TestCase("o4-mini", "low,medium,high")]
    [TestCase("gpt-5", "low,medium,high")]
    [TestCase("gpt-5-mini", "low,medium,high")]
    [TestCase("gpt-5-nano", "low,medium,high")]
    [TestCase("gpt-5-codex", "low,medium,high")]
    [TestCase("gpt-5.1", "none,low,medium,high")]
    [TestCase("gpt-5.1-mini", "none,low,medium,high")]
    [TestCase("gpt-5.2", "none,low,medium,high,xhigh")]
    [TestCase("gpt-5.10", "none,low,medium,high,xhigh")]
    [TestCase("gpt-5.2-codex", "none,low,medium,high,xhigh")]
    [TestCase("gpt-5-pro", "medium,high,xhigh")]
    [TestCase("gpt-5.2-pro", "medium,high,xhigh")]
    [TestCase("gpt-4o", "")]
    [TestCase("gpt-4.1", "")]
    [TestCase("chatgpt-4o-latest", "")]
    [TestCase("gpt-50", "")]
    [TestCase("gpt-5.", "")]
    [TestCase("llama3", "")]
    public void OpenAi_FollowsTheModelNameRules(string modelId, string expected) =>
        Assert.That(string.Join(",", AiEffortCatalog.ForOpenAi(modelId).Select(Name)), Is.EqualTo(expected));

    [TestCase("gemini-3-pro", true, "low,medium,high")]
    [TestCase("gemini-3.1-flash", true, "low,medium,high")]
    [TestCase("gemini-10-ultra", true, "low,medium,high")]
    [TestCase("gemini-3-pro", false, "")]
    [TestCase("gemini-2.5-pro", true, "")]
    [TestCase("gemini-1.5-flash", true, "")]
    [TestCase("gemma-3-27b", true, "")]
    public void Google_NeedsMajorVersionThreeAndAThinkingModel(string modelId, bool thinking, string expected) =>
        Assert.That(string.Join(",", AiEffortCatalog.ForGoogle(modelId, thinking).Select(Name)), Is.EqualTo(expected));

    [Test]
    public void Anthropic_ListsOnlyTheSupportedLevelsAndIsEmptyWithoutCapabilities()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AiEffortCatalog.ForAnthropic(true, true, true, true, true), Is.EqualTo(new[] { AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh, AiEffort.Max }));
            Assert.That(AiEffortCatalog.ForAnthropic(true, true, true, false, false), Is.EqualTo(LowToHigh));
            Assert.That(AiEffortCatalog.ForAnthropic(false, false, false, false, false), Is.Empty);
        }
    }

    [Test]
    public void Custom_FollowsTheProvidersRulesAndCompatibleIsAlwaysEmpty()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AiEffortCatalog.ForCustom(AiProviderKind.Anthropic, "claude-future-9"), Has.Count.EqualTo(5));
            Assert.That(AiEffortCatalog.ForCustom(AiProviderKind.Openai, "gpt-5.2"), Has.Count.EqualTo(5));
            Assert.That(AiEffortCatalog.ForCustom(AiProviderKind.Google, "gemini-3-pro"), Is.EqualTo(LowToHigh));
            Assert.That(AiEffortCatalog.ForCustom(AiProviderKind.OpenaiCompatible, "gpt-5.2"), Is.Empty);
        }
    }

    private static string Name(AiEffort effort) => effort.ToString().ToLowerInvariant();
}
