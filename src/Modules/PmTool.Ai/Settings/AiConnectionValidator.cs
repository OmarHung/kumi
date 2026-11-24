using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;
using PmTool.Http;

namespace PmTool.Ai.Settings;

/// <summary>金鑰怎麼處理：沒有、沿用存的、清掉、換成新的。</summary>
internal enum ApiKeyChange
{
    Keep,
    Clear,
    Replace,
}

/// <summary>檢查過、整理好的連線（還沒加密金鑰）。</summary>
internal sealed record ValidatedConnection(
    string Name,
    string NameKey,
    AiProviderKind Kind,
    string? BaseUrl,
    ApiKeyChange KeyChange,
    string? NewApiKey,
    IReadOnlyList<string> EnabledModels,
    IReadOnlyList<string> CustomModels)
{
    // 帶著金鑰，不能在記錄、例外訊息或測試失敗的輸出裡露出來。
    public override string ToString() =>
        $"ValidatedConnection {{ Name = {Name}, Kind = {Kind}, BaseUrl = {BaseUrl}, KeyChange = {KeyChange}, NewApiKey = {(NewApiKey is null ? "null" : "***")} }}";
}

/// <summary>連線欄位的檢查（internal/HANDOFF-ai-assistant.md 第 4.6 節的表）。細項的路徑是 camelCase 的欄位名稱。</summary>
internal static class AiConnectionValidator
{
    private const string KindPath = "kind";
    private const string NamePath = "name";
    private const string BaseUrlPath = "baseUrl";
    private const string ApiKeyPath = "apiKey";
    private const string EnabledModelsPath = "enabledModels";
    private const string CustomModelsPath = "customModels";

    /// <param name="existing">修改時已經存的連線（新增是 null）：金鑰沿用時算有金鑰，也要確認沒有換目的地。</param>
    public static Result<ValidatedConnection> Validate(
        AiConnectionInput input, AiConnectionRow? existing, bool allowPrivateEndpoints, bool personal = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        var problems = new List<FailureDetail>();

        if (input.Kind.IsLocalEngine())
        {
            // 本機的引擎是「那台電腦的那個引擎」，由本機代理登記；沒有金鑰也沒有網址，不能存成連線。
            problems.Add(Invalid(KindPath, "Claude Code 與 Codex 要在自己的電腦執行 kumi agent start 才會出現，不能存成連線。"));
            return Invalid(problems);
        }

        var name = CheckName(input.Name, problems);
        var baseUrl = CheckBaseUrl(input.Kind, input.BaseUrl, allowPrivateEndpoints, personal, problems);
        var key = CheckApiKey(input, baseUrl, existing, problems);
        var enabled = CheckModelIds(input.EnabledModels, EnabledModelsPath, AiRules.MaxEnabledModels, input.Kind, problems);
        var custom = CheckModelIds(input.CustomModels, CustomModelsPath, AiRules.MaxCustomModels, input.Kind, problems);

        return problems.Count > 0 || name is null
            ? Invalid(problems)
            : new ValidatedConnection(
                name, KeyOf(name), input.Kind, baseUrl, key.Change, key.Value, enabled, custom);
    }

    /// <summary>名稱轉大寫（不分文化）：組織裡不分大小寫不能重複。</summary>
    public static string KeyOf(string name) => name.ToUpperInvariant();

    /// <summary>「讀取模型」用：只看供應商、網址、金鑰（<paramref name="existing"/> 是 <c>connectionId</c> 找到的連線）。</summary>
    public static Result<ValidatedConnection> ValidateForDiscovery(
        AiDiscoveryInput input, AiConnectionRow? existing, bool allowPrivateEndpoints, bool personal = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asConnection = new AiConnectionInput("discovery", input.Kind, input.BaseUrl, input.ApiKey, [], []);
        return Validate(asConnection, existing, allowPrivateEndpoints, personal);
    }

    private static Failure Invalid(List<FailureDetail> problems) =>
        new(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "連線的設定有幾個欄位不對。") { Details = problems };

    private static string? CheckName(string? raw, List<FailureDetail> problems)
    {
        var name = Clean(raw, NamePath, AiRules.MaxNameLength, problems);
        if (name is null && problems.All(p => p.Path != NamePath))
        {
            problems.Add(Required(NamePath, "要填連線的名稱。"));
        }

        return name;
    }

