using System.Text.Json.Serialization;
using PmTool.Ai.Assistant;

namespace PmTool.Ai.LocalAgents;

/// <summary><c>ai.local_agent_events.content</c> 的形狀。和合約的 <see cref="LocalAgentEvent"/> 分開：存的格式不跟著合約的型別走。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StoredText), "text")]
[JsonDerivedType(typeof(StoredTool), "tool")]
internal abstract record StoredEvent
{
    public static string Serialize(LocalAgentEvent progress) => AssistantJson.Serialize<StoredEvent>(progress switch
    {
        LocalAgentText text => new StoredText(text.Delta),
        LocalAgentTool tool => new StoredTool(tool.Tool.CallId, tool.Tool.Name, tool.Tool.Status, tool.Tool.ChangeSetNumber, tool.Tool.Error, tool.Tool.ItemProposalId, tool.Tool.ProjectProposalId, tool.Tool.NewItemProposalId, tool.Tool.MemberProposalId, tool.Tool.ItemDeletionProposalId, tool.Tool.ItemBatchProposalId, tool.Tool.ProposalId),
        _ => throw new ArgumentOutOfRangeException(nameof(progress), progress, "不認得的進度種類。"),
    });

    public static LocalAgentEvent Deserialize(string json) => AssistantJson.Deserialize<StoredEvent>(json) switch
    {
        StoredText text => new LocalAgentText(text.Delta),
        StoredTool tool => new LocalAgentTool(new AssistantToolPart(tool.CallId, tool.Name, tool.Status, tool.ChangeSetNumber, tool.Error) { ItemProposalId = tool.ItemProposalId, ProjectProposalId = tool.ProjectProposalId, NewItemProposalId = tool.NewItemProposalId, MemberProposalId = tool.MemberProposalId, ItemDeletionProposalId = tool.ItemDeletionProposalId, ItemBatchProposalId = tool.ItemBatchProposalId, ProposalId = tool.ProposalId }),
        _ => throw new FormatException("不認得的進度。"),
    };
}

internal sealed record StoredText(string Delta) : StoredEvent;

internal sealed record StoredTool(
    string CallId, string Name, AssistantToolStatus Status, long? ChangeSetNumber, string? Error, Guid? ItemProposalId = null, Guid? ProjectProposalId = null, Guid? NewItemProposalId = null, Guid? MemberProposalId = null, Guid? ItemDeletionProposalId = null, Guid? ItemBatchProposalId = null, Guid? ProposalId = null) : StoredEvent;
