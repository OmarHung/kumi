using System.Runtime.CompilerServices;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 關掉的應用程式實體要能被回收、不留下還在跑的計時器。API 的測試每個測試開一個實體、一輪兩千多個：
/// 限流器的計時器（每 0.1 秒一次）沒停，會抓著整個實體，記憶體與計時器越積越多，後面的測試慢好幾倍。
/// 計時器的數量是整個程序共用的，所以不和別的測試同時跑。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ApiFactoryLeakTests
{
    [Test]
    public async Task DisposedFactory_CanBeGarbageCollected()
    {
        // Arrange、Act
        var services = await StartAndDisposeAsync();
        var isCollected = await CollectAsync(services);

        // Assert
        Assert.That(isCollected, Is.True, "關掉的實體還被什麼抓著（例如沒停的計時器記下的執行內容）");
    }

    [Test]
    public async Task DisposedFactories_LeaveNoTimersRunning()
    {
        // Arrange（第一個實體會建立整個程序共用、只建一次的計時器，不算在內）
        await StartAndDisposeAsync();
        var before = Timer.ActiveCount;

        // Act
        for (var i = 0; i < 5; i++)
        {
            await StartAndDisposeAsync();
        }

        // Assert（修好之前每個實體留下三個：中介層的端點限流器與全域限流的兩個分區限流器）
        Assert.That(Timer.ActiveCount - before, Is.LessThanOrEqualTo(2));
    }

    // 不讓編譯器把實體留在呼叫端的區域變數裡：要測的是「沒有別人抓著」。
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> StartAndDisposeAsync()
    {
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/v1/session");
        return new WeakReference(factory.Services);
    }

    /// <summary>
    /// 剛停掉的計時器迴圈還要再跑一次排好的回呼才會結束，在那之前還抓著主機：多給一點時間、反覆回收。
    /// 真的漏掉的會一直抓著，等多久都回收不了。
    /// </summary>
    private static async Task<bool> CollectAsync(WeakReference reference)
    {
        for (var attempt = 0; attempt < MaxCollectAttempts && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(CollectRetryDelay);
        }

        return !reference.IsAlive;
    }

    private const int MaxCollectAttempts = 20;

    private static readonly TimeSpan CollectRetryDelay = TimeSpan.FromMilliseconds(100);
}
