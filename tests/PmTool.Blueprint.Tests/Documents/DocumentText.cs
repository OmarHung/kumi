using System.Text;

namespace PmTool.Blueprint.Tests.Documents;

/// <summary>組出測試用的文件文字。特殊字元一律用字碼建立，避免原始檔裡出現看不見的字元。</summary>
internal static class DocumentText
{
    public const char ByteOrderMark = (char)0xFEFF;
    public const char HighSurrogate = (char)0xD800;
    public const char LowSurrogate = (char)0xDC00;

    /// <summary>一個反斜線。和後面的文字分開寫，免得被當成 C# 的跳脫字元。</summary>
    public const string Backslash = "\\";

    public static string ExamplePath(string fileName) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Examples", fileName);

    public static string ReadExample(string fileName) => File.ReadAllText(ExamplePath(fileName));

    /// <summary>JSON 與 YAML 的 flow 寫法都適用：depth 層陣列包著一個 0。</summary>
    public static string NestedArrays(int depth) =>
        new string('[', depth) + "0" + new string(']', depth);

    public static string NestedJsonObjects(int depth) =>
        Repeat("{\"k\":", depth) + "0" + new string('}', depth);

    /// <summary>depth 層 YAML block mapping，最裡面是 k: 0。</summary>
    public static string NestedBlockMappings(int depth)
    {
        var builder = new StringBuilder();
        for (var level = 0; level < depth; level++)
        {
            builder.Append(' ', level * 2).Append(level == depth - 1 ? "k: 0" : "k:").Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>一個陣列裡放 count 個 0，總共 count + 1 個節點。JSON 與 YAML 的 flow 寫法都適用。</summary>
    public static string FlowArrayOfZeros(int count) => "[" + string.Join(",", Enumerable.Repeat("0", count)) + "]";

    /// <summary>一個 JSON 物件有 count 個屬性，值都是 0。</summary>
    public static string JsonObjectWithProperties(int count) =>
        "{" + string.Join(",", Enumerable.Range(0, count).Select(index => $"\"k{index}\":0")) + "}";

    /// <summary>一個 YAML block mapping 有 count 個鍵，值都是 0。</summary>
    public static string YamlMappingWithKeys(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(index => $"k{index}: 0\n"));

    public static string YamlBlockSequenceOfZeros(int count) => string.Concat(Enumerable.Repeat("- 0\n", count));

    public static string Repeat(string text, int count) => new StringBuilder(text.Length * count).Insert(0, text, count).ToString();
}