    private static string? CheckBaseUrl(AiProviderKind kind, string? raw, bool allowPrivate, bool personal, List<FailureDetail> problems)
    {
        if (kind != AiProviderKind.OpenaiCompatible)
        {
            return null;
        }

        var text = Clean(raw, BaseUrlPath, AiRules.MaxBaseUrlLength, problems);
        if (text is null)
        {
            if (problems.All(p => p.Path != BaseUrlPath))
            {
                problems.Add(Required(BaseUrlPath, "OpenAI 相容的端點要填基底網址。"));
            }

            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || text.Contains('?', StringComparison.Ordinal)
            || text.Contains('#', StringComparison.Ordinal))
        {
            problems.Add(Invalid(BaseUrlPath, "基底網址要是 http 或 https 的完整網址，不能帶帳號密碼、? 或 #。"));
            return null;
        }

        if (!allowPrivate && PublicAddressGuard.IsBlockedLiteral(uri))
        {
            problems.Add(new FailureDetail(
                AiErrorCodes.EndpointNotAllowed,
                BaseUrlPath,
                personal
                    ? "這個位址是內網或本機，自己的 AI 帳號不能連內部網路的位址，請組織管理員接成組織的連線。"
                    : "這個位址是內網或本機，這個部署不允許連（Ai:AllowPrivateEndpoints）。"));
            return null;
        }

        var normalized = text.TrimEnd('/');
        if (normalized.Length > AiRules.MaxBaseUrlLength)
        {
            problems.Add(Invalid(BaseUrlPath, $"基底網址最長 {AiRules.MaxBaseUrlLength} 個字。"));
        }

        return normalized;
    }

    private static (ApiKeyChange Change, string? Value) CheckApiKey(
        AiConnectionInput input, string? baseUrl, AiConnectionRow? existing, List<FailureDetail> problems)
    {
        var requiresKey = input.Kind != AiProviderKind.OpenaiCompatible;
        var (change, value) = input.ApiKey switch
        {
            null => existing is null ? (ApiKeyChange.Clear, null) : (ApiKeyChange.Keep, null),
            "" => (ApiKeyChange.Clear, null),
            var text => (ApiKeyChange.Replace, text.Trim()),
        };

        if (change == ApiKeyChange.Replace)
        {
            if (value!.Length > AiRules.MaxApiKeyLength)
            {
                problems.Add(Invalid(ApiKeyPath, $"金鑰最長 {AiRules.MaxApiKeyLength} 個字。"));
            }
            else if (value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                problems.Add(Invalid(ApiKeyPath, "金鑰不能有空白或控制字元。"));
            }
        }

        var hasKey = change switch
        {
            ApiKeyChange.Keep => existing?.ProtectedApiKey is not null,
            ApiKeyChange.Replace => true,
            _ => false,
        };

        if (requiresKey && !hasKey)
        {
            problems.Add(Required(ApiKeyPath, "要填金鑰。"));
        }
        else if (change == ApiKeyChange.Keep && hasKey && ChangedDestination(existing!, input.Kind, baseUrl))
        {
            // 不然另一個管理員把網址指到自己的主機、金鑰留白，伺服器就會把存的金鑰送過去。
            problems.Add(Required(ApiKeyPath, "改了供應商或基底網址，要重新輸入金鑰。"));
        }

        return (change, value);
    }

    private static bool ChangedDestination(AiConnectionRow existing, AiProviderKind kind, string? baseUrl) =>
        AiEnumNames.ParseKind(existing.Kind) != kind || !string.Equals(existing.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);

    private static List<string> CheckModelIds(
        IReadOnlyList<string>? ids, string path, int max, AiProviderKind kind, List<FailureDetail> problems)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < (ids?.Count ?? 0); i++)
        {
            var id = ids![i]?.Trim();
            if (string.IsNullOrEmpty(id) || id.Length > AiRules.MaxModelIdLength || id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                problems.Add(Invalid($"{path}[{i}]", $"模型代號不能是空的、不能有空白或控制字元，最長 {AiRules.MaxModelIdLength} 個字。"));
            }
            else if (kind == AiProviderKind.Openrouter && id.Contains('#', StringComparison.Ordinal))
            {
                // OpenRouterHandler 用 # 把思考深度附在代號後面，代號裡有 # 會被誤讀。OpenRouter 的代號不會有。
                problems.Add(Invalid($"{path}[{i}]", "OpenRouter 的模型代號不能有 #。"));
            }
            else if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        if (result.Count > max)
        {
            problems.Add(Invalid(path, $"最多 {max} 個。"));
        }

        return result;
    }

    private static string? Clean(string? raw, string path, int max, List<FailureDetail> problems)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Any(char.IsControl))
        {
            problems.Add(Invalid(path, "不能有控制字元。"));
            return null;
        }

        if (text.Length > max)
        {
            problems.Add(Invalid(path, $"最長 {max} 個字。"));
            return null;
        }

        return text;
    }

    private static FailureDetail Required(string path, string message) => new(AiErrorCodes.FieldRequired, path, message);

    private static FailureDetail Invalid(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);
}
