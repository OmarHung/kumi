using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Providers;

/// <summary>每一次呼叫模型的參數（internal/HANDOFF-ai-assistant.md 第 4.4 節）。助理再加上工具。</summary>
internal static class AiRequestOptions
{
    /// <summary>輸出上限、思考深度（null 是不送）、Anthropic 的提示快取。回傳新的物件，呼叫端可以自己改。</summary>
    public static ChatOptions Create(AiProviderKind kind, string modelId, AiEffort? effort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var options = new ChatOptions { ModelId = modelId, MaxOutputTokens = AiRules.MaxOutputTokens };
        if (kind == AiProviderKind.Anthropic)
        {
            options.RawRepresentationFactory = _ => AnthropicParams(modelId, effort);
            return options;
        }

        if (kind == AiProviderKind.Openrouter)
        {
            // OpenAI 的 SDK 與 ChatOptions.Reasoning 都只送得出 reasoning_effort，而且只有 OpenAI 的幾個等級（沒有 max）；
            // OpenRouter 要的是 reasoning: { effort }。SDK 沒有公開的方法加任意欄位（Patch 是實驗性的、診斷不准關），
            // 所以把等級附在模型代號後面（OpenRouterHandler 讀完就拿掉，OpenRouter 的模型代號不會有 # 或空白）。
            if (effort is { } routed)
            {
                options.ModelId = OpenRouterHandler.WithEffort(modelId, AiEnumNames.NameOf(routed));
            }

            return options;
        }

        if (effort is { } level)
        {
            options.Reasoning = new ReasoningOptions { Effort = ToReasoningEffort(level) };
        }

        return options;
    }

    // 不走 ChatOptions.Reasoning：轉接層把「不思考」轉成 thinking: disabled（Opus 5.5 這類模型回 400），
    // 也沒有 max。不送 thinking 時，目前的模型預設就是 adaptive。Messages 是空的，轉接層會換成這次的對話。
    private static MessageCreateParams AnthropicParams(string modelId, AiEffort? effort) => new()
    {
        Model = modelId,
        MaxTokens = AiRules.MaxOutputTokens,
        Messages = [],
        CacheControl = new CacheControlEphemeral(),
        OutputConfig = effort is { } level ? new OutputConfig { Effort = ToAnthropicEffort(level) } : null,
    };

    private static Effort ToAnthropicEffort(AiEffort effort) => effort switch
    {
        AiEffort.Low => Effort.Low,
        AiEffort.Medium => Effort.Medium,
        AiEffort.High => Effort.High,
        AiEffort.Xhigh => Effort.Xhigh,
        AiEffort.Max => Effort.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "Anthropic 沒有這個思考深度。"),
    };

    private static ReasoningEffort ToReasoningEffort(AiEffort effort) => effort switch
    {
        AiEffort.None => ReasoningEffort.None,
        AiEffort.Low => ReasoningEffort.Low,
        AiEffort.Medium => ReasoningEffort.Medium,
        AiEffort.High => ReasoningEffort.High,
        AiEffort.Xhigh => ReasoningEffort.ExtraHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "這家供應商沒有這個思考深度。"),
    };
}
