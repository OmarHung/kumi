namespace PmTool.Api.Json;

/// <summary>端點自己的 JSON 內文上限（位元組）。沒有宣告的端點用 <see cref="ApiLimits.MaxJsonBodyBytes"/>。</summary>
internal sealed record JsonBodyLimit(int MaxBytes)
{
    public static int For(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<JsonBodyLimit>()?.MaxBytes ?? ApiLimits.MaxJsonBodyBytes;
}

internal static class JsonBodyLimitExtensions
{
    public static TBuilder WithJsonBodyLimit<TBuilder>(this TBuilder builder, int maxBytes)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new JsonBodyLimit(maxBytes));
}
