namespace PmTool.Api.Mcp;

/// <summary>工具的參數不合格。訊息給代理看（它通常會自己修正再呼叫），所以寫成工具的錯誤，不是協定的錯誤。</summary>
internal sealed class McpArgumentException(string message) : Exception(message);
