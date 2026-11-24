using PmTool.Work.History;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>來源與外部編號的格式：結尾換行、控制字元、空白都擋得住。</summary>
[TestFixture]
public sealed class HistoryRulesTests
{
    [TestCase("monday")]
    [TestCase("my-source_2")]
    public void CheckSource_ValidNames_Pass(string source) => Assert.That(HistoryRules.CheckSource(source), Is.Null);

    [TestCase("monday\n")]
    [TestCase("monday\r\n")]
    [TestCase("Monday")]
    [TestCase("m")]
    [TestCase("1monday")]
    [TestCase("mon day")]
    [TestCase("mon\0day")]
    [TestCase("")]
    [TestCase(null)]
    public void CheckSource_BadNames_AreHistoryInvalid(string? source) =>
        Assert.That(HistoryRules.CheckSource(source)?.Code, Is.EqualTo(WorkErrorCodes.HistoryInvalid));

    [TestCase("m-1")]
    [TestCase("1234567890")]
    public void ExternalIdProblem_ValidIds_Pass(string id) => Assert.That(HistoryRules.ExternalIdProblem(id), Is.Null);

    [TestCase("m-1\n")]
    [TestCase("m-1\r")]
    [TestCase("\nm-1")]
    [TestCase("m 1")]
    [TestCase("m\t1")]
    [TestCase("m\u00001")]
    [TestCase("m\u00071")]
    [TestCase("m\u007f1")]
    [TestCase("m\u20281")]
    [TestCase("")]
    [TestCase(null)]
    public void ExternalIdProblem_TrailingLineBreakWhitespaceOrControlCharacters_AreRefused(string? id) =>
        Assert.That(HistoryRules.ExternalIdProblem(id), Is.Not.Null);

    [Test]
    public async Task ImportItemsAsync_SourceWithTrailingLineBreak_IsHistoryInvalid()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var (_, manager, member) = await ProjectWithMembersAsync(host);

        // Act
        var result = await host.History.ImportItemsAsync(
            manager, "WEB", Batch(Row("m-1", member.PersonId)) with { Source = "monday\n" }, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Code, Paths(result.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "source" })));
    }
}
