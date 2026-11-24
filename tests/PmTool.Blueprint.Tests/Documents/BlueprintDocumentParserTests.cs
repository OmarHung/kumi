using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Tests.Documents;

/// <summary>公開的讀文件入口：和驗證器讀文件走同一份程式，CLI 靠它在送出前改文件。</summary>
[TestFixture]
public sealed class BlueprintDocumentParserTests
{
    [Test]
    public void Parse_YamlScalars_FollowTheSameRulesAsTheValidator()
    {
        // Arrange
        const string yaml = "hex: 0x10\nbig: 99999999999999999999\nquoted: \"12\"\nflag: true\nnothing: ~\n";

        // Act
        var result = BlueprintDocumentParser.Parse(yaml, DocumentFormat.Yaml);

        // Assert
        var root = (JsonObject)result.Root!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(root["hex"]!.GetValue<long>(), Is.EqualTo(16));
            Assert.That(root["big"]!.GetValue<string>(), Is.EqualTo("99999999999999999999"), "放不進 long 的整數是字串");
            Assert.That(root["quoted"]!.GetValue<string>(), Is.EqualTo("12"));
            Assert.That(root["flag"]!.GetValue<bool>(), Is.True);
            Assert.That(root["nothing"], Is.Null);
        }
    }

    [TestCase("a: &x 1\nb: *x\n", "anchor")]
    [TestCase("a: !!str 1\n", "標籤")]
    [TestCase("? [a, b]\n: 1\n", "鍵是 mapping 或 sequence")]
    [TestCase("a: 1\n---\nb: 2\n", "第二份文件")]
    public void Parse_YamlFeaturesTheServerRejects_ReturnsAnIssueWithoutThrowing(string yaml, string expected)
    {
        // Act
        var result = BlueprintDocumentParser.Parse(yaml, DocumentFormat.Yaml);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Root, Is.Null);
            Assert.That(result.Issues.Single().Message, Does.Contain(expected));
        }
    }

    [Test]
    public void Parse_Json_ReturnsTheTree()
    {
        // Act
        var result = BlueprintDocumentParser.Parse("{\"baseVersion\":1}", DocumentFormat.Json);

        // Assert
        Assert.That(((JsonObject)result.Root!)["baseVersion"]!.GetValue<long>(), Is.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("   ")]
    public void Parse_EmptyText_ReturnsTheEmptyDocumentIssue(string? text)
    {
        // Act
        var result = BlueprintDocumentParser.Parse(text, DocumentFormat.Yaml);

        // Assert
        Assert.That(result.Issues.Single().Code, Is.EqualTo(IssueCodes.DocumentEmpty));
    }
}
