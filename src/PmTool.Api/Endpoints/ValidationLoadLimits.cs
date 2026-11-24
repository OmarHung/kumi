namespace PmTool.Api.Endpoints;

/// <summary>
/// 驗證端點保護自己用的兩個數字。預設值是 <see cref="ApiLimits"/> 的常數；
/// 獨立成一個可以替換的物件，測試才不必真的等十秒、開十幾條連線。
/// 送出變更集讀的是同一種文件，讀內文的期限也用這裡的（同時處理的數量則不共用，見 <see cref="ChangeSetEndpoints"/>）。
/// </summary>
public sealed record ValidationLoadLimits
{
    public int MaxConcurrentRequests { get; init; } = ApiLimits.MaxConcurrentValidations;

    public TimeSpan BodyReadTimeout { get; init; } = TimeSpan.FromSeconds(ApiLimits.BodyReadTimeoutSeconds);
}
