namespace PmTool.Abstractions;

/// <summary>
/// 模組現在能不能服務請求，例如資料庫連不連得上。給就緒檢查用；
/// 存活檢查（/health）不看這個，否則資料庫一抖，平台就把所有容器重啟。
/// </summary>
public interface IReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
