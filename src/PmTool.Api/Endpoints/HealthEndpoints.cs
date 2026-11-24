using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Responses;

namespace PmTool.Api.Endpoints;

internal sealed record HealthStatus(string Status);

internal static class HealthEndpoints
{
    public const string Route = "/health";
    public const string ReadyRoute = "/health/ready";

    private static readonly HealthStatus Ok = new("ok");

    public static IServiceCollection AddReadinessCheck(this IServiceCollection services)
    {
        services.AddSingleton(new ReadinessLimits());
        services.AddSingleton<ReadinessCheck>();
        return services;
    }

    /// <summary>
    /// 健康檢查不套用限流：它和用戶端流量常來自同一個來源 IP（例如同一台代理），
    /// 額度用完時平台會把健康的容器誤判成壞掉而重啟。成功的回應照容器平台的慣例，不包信封。
    /// 存活（/health）只看程序活著；就緒（/health/ready）才看各模組，資料庫一抖不會讓平台把容器重啟。
    /// 路由一比對到就直接執行（ShortCircuit），不經過後面的確認登入與限流：它不需要知道是誰，
    /// 讀了 Cookie 就可能去問身分模組，而它又不限流，等於讓任何人都能替資料庫加壓。
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, () => TypedResults.Ok(Ok))
            .WithName("Health")
            .ShortCircuit();
        app.MapGet(ReadyRoute, ReadyAsync)
            .WithName("Ready")
            .ShortCircuit();
        return app;
    }

    private static async Task<IResult> ReadyAsync([FromServices] ReadinessCheck readiness, CancellationToken cancellationToken) =>
        await readiness.IsReadyAsync(cancellationToken) ? Results.Ok(Ok) : ApiFailure.NotReady.ToResult();
}
