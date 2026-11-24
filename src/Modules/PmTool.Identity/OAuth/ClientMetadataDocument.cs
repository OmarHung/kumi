using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PmTool.Identity.OAuth;

/// <summary>讀過、驗過的 Client ID Metadata Document。</summary>
internal sealed record ClientMetadataDocument(string Name, IReadOnlyList<string> RedirectUris)
{
    /// <summary>
    /// 驗證文件（<c>internal/HANDOFF-mcp.md</c> 4.4）：<c>client_id</c> 要等於網址本身；名稱必填；
    /// 導回網址至少一個、每個都合規定；只收公開用戶端。不合時 <paramref name="error"/> 是給人看的原因。
    /// </summary>
    public static bool TryParse(string json, string expectedClientId, [NotNullWhen(true)] out ClientMetadataDocument? document, out string error)
    {
        document = null;
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = parsed.RootElement;
            error = root.ValueKind == JsonValueKind.Object ? Validate(root, expectedClientId, out document) : "用戶端的資料不是 JSON 物件。";
        }
        catch (JsonException)
        {
            error = "用戶端的資料不是正確的 JSON。";
        }

        return document is not null;
    }

    private static string Validate(JsonElement root, string expectedClientId, out ClientMetadataDocument? document)
    {
        document = null;
        if (StringOf(root, "client_id") != expectedClientId)
        {
            return "用戶端資料裡的 client_id 和網址不一致。";
        }

        var name = OAuthText.CleanName(StringOf(root, "client_name"), OAuthRules.MaxClientNameLength);
        if (name is null)
        {
            return "用戶端的資料缺少名稱（client_name），或名稱不合規定。";
        }

        if (root.TryGetProperty("token_endpoint_auth_method", out var method) && method.ValueKind != JsonValueKind.Null
            && !(method.ValueKind == JsonValueKind.String && method.GetString() == "none"))
        {
            return "只支援公開用戶端（token_endpoint_auth_method 要是 none）。";
        }

        if (!TryReadRedirectUris(root, out var uris))
        {
            return "用戶端資料裡的導回網址（redirect_uris）缺少，或有不合規定的網址。";
        }

        document = new ClientMetadataDocument(name, uris);
        return string.Empty;
    }

    private static bool TryReadRedirectUris(JsonElement root, out IReadOnlyList<string> uris)
    {
        uris = [];
        if (!root.TryGetProperty("redirect_uris", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var found = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !OAuthRedirects.TryParse(item.GetString(), out _))
            {
                return false;
            }

            found.Add(item.GetString()!);
        }

        uris = [.. found.Distinct(StringComparer.Ordinal)];
        return uris.Count is >= 1 and <= OAuthRules.MaxRedirectUris;
    }

    private static string? StringOf(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
