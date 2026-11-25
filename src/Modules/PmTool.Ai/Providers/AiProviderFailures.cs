using System.ClientModel;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.Exceptions;
using PmTool.Http;

namespace PmTool.Ai.Providers;

/// <summary>把連供應商時的例外說成人看得懂的原因（internal/HANDOFF-ai-assistant.md 第 4.5 節）。助理的回覆失敗時用同一份。</summary>
internal static partial class AiProviderFailures
{
    private const int MaxSnippetLength = 300;

    /// <summary>
    /// 自己的連線連不上時的唯一一句話（AI-04／05）：位址被擋、解析不到、連線被拒、被擋的連接埠都是它。
    /// 成員填的端點不能被拿來分辨「內網有沒有這台機器」，詳細的原因只寫記錄。
    /// </summary>
    internal const string PersonalUnreachable = "連不上這個端點，請確認網址是從網路上連得到的 https 位址。";

    /// <summary>繁體中文，不超過 <see cref="AiRules.MaxErrorLength"/>，不含金鑰。</summary>
    public static string Describe(Exception exception) => Describe(exception, host: null);

    /// <param name="host">連不上時說明連的是哪一台；不知道就說「供應商」。</param>
    /// <param name="personal">成員自己的連線：位址被擋時說明改成請管理員接成組織的連線。</param>
    /// <param name="secret">
    /// 這個連線的金鑰原文：出現在供應商的話裡就遮掉。樣式只認得 sk-、AIza 這類有前綴的金鑰，
    /// 相容端點自訂的 token 沒有特徵，只能靠這個比對。
    /// </param>
    public static string Describe(Exception exception, string? host, string? secret = null, bool personal = false)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var chain = Flatten(exception).ToList();
        var text = Redact(DescribeChain(chain, host, secret, personal), secret);
        return AiText.Ellipsize(text, AiRules.MaxErrorLength);
    }

    private static string Redact(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "***", StringComparison.Ordinal);

    private static string DescribeChain(List<Exception> chain, string? host, string? secret, bool personal)
    {
        if (chain.OfType<AiProviderException>().FirstOrDefault() is { } ours)
        {
            return Clean(ours.Message);
        }

        if (chain.OfType<BlockedAddressException>().FirstOrDefault() is { } blocked)
        {
            return personal
                ? PersonalUnreachable
                : $"{Clean(blocked.Host)} 是內網或本機的位址，這個部署不允許連（Ai:AllowPrivateEndpoints）。";
        }

        foreach (var exception in chain)
        {
            if (StatusOf(exception) is { } status)
            {
                // 自己的端點是成員填的網址：對方回的話不原樣轉出來（它可以拿來回顯內網探測到的內容）。
                return DescribeStatus(status, personal ? null : SnippetOf(exception, secret));
            }
        }

        if (chain.Any(e => e is OperationCanceledException or TimeoutException))
        {
            return "供應商太久沒有回應。";
        }

        if (chain.Any(e => e is HttpRequestException or SocketException))
        {
            if (personal)
            {
                return PersonalUnreachable;
            }

            return string.IsNullOrWhiteSpace(host) ? "連不上供應商。" : $"連不上 {Clean(host)}。";
        }

        return "呼叫供應商時出錯了。";
    }

    private static string DescribeStatus(int status, string? snippet)
    {
        var withSnippet = (string text) => string.IsNullOrEmpty(snippet) ? text : $"{text}：{snippet}";
        return status switch
        {
            401 or 403 => "金鑰不對，或這把金鑰沒有權限。",
            402 => withSnippet("帳號的額度不夠（餘額用完，或這把金鑰的額度用完了），請到供應商那邊儲值或調整額度"),
            404 => withSnippet("找不到（模型代號不對，或基底網址不對）"),
            429 => withSnippet("供應商說太頻繁或額度用完了，稍後再試"),
            400 => withSnippet("供應商不接受這個請求（例如這個模型不支援這個思考深度）"),
            >= 500 => "供應商那邊出錯了，稍後再試。",
            _ => withSnippet($"供應商回了 {status}"),
        };
    }

    private static int? StatusOf(Exception exception) => exception switch
    {
        AnthropicApiException anthropic => (int)anthropic.StatusCode,
        ClientResultException client when client.Status > 0 => client.Status,
        Google.GenAI.ApiException google when google.StatusCode > 0 => google.StatusCode,
        HttpRequestException { StatusCode: { } code } => (int)code,
        _ => null,
    };

    private static string? SnippetOf(Exception exception, string? secret)
    {
        var raw = exception switch
        {
            AnthropicApiException anthropic => anthropic.ResponseBody,
            ClientResultException client => BufferedBodyOf(client) ?? client.Message,
            _ => exception.Message,
        };
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // 先遮再截斷：截斷會把金鑰切成一半，剩下的前半段就比對不到了。
        var text = ExtractJsonMessage(Redact(raw, secret)) ?? Redact(raw, secret);
        var cleaned = AiText.Ellipsize(Clean(text), MaxSnippetLength);
        return cleaned.Length == 0 ? null : cleaned;
    }

    // 本文沒有讀進來的回應取 Content 會丟 InvalidOperationException，那就用例外自己的訊息。
    private static string? BufferedBodyOf(ClientResultException exception)
    {
        try
        {
            return exception.GetRawResponse()?.Content.ToString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // 各家的錯誤本文都是 {"error":{"message":…}} 之類的 JSON；有就只取那一句，沒有就用原文。
    private static string? ExtractJsonMessage(string raw)
    {
        var start = raw.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw[start..]);
            return FindMessage(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindMessage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString();
        }

        return element.TryGetProperty("error", out var error) ? FindMessage(error) : null;
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>去掉控制字元、收斂空白，並把像金鑰或「key=…」的片段遮住：供應商的話與 SDK 的訊息都可能帶出請求的內容。</summary>
    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        var redacted = KeyParameterPattern().Replace(builder.ToString(), "$1=***");
        redacted = KeyShapePattern().Replace(redacted, "***");
        redacted = BearerPattern().Replace(redacted, "Bearer ***");
        return WhitespacePattern().Replace(redacted, " ").Trim();
    }

    [GeneratedRegex(@"(?i)\b(api[_-]?key|key|token|authorization|bearer)[""']?\s*[:=]\s*[""']?[^\s""',&]+")]
    private static partial Regex KeyParameterPattern();

    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9._~+/\-]{6,}")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"\b(?:sk|pk|AIza)[A-Za-z0-9_\-]{8,}")]
    private static partial Regex KeyShapePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
