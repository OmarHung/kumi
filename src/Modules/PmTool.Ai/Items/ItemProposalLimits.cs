namespace PmTool.Ai.Items;

/// <summary>修改建議用的長度上限。項目編號是「專案代號（最長 10）-流水號」，留寬一點。</summary>
internal static class ItemProposalLimits
{
    public const int MaxItemKeyLength = 40;

    public const int MaxItemTitleLength = 300;

    /// <summary>說明超過這個長度的項目，不接受用建議改說明（模型讀不完整份，改寫會截掉內容）；請改用留言。</summary>
    public const int MaxEditableDescriptionLength = 20_000;

    /// <summary>自訂欄位的值：字串最長、陣列最多幾項。</summary>
    public const int MaxCustomStringLength = 50_000;

    public const int MaxCustomArrayItems = 200;
}
