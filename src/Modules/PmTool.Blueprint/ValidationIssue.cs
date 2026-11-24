namespace PmTool.Blueprint;

/// <summary>一個驗證問題。Code 給程式判斷用，不會隨語系改變；Path 是 JSON Pointer。</summary>
public sealed record ValidationIssue(string Code, string Path, string Message);
