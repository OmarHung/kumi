using PmTool.Cli.Output;

namespace PmTool.Cli.Tests;

[TestFixture]
public sealed class TextTableTests
{
    [TestCase("abc", 3)]
    [TestCase("標題", 4)]
    [TestCase("A-1 任務", 8)]
    [TestCase("", 0)]
    [TestCase("🔥", 2)]
    [TestCase("🚀 上線", 7)]
    [TestCase("🧪", 2)]
    public void DisplayWidth_CountsWideCharactersAsTwoColumns(string text, int expected) =>
        Assert.That(TextTable.DisplayWidth(text), Is.EqualTo(expected));

    [Test]
    public async Task Write_AlignsColumnsByDisplayWidthAndKeepsEachCellOnOneLine()
    {
        // Arrange
        var output = new StringWriter();

        // Act
        await TextTable.WriteAsync(output, ["編號", "標題"], [["A-1", "第一行\n第二行"], ["LONGER-10", "標題"]]);

        // Assert
        var lines = output.ToString().TrimEnd().Split(output.NewLine);
        Assert.That(lines, Is.EqualTo(new[]
        {
            "編號       標題",
            "A-1        第一行 第二行",
            "LONGER-10  標題",
        }));
    }
}
