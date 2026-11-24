namespace PmTool.Api.Endpoints;

/// <summary>
/// 同時處理的驗證請求數量上限。每個請求讀內文時最多占住 1 MiB，數量沒有上限的話，
/// 匿名的用戶端只要開很多條連線、把內文送到一半停住，就能用完容器的記憶體。
/// 滿了就立刻拒絕，不排隊：排隊的請求一樣占著連線，只是把問題往後推。
/// </summary>
internal sealed class ValidationSlots(ValidationLoadLimits limits)
{
    private int _inUse;

    public TimeSpan BodyReadTimeout => limits.BodyReadTimeout;

    public bool TryEnter()
    {
        // 先加再檢查的寫法在剛好滿的時候會多拒絕一個本來進得來的請求，所以用比較後交換。
        while (true)
        {
            var current = Volatile.Read(ref _inUse);
            if (current >= limits.MaxConcurrentRequests)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _inUse, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    public void Exit() => Interlocked.Decrement(ref _inUse);
}
