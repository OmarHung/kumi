using System.Text.Json;

namespace PmTool.Work.Templates;

/// <summary>
/// 讀範例檔裡一筆東西的形狀（有哪些鍵、型別對不對）的共同部分；內容對不對交給檢查的類別。
/// 問題寫成一句完整的話，開頭說明是哪個模板、哪一種、第幾筆，改檔的人不必猜。
/// </summary>
internal abstract class ExampleShapeReader
{
    private readonly List<string> _problems = [];

    public IReadOnlyList<string> Problems => _problems;

    protected abstract string Describe(string path, string message);

    protected void Add(string path, string message) => _problems.Add(Describe(path, message));

    protected void RejectUnknownKeys(JsonElement element, IReadOnlyCollection<string> known)
    {
        foreach (var property in element.EnumerateObject().Where(p => !known.Contains(p.Name)))
        {
            Add(property.Name, "不認得這個鍵。");
        }
    }

    protected string? Text(JsonElement element, string key, bool required)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                Add(key, "必填。");
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        Add(key, "要寫文字。");
        return null;
    }

    protected int? Days(JsonElement element, string key, bool required = false)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                Add(key, "必填。");
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var days) && days is >= -ExampleFields.MaxDays and <= ExampleFields.MaxDays)
        {
            return days;
        }

        Add(key, $"要寫成相對建立當天的天數（整數，絕對值不超過 {ExampleFields.MaxDays}）。");
        return null;
    }
}
