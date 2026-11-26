using PmTool.Abstractions;
using PmTool.Ai.LocalAgents;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 本機代理或主機的 worker 這個來源讀得到哪些附件（internal/HANDOFF-ai-attachments.md 第 8 節）：
/// 文字檔一律讀得到；圖片與 PDF 要 kumi 在 <see cref="AiRules.MinAgentVersionForAttachments"/> 以上，而且 Codex 讀不到 PDF（只收圖片）。
/// </summary>
/// <param name="Version">本機看那台電腦登記的、主機看 worker 回報的 kumi 版本；不知道是 null（當作太舊）。</param>
/// <param name="Name">能選清單裡的名稱，錯誤訊息用它說是哪一個來源。</param>
internal sealed record AgentSource(AiProviderKind Engine, string? Version, bool Hosted, string Name)
{
    public bool SupportsBinary => AgentVersions.IsAtLeast(Version, AiRules.MinAgentVersionForAttachments);

    public bool Accepts(AssistantAttachmentKind kind) => kind switch
    {
        AssistantAttachmentKind.Text => true,
        AssistantAttachmentKind.Image => SupportsBinary,
        _ => SupportsBinary && Engine != AiProviderKind.Codex,
    };

    /// <summary>送出時：這一則帶的附件這個來源讀不讀得到；讀不到的第一個原因，什麼都不存。</summary>
    public Failure? Check(IReadOnlyCollection<AssistantAttachmentKind> kinds)
    {
        var binary = kinds.Where(kind => kind != AssistantAttachmentKind.Text).ToList();
        if (binary.Count == 0)
        {
            return null;
        }

        if (!SupportsBinary)
        {
            return AttachmentFailures.AgentTooOld(this);
        }

        return binary.Contains(AssistantAttachmentKind.Pdf) && Engine == AiProviderKind.Codex ? AttachmentFailures.CodexNoPdf(this) : null;
    }
}
