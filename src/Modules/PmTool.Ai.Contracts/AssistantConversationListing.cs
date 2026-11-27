namespace PmTool.Ai;

/// <summary>
/// 跨專案的對話清單（<see cref="IAssistantService.ListAllAsync"/>，浮動面板的「對話」選單）的一則：
/// 對話加上它屬於哪個專案（工作引擎回的代號）；null 是不選專案的組織對話。
/// </summary>
public sealed record AssistantConversationListing(string? ProjectKey, AssistantConversationSummary Conversation);
