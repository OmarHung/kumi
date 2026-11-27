namespace PmTool.Ai.Proposals;

/// <summary>建議的種類與狀態的名字：給模型的工具結果（小寫的英文代號）與狀態訊息（中文）用，兩邊只有這一份。</summary>
internal static class ProposalNames
{
    public static string KindCode(ProposalKind kind) => kind switch
    {
        ProposalKind.ChangeSet => "changeset",
        ProposalKind.ItemChanges => "item_changes",
        ProposalKind.BulkItemChanges => "bulk_item_changes",
        ProposalKind.NewItems => "new_items",
        ProposalKind.ItemDeletions => "item_deletions",
        ProposalKind.Members => "members",
        ProposalKind.Project => "project",
        ProposalKind.Planning => "planning",
        ProposalKind.Comments => "comments",
        ProposalKind.ItemRestores => "item_restores",
        ProposalKind.ProjectChanges => "project_changes",
        ProposalKind.Action => "action",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的建議種類。"),
    };

    public static string StateCode(ProposalState state) => state switch
    {
        ProposalState.Pending => "pending",
        ProposalState.Applying => "applying",
        ProposalState.Applied => "applied",
        ProposalState.Dismissed => "dismissed",
        ProposalState.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "不認得的建議狀態。"),
    };

    public static string KindLabel(ProposalKind kind) => kind switch
    {
        ProposalKind.ChangeSet => "變更集",
        ProposalKind.ItemChanges => "修改項目",
        ProposalKind.BulkItemChanges => "批次修改項目",
        ProposalKind.NewItems => "新增項目",
        ProposalKind.ItemDeletions => "刪除項目",
        ProposalKind.Members => "成員變動",
        ProposalKind.Project => "新專案",
        ProposalKind.Planning => "週期與里程碑",
        ProposalKind.Comments => "留言",
        ProposalKind.ItemRestores => "復原項目",
        ProposalKind.ProjectChanges => "改專案",
        ProposalKind.Action => "動作（知識庫、人員、附件、檢視或說明格式）",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的建議種類。"),
    };

    public static string StateLabel(ProposalState state) => state switch
    {
        ProposalState.Pending => "等待中（使用者還沒按）",
        ProposalState.Applying => "建立中",
        ProposalState.Applied => "已套用",
        ProposalState.Dismissed => "已略過（使用者按了「不要」、撤回或過期）",
        ProposalState.Rejected => "已否決",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "不認得的建議狀態。"),
    };
}
