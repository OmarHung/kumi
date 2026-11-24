using System.Text;
using System.Text.Json;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint.Tests.Documents;

/// <summary>
/// 把範例檔隨機改壞再讀，確認 Read 從不丟例外、成功時的樹一定能序列化。
/// 種子固定，失敗時可以重現；YamlDotNet 會丟 YamlException 以外的例外，就是這樣找到的。
/// </summary>
[TestFixture]
public sealed class DocumentReaderFuzzTests
{
    private const int Seed = 20_261_112;
    private const int MutatedDocumentsPerFormat = 3_000;
    private const int MaxMutationsPerDocument = 5;
    private const int MaxCopiedLength = 20;

    private static readonly string Alphabet =
        "-?:,[]{}#&*!|>'\"%@`\\ \t\n\r0123456789abcxyzUuex.~+eE"
        + new string(new[] { (char)0x85, (char)0x2028, DocumentText.ByteOrderMark, (char)0, (char)0xA0 });

    [Test]
    public void Read_RandomlyMutatedExamples_NeverThrowAndAlwaysReturnASerializableTree([Values] DocumentFormat format)
    {
        // Arrange
        var random = new Random(Seed);
        var seeds = SeedTexts(format);
        var inputs = Enumerable.Range(0, MutatedDocumentsPerFormat)
            .Select(_ => Mutate(seeds[random.Next(seeds.Length)], random))
            .ToList();

        // Act
        var failures = inputs
            .Select((text, index) => Describe(index, text, format))
            .OfType<string>()
            .ToList();

        // Assert
        Assert.That(failures, Is.Empty);
    }

    private static string[] SeedTexts(DocumentFormat format)
    {
        var yamlTexts = new[] { "agency-project.blueprint.yaml", "split-acceptance.changeset.yaml" }
            .Select(DocumentText.ReadExample)
            .ToArray();
        if (format == DocumentFormat.Yaml)
        {
            return yamlTexts;
        }

        var indented = new JsonSerializerOptions { WriteIndented = true };
        return yamlTexts
            .Select(text => DocumentReader.Read(text, DocumentFormat.Yaml).Root!.ToJsonString(indented))
            .ToArray();
    }

    private static string Mutate(string seed, Random random)
    {
        var builder = new StringBuilder(seed);
        var mutations = random.Next(1, MaxMutationsPerDocument + 1);
        for (var step = 0; step < mutations; step++)
        {
            var position = random.Next(builder.Length);
            switch (random.Next(4))
            {
                case 0:
                    builder.Insert(position, Alphabet[random.Next(Alphabet.Length)]);
                    break;
                case 1:
                    builder.Remove(position, 1);
                    break;
                case 2:
                    builder.Insert(position, "\"" + DocumentText.Backslash + "U" + random.Next().ToString("X8") + "\"");
                    break;
                default:
                    var length = Math.Min(random.Next(1, MaxCopiedLength), builder.Length - position);
                    builder.Insert(random.Next(builder.Length), builder.ToString(position, length));
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>結果符合合約就回傳 null，否則回傳一行說明，方便在失敗訊息裡看到是第幾份。</summary>
    private static string? Describe(int index, string text, DocumentFormat format)
    {
        try
        {
            var result = DocumentReader.Read(text, format);
            if (result.Succeeded)
            {
                _ = result.Root?.ToJsonString();
                return null;
            }

            var isWellFormedFailure = result.Root is null && result.Issues.Count == 1 && result.Issues[0].Path.Length == 0;
            return isWellFormedFailure ? null : $"#{index}: 失敗結果的形狀不對";
        }
        catch (Exception exception)
        {
            return $"#{index}: {exception.GetType().Name}: {exception.Message}";
        }
    }
}
