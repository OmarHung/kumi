using System.Text.RegularExpressions;

namespace PmTool.Ai.Providers;

/// <summary>
/// 每個模型能選的思考深度（internal/HANDOFF-ai-assistant.md 第 4.3 節）。這些規則會過時，所以只有這一份；
/// 依據的是 2026-11 各家的文件：Anthropic 的 Models API 回 <c>capabilities.effort</c>，OpenAI 與 Gemini 只能照模型代號推。
/// </summary>
internal static partial class AiEffortCatalog
{
    private static readonly AiEffort[] LowToHigh = [AiEffort.Low, AiEffort.Medium, AiEffort.High];

    private static readonly AiEffort[] NoneToHigh = [AiEffort.None, AiEffort.Low, AiEffort.Medium, AiEffort.High];

    private static readonly AiEffort[] NoneToXhigh = [AiEffort.None, AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh];

    private static readonly AiEffort[] ProLevels = [AiEffort.Medium, AiEffort.High, AiEffort.Xhigh];

    private static readonly AiEffort[] AnthropicAll = [AiEffort.Low, AiEffort.Medium, AiEffort.High, AiEffort.Xhigh, AiEffort.Max];

    /// <summary>Anthropic：Models API 的 capabilities.effort 裡各等級的 supported；沒有 capabilities 時整個是空的。</summary>
    public static IReadOnlyList<AiEffort> ForAnthropic(bool low, bool medium, bool high, bool xhigh, bool max)
    {
        var efforts = new List<AiEffort>(5);
        AddIf(efforts, low, AiEffort.Low);
        AddIf(efforts, medium, AiEffort.Medium);
        AddIf(efforts, high, AiEffort.High);
        AddIf(efforts, xhigh, AiEffort.Xhigh);
        AddIf(efforts, max, AiEffort.Max);
        return efforts;
    }

    /// <summary>
    /// 不指定思考深度時 Anthropic 的模型實際用的等級。出處：platform.claude.com/docs/en/build-with-claude/effort（2026-11）：
    /// opus-5-5、haiku-5-5 預設 medium；sonnet-5-5、fable-5-2、opus-5、sonnet-5、opus-4-7 預設 high。
    /// 文件沒寫的模型不猜（null）；只在那個值是模型能選的等級時才給（<paramref name="efforts"/>）。
    /// </summary>
    public static AiEffort? DefaultForAnthropic(string modelId, IReadOnlyList<AiEffort> efforts)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ArgumentNullException.ThrowIfNull(efforts);
        var family = DateSuffix().Replace(modelId, string.Empty);
        AiEffort? documented = family switch
        {
            "claude-opus-5-5" or "claude-haiku-5-5" => AiEffort.Medium,
            "claude-sonnet-5-5" or "claude-fable-5-2" or "claude-opus-5" or "claude-sonnet-5" or "claude-opus-4-7" => AiEffort.High,
            _ => null,
        };
        return documented is { } effort && efforts.Contains(effort) ? effort : null;
    }

    public static IReadOnlyList<AiEffort> ForOpenAi(string modelId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        if (modelId.StartsWith("o1", StringComparison.Ordinal)
            || modelId.StartsWith("o3", StringComparison.Ordinal)
            || modelId.StartsWith("o4", StringComparison.Ordinal))
        {
            return LowToHigh;
        }

        var match = Gpt5Pattern().Match(modelId);
        if (!match.Success)
        {
            return [];
        }

        if (modelId.Contains("-pro", StringComparison.Ordinal))
        {
            return ProLevels;
        }

        if (!match.Groups["minor"].Success)
        {
            return LowToHigh;
        }

        // 數字太大塞不進 int 的當作很新的版本，不丟例外。
        var minor = int.TryParse(match.Groups["minor"].ValueSpan, out var parsed) ? parsed : int.MaxValue;
        return minor >= 2 ? NoneToXhigh : NoneToHigh;
    }

    /// <param name="thinking">Google 的清單說這個模型會思考；手動加的模型不知道，傳 true 讓代號規則決定。</param>
    public static IReadOnlyList<AiEffort> ForGoogle(string modelId, bool thinking)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        var match = GeminiPattern().Match(modelId);
        if (!thinking || !match.Success)
        {
            return [];
        }

        // 2.5 用 thinkingBudget，不送；3 以上才有 thinkingLevel。
        return int.TryParse(match.Groups["major"].ValueSpan, out var major) && major >= 3 ? LowToHigh : [];
    }

    /// <summary>
    /// OpenRouter：模型清單的 <c>reasoning</c>。沒有 <c>reasoning</c> 的模型不能選（空的）。<paramref name="supported"/> 是它列的
    /// <c>supported_efforts</c>：<c>minimal</c> 與不認得的略過，由淺到深排好；是 null（沒有白名單）時當作 low、medium、high
    /// （OpenRouter 會把這三個等級對到各家自己的設定）。<paramref name="mandatory"/> 是不能關閉思考的模型，不列 none。
    /// </summary>
    public static IReadOnlyList<AiEffort> ForOpenRouter(bool hasReasoning, IEnumerable<string>? supported, bool mandatory)
    {
        if (!hasReasoning)
        {
            return [];
        }

        if (supported is null)
        {
            return LowToHigh;
        }

        var names = new HashSet<string>(supported, StringComparer.Ordinal);
        var efforts = new List<AiEffort>(6);
        AddIf(efforts, names.Contains("none") && !mandatory, AiEffort.None);
        AddIf(efforts, names.Contains("low"), AiEffort.Low);
        AddIf(efforts, names.Contains("medium"), AiEffort.Medium);
        AddIf(efforts, names.Contains("high"), AiEffort.High);
        AddIf(efforts, names.Contains("xhigh"), AiEffort.Xhigh);
        AddIf(efforts, names.Contains("max"), AiEffort.Max);
        return efforts;
    }

    /// <summary>手動加的模型：照代號推。相容端點與 OpenRouter 一律空的（OpenRouter 的清單有每個模型自己的等級，手動加的不知道，不送這個參數）。</summary>
    public static IReadOnlyList<AiEffort> ForCustom(AiProviderKind kind, string modelId) => kind switch
    {
        // 沒有 capabilities 可讀：目前的模型都支援這五個；不支援時供應商會回錯誤，畫面顯示原因。
        AiProviderKind.Anthropic => AnthropicAll,
        AiProviderKind.Openai => ForOpenAi(modelId),
        AiProviderKind.Google => ForGoogle(modelId, thinking: true),
        _ => [],
    };

    private static void AddIf(List<AiEffort> efforts, bool supported, AiEffort effort)
    {
        if (supported)
        {
            efforts.Add(effort);
        }
    }

    [GeneratedRegex(@"-\d{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateSuffix();

    [GeneratedRegex(@"^gpt-5(\.(?<minor>\d+))?(?![\w.])", RegexOptions.CultureInvariant)]
    private static partial Regex Gpt5Pattern();

    [GeneratedRegex(@"^gemini-(?<major>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex GeminiPattern();
}
