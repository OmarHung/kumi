namespace PmTool.Ai.ItemRevisions;

/// <summary>刪除與批次修改建議用的長度上限。</summary>
internal static class ItemRevisionLimits
{
    /// <summary>刪除原因（卡片上的一行小字）最長幾個字。</summary>
    public const int MaxReasonLength = 200;

    /// <summary>建立刪除建議時，列出「沒有一起選的下層」最多幾個編號。</summary>
    public const int MaxChildrenListed = 10;

    /// <summary>找下層時每個項目最多讀幾個直接下層（超過的不再列）。</summary>
    public const int ChildrenScanLimit = 50;
}
