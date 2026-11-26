using PmTool.Work;

namespace PmTool.Ai.ProjectProposals;

/// <summary>新專案建議的欄位長度（資料庫欄位用；真正的規則由工作引擎的 <c>CheckNewProjectAsync</c> 檢查）。</summary>
internal static class ProjectProposalLimits
{
    public const int MaxNameLength = WorkRules.MaxProjectNameLength;

    /// <summary>模板的 key 與顯示名稱。</summary>
    public const int MaxTemplateLength = 100;
}
