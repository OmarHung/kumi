using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Api.Cli;
using PmTool.Api.Composition;
using PmTool.Api.Endpoints;
using PmTool.Api.Hosting;
using PmTool.Api.Json;
using PmTool.Api.Live;
using PmTool.Api.Mcp;
using PmTool.Api.OAuth;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Blueprint;

// 最先裝：之後任何一步丟出沒接住的例外，程序都用結束代碼離開，平台才會重啟它（理由見 CrashExit）。
CrashExit.Install();

var builder = WebApplication.CreateBuilder(MigrateCommand.HostArguments(args));

builder.Services.AddOpenApi();
builder.Services.AddApiJson();
builder.Services.AddApiRateLimiting();
builder.Services.AddSignInThrottle();
builder.Services.AddValidationEndpoints();
builder.Services.AddClientErrorReporting();
builder.Services.AddReadinessCheck();
builder.Services.AddTransportSecurity(builder.Configuration);
builder.Services.AddSessionAuthentication(builder.Configuration);
builder.Services.TryAddSingleton(TimeProvider.System);

// 用工廠委派註冊：測試換成假的驗證器時，真的那一個（載入內嵌 Schema）不會被建立。
builder.Services.AddSingleton<IBlueprintValidator>(_ => BlueprintValidator.CreateDefault());
builder.Services.AddModules(builder.Configuration);
builder.Services.AddCliDownloads(builder.Configuration);
builder.Services.AddLive(builder.Configuration);
builder.Services.AddOAuthServer();
builder.Services.AddMcpServerEndpoint();

var app = builder.Build();

// 同一個映像檔的另一種用法：只遷移，不接請求。
if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(app);
}

// 啟動時就建立驗證器。內嵌的 Schema 有問題時讓程序直接起不來，部署會停在舊版；
// 等到第一個請求才建立的話，健康檢查是好的，每個驗證請求卻都回 500。
_ = app.Services.GetRequiredService<IBlueprintValidator>();
app.WarnIfKeysAreNotEncrypted();
app.WarnAboutTransportSettings();
await app.RunStartupTasksAsync();

// 順序有意義：
// - 安全標頭在最外層，連轉到 https 的 308 與 500 都帶得到；
// - 接著讀轉送的標頭，之後的每一層（轉到 https、Cookie 的 Secure、限流的來源 IP）看到的都是用戶端的，不是代理的；
// - 轉到 https 排在其他東西之前，轉走的請求不讀 Cookie、不吃限流的額度；
// - 不需要知道是誰的請求（靜態檔、網頁的後備、健康檢查）都在確認登入之前就回應，不讀 Cookie，也不吃限流的額度；
// - 限流要先知道是誰，所以排在確認登入之後；主機代理的機器憑證與工具授權（不是任何人）也在限流之前驗證，限流才能依代理、依工作算；
// - 範圍的檢查要看端點的宣告，所以排在路由之後、授權之前：沒登入的請求不受影響，由授權回 401；
// - 即時連線的來源與名額檢查也看端點的宣告，同樣排在授權之前。
app.UseSecurityHeaders();
app.UseForwardedProxyHeaders();
app.UseHttpsRequirement();
app.UseEnvelopeErrorResponses();
app.UseWebAssets();
app.UseRouting();
app.UseSpaFallback();
app.UseAuthentication();
app.UseHostCredentials();
app.UseRateLimiter();
app.UseCsrfProtection();
app.UseTokenScopeCheck();
app.UseLiveGuards();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapValidationEndpoints();
app.MapSessionEndpoints();
app.MapSignedInEndpoints();
app.MapCliLoginTokenEndpoint();
app.MapHostAgentWorkerEndpoints();
app.MapLive();
app.MapOAuthEndpoints();
app.MapMcpEndpoint();

await app.RunAsync();
return 0;

public partial class Program;
