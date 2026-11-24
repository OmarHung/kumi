using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>讀模型給的參數。值可能是 JsonElement，也可能已經是 .NET 的字串；一律不信任，缺的或型別不對就回 null。</summary>
internal static class ToolArguments
{
    public static string? String(AIFunctionArguments arguments, string name) => arguments.TryGetValue(name, out var value)
        ? value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        }
        : null;

    /// <summary>
    /// 一個 JSON 物件。模型有時會把物件包成字串送過來（提示裡寫了不要，但還是會發生），字串裡是 JSON 物件的話照收。
    /// 回傳物件的原文。
    /// </summary>
    public static string? ObjectText(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Object } element => element.GetRawText(),
            JsonElement { ValueKind: JsonValueKind.String } element => ObjectTextOf(element.GetString()),
            string text => ObjectTextOf(text),
            _ => null,
        };
    }

    private static string? ObjectTextOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int? Int(AIFunctionArguments arguments, string name) =>
        arguments.TryGetValue(name, out var value)
            ? value switch
            {
                int number => number,
                JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
                _ => null,
            }
            : null;

    /// <summary>
    /// 一個 JSON 陣列。模型有時把陣列包成字串送來，字串裡是 JSON 陣列的話照收。不是陣列是 null。
    /// 回傳的元素已經複製，不受來源文件的生命週期影響。
    /// </summary>
    public static IReadOnlyList<JsonElement>? Array(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        var element = value switch
        {
            JsonElement json => json,
            string text => TryParse(text),
            _ => default,
        };
        if (element.ValueKind == JsonValueKind.String)
        {
            element = TryParse(element.GetString());
        }

        return element.ValueKind == JsonValueKind.Array ? [.. element.EnumerateArray().Select(e => e.Clone())] : null;
    }

    private static JsonElement TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
