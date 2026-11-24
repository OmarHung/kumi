using System.Reflection;
using Microsoft.AspNetCore.TestHost;

namespace PmTool.Api.Tests.Support;

/// <summary>
/// 釋放 ASP.NET Core 限流中介層自己的「端點限流器」。它有一個每 0.1 秒跑一次的計時器，中介層卻不會在主機關閉時釋放它；
/// 計時器記下了建主機時的執行內容（裡面有測試框架的 HostingListener），連帶整個 factory、TestServer 與容器都放不掉。
/// 正式環境一個程序只有一個主機，不受影響；測試一輪開兩千多個，記憶體與計時器越積越多，後面的測試慢好幾倍。
/// 中介層與欄位都是框架內部的，只能用反射找：沿著請求管線的 <c>_next</c>（委派）往下走。
/// 框架改版找不到時回 false，<see cref="ApiFactoryLeakTests"/> 會失敗，提醒更新這裡。
/// </summary>
internal static class RateLimiterCleanup
{
    private const string MiddlewareName = "RateLimitingMiddleware";
    private const string LimiterField = "_endpointLimiter";
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool DisposeEndpointLimiter(TestServer server)
    {
        var application = typeof(TestServer).GetProperty("Application", InstanceFields)?.GetValue(server);
        var pending = new Queue<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Enqueue(application, pending, visited);
        while (pending.TryDequeue(out var node))
        {
            if (node.GetType().Name == MiddlewareName)
            {
                return DisposeLimiterOf(node);
            }

            foreach (var next in NextNodes(node))
            {
                Enqueue(next, pending, visited);
            }
        }

        return false;
    }

    private static bool DisposeLimiterOf(object middleware)
    {
        var field = middleware.GetType().GetField(LimiterField, InstanceFields);
        if (field?.GetValue(middleware) is not IDisposable limiter)
        {
            return false;
        }

        limiter.Dispose();
        return true;
    }

    /// <summary>
    /// 只往「下一個中介層」的方向走：委派的 Target，以及型別是委派或屬於 ASP.NET Core 主機、測試主機的欄位
    /// （包住管線的 ApplicationWrapper、HostingApplication）。不走進路由、容器這些大的物件圖。
    /// </summary>
    private static IEnumerable<object?> NextNodes(object node)
    {
        if (node is Delegate handler)
        {
            yield return handler.Target;
            yield break;
        }

        foreach (var field in node.GetType().GetFields(InstanceFields))
        {
            if (typeof(Delegate).IsAssignableFrom(field.FieldType) || IsHostingType(field.FieldType))
            {
                yield return field.GetValue(node);
            }
        }
    }

    private static bool IsHostingType(Type type) =>
        type.Namespace is "Microsoft.AspNetCore.Hosting" or "Microsoft.AspNetCore.TestHost"
        || (type.IsInterface && type.Name.StartsWith("IHttpApplication", StringComparison.Ordinal));

    private static void Enqueue(object? node, Queue<object> pending, HashSet<object> visited)
    {
        if (node is not null && visited.Add(node))
        {
            pending.Enqueue(node);
        }
    }
}
