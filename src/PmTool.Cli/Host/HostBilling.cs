using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PmTool.Cli.Host;

/// <summary>伺服器的 <c>HostBilling</c>（JSON 是 snake_case）。</summary>
internal static class Billing
{
    public const string Api = "api";
    public const string Subscription = "subscription";
    public const string Unknown = "unknown";
}

/// <summary>從 CLI 的登入狀態判斷「有沒有登入、用什麼計費」。只看判斷需要的欄位：email、組織名稱、金鑰的片段一律不往外送、不印。</summary>
internal static class BillingClassifier
{
    /// <summary>
    /// <c>claude auth status</c>：JSON 的 <c>loggedIn</c> 為 false 是沒登入；<c>authMethod</c> 是 <c>claude.ai</c> → 訂閱、<c>api_key</c> → API，
    /// 其他（<c>oauth</c> 權杖、雲端供應商、沒有）→ 不明。輸出不是 JSON 時，只相信結束代碼。
    /// </summary>
    public static LoginStatus Claude(int exitCode, string standardOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(standardOutput);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new LoginStatus(exitCode == 0, Billing.Unknown, null);
            }

            var loggedIn = root.TryGetProperty("loggedIn", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (!loggedIn)
            {
                return LoginStatus.LoggedOut;
            }

            var method = Text(root, "authMethod");
            var billing = method switch
            {
                "claude.ai" => Billing.Subscription,
                "api_key" => Billing.Api,
                _ => Billing.Unknown,
            };
            return new LoginStatus(true, billing, Fingerprint(method, Text(root, "orgId"), Text(root, "email"), Text(root, "apiKeySource")));
        }
        catch (JsonException)
        {
            return new LoginStatus(exitCode == 0, Billing.Unknown, null);
        }
    }

    /// <summary><c>codex login status</c>（訊息可能在標準錯誤）：結束代碼非 0 是沒登入；「Logged in using ChatGPT」→ 訂閱、「Logged in using an API key」→ API，其他 → 不明。</summary>
    public static LoginStatus Codex(int exitCode, string output)
    {
        if (exitCode != 0)
        {
            return LoginStatus.LoggedOut;
        }

        var billing = output.Contains("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase) ? Billing.Subscription
            : output.Contains("Logged in using an API key", StringComparison.OrdinalIgnoreCase) ? Billing.Api
            : Billing.Unknown;
        return new LoginStatus(true, billing, null);
    }

    /// <summary>Codex 的 <c>auth.json</c> 的指紋：登入方式、API 金鑰、ChatGPT 的 account_id；權杖與刷新時間不算（Codex 自己會改寫）。</summary>
    public static string? CodexFingerprint(JsonElement auth)
    {
        if (auth.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var account = auth.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object ? Text(tokens, "account_id") : null;
        return Fingerprint("codex", Text(auth, "auth_mode"), Text(auth, "OPENAI_API_KEY"), account);
    }

    /// <summary>登入帳號的指紋：之後有人在 Kumi 以外換了登入，指紋就不同，worker 不再替它報 Kumi 的登入編號。只存本機、不往外送。</summary>
    public static string Fingerprint(params string?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts.Select(part => part ?? string.Empty)))));

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal sealed record LoginStatus(bool LoggedIn, string Billing, string? Fingerprint)
{
    public static readonly LoginStatus LoggedOut = new(false, Host.Billing.Unknown, null);
}
