using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PmTool.Blueprint;

namespace PmTool.Cli.Documents;

/// <summary>一份讀進來的文件：原文、格式，以及它的來源名稱（錯誤訊息用）。</summary>
internal sealed record DocumentText(string Text, DocumentFormat Format, string Source)
{
    /// <summary>YAML 檔第一行的 `# project: KEY`。JSON 沒有地方放註解，所以只在 YAML 找。</summary>
    private static readonly Regex ProjectComment = new(@"^#\s*project:\s*(?<key>[A-Za-z0-9_-]+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private const int HeaderCharacters = 400;

    public static async Task<DocumentText> ReadAsync(string path, TextReader standardInput, CancellationToken cancellationToken)
    {
        if (path == "-")
        {
            var piped = await standardInput.ReadToEndAsync(cancellationToken);
            return new DocumentText(piped, Sniff(piped), "標準輸入");
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return new DocumentText(text, FormatOf(path, text), path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliException(ExitCodes.Error, $"讀不到檔案 {path}：{exception.Message}");
        }
    }

    /// <summary>--project 沒給時，用檔案第一段註解裡的 `# project: KEY`。</summary>
    public string? ProjectKeyFromComment()
    {
        if (Format != DocumentFormat.Yaml)
        {
            return null;
        }

        var header = Text.Length > HeaderCharacters ? Text[..HeaderCharacters] : Text;
        var match = ProjectComment.Match(header);
        return match.Success ? match.Groups["key"].Value : null;
    }

    /// <summary>頂層的屬性名稱；讀不懂就回 null，讓驗證去回報真正的問題。</summary>
    public IReadOnlyList<string>? TopLevelKeys() => ToJson() is JsonObject root ? [.. root.Select(pair => pair.Key)] : null;

    /// <summary>
    /// 讀成 JSON 樹。用的是 PmTool.Blueprint 讀文件的那一份程式，和伺服器讀出來的值、接受與拒絕的寫法都一樣。
    /// 讀不懂回 null；要知道哪裡不對用 <see cref="Parse"/>。
    /// </summary>
    public JsonNode? ToJson() => Parse() is { Succeeded: true } parsed ? parsed.Root : null;

    public ParsedDocument Parse() => BlueprintDocumentParser.Parse(Text, Format);

    private static DocumentFormat FormatOf(string path, string text) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => DocumentFormat.Json,
        ".yaml" or ".yml" => DocumentFormat.Yaml,
        _ => Sniff(text),
    };

    private static DocumentFormat Sniff(string text) =>
        text.TrimStart().StartsWith('{') ? DocumentFormat.Json : DocumentFormat.Yaml;

    /// <summary>把整份文件換成 JSON 文字（加上 summary 時用）。</summary>
    public static string ToJsonText(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
