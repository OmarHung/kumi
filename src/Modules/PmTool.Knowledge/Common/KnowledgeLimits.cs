namespace PmTool.Knowledge.Common;

/// <summary>
/// 垃圾桶的上限與一次永久刪除的量。正式環境一律是 <see cref="KnowledgeRules"/> 的常數；做成可以注入的值，
/// 是為了讓測試用幾批就測得到邊界，不必真的建一千批。
/// </summary>
internal sealed record KnowledgeLimits(
    int MaxTrashBatchesPerSpace = KnowledgeRules.MaxTrashBatchesPerSpace,
    int MaxBatchesPerPurge = KnowledgeRules.MaxBatchesPerPurge,
    int MaxTrashPagesPerSpace = KnowledgeRules.MaxTrashPagesPerSpace,
    int MaxPagesPerPurge = KnowledgeRules.MaxPagesPerPurge)
{
    public static readonly KnowledgeLimits Default = new();
}
