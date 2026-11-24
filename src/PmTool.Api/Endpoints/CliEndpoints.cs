using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Cli;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

internal sealed record CliPackageData(string Platform, string FileName, long SizeBytes, string Sha256);

internal sealed record CliData(string? Version, IReadOnlyList<CliPackageData> Packages);

/// <summary>kumi 的執行檔下載。<c>kumi update</c> 用的是登入時存的那把權杖，範圍不一定，所以任何一把有效的權杖都可以。</summary>
internal static class CliEndpoints
{
    public const string Sha256Header = "X-Kumi-Sha256";

    private const string OctetStream = "application/octet-stream";
    private const string CacheControl = "private, max-age=0";

    public static RouteGroupBuilder MapCliEndpoints(this RouteGroupBuilder app)
    {
        app.MapGet("/cli", GetCli).WithName("GetCli").AllowAnyAccessToken();
        app.MapGet("/cli/download/{platform}", Download).WithName("DownloadCli").AllowAnyAccessToken();
        return app;
    }

    private static IResult GetCli([FromServices] CliPackageCatalog catalog) =>
        Results.Ok(ApiEnvelope.Ok(new CliData(
            catalog.Version,
            [.. catalog.Packages.Select(p => new CliPackageData(p.Platform, p.FileName, p.SizeBytes, p.Sha256))])));

    private static IResult Download(string platform, HttpContext context, [FromServices] CliPackageCatalog catalog)
    {
        // 平台代碼只拿來查目錄；檔案路徑一律來自目錄。
        if (catalog.Find(platform) is not { } package)
        {
            return ApiFailure.CliPackageNotFound.ToResult();
        }

        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = AttachmentDownload.ContentSecurityPolicy;
        headers.CacheControl = CacheControl;
        headers.ContentDisposition = AttachmentDownload.DispositionOf("attachment", package.FileName);
        headers[Sha256Header] = package.Sha256;
        return Results.File(package.FilePath, OctetStream, enableRangeProcessing: true);
    }
}
