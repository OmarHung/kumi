namespace PmTool.Cli;

/// <summary>結束代碼。腳本與 CI 靠它判斷，不能隨便改。</summary>
public static class ExitCodes
{
    public const int Success = 0;

    /// <summary>一般錯誤：連不上、沒登入、找不到、沒有權限、伺服器出錯。</summary>
    public const int Error = 1;

    /// <summary>驗證失敗：文件不合規定、欄位的值不對、不允許的轉換，以及指令的用法不對。</summary>
    public const int Invalid = 2;

    /// <summary>計畫裡有需要人決定的資料影響，或現在還擋著套用。</summary>
    public const int NeedsDecision = 3;
}

/// <summary>要結束指令的失敗。訊息寫到標準錯誤，細項（路徑加訊息）接在後面。</summary>
internal sealed class CliException(
    int exitCode, string message, IReadOnlyList<(string Path, string Message)>? details = null, string? code = null, int? httpStatus = null)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    /// <summary>伺服器回的錯誤代碼（例如 attachment.too_large）；不是伺服器回的錯誤時是 null。</summary>
    public string? Code { get; } = code;

    /// <summary>伺服器回的 HTTP 狀態碼；不是伺服器回的錯誤時是 null。</summary>
    public int? HttpStatus { get; } = httpStatus;

    public IReadOnlyList<(string Path, string Message)> Details { get; } = details ?? [];

    public static CliException Usage(string message) => new(ExitCodes.Invalid, message);
}
