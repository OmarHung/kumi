using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Notifications;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 要存的寄信設定。列舉是 snake_case（<c>provider</c> 是 gmail、smtp；<c>security</c> 是 start_tls、tls、none）。
/// <c>password</c> 是 null 或省略時沿用已經存的密碼，空字串是清掉。
/// </summary>
internal sealed record EmailSettingsRequest(
    bool Enabled,
    EmailProvider Provider,
    string? Host = null,
    int? Port = null,
    SmtpSecurity? Security = null,
    string? Username = null,
    string? Password = null,
    string? FromAddress = null,
    string? FromName = null,
    string? SiteUrl = null)
{
    public EmailSettingsInput ToInput() =>
        new(Enabled, Provider, Host, Port, Security, Username, Password, FromAddress, FromName, SiteUrl);

    // 帶著密碼，不能在記錄或例外訊息裡露出來。
    public override string ToString() =>
        $"EmailSettingsRequest {{ Enabled = {Enabled}, Provider = {Provider}, Host = {Host}, Port = {Port}, Security = {Security}, "
        + $"Username = {Username}, Password = {(Password is null ? "null" : "***")}, FromAddress = {FromAddress}, FromName = {FromName}, SiteUrl = {SiteUrl} }}";
}

/// <summary>寄信設定在 API 上的形狀。密碼不會出現，只說有沒有存（<c>hasPassword</c>）。</summary>
internal sealed record EmailSettingsData(
    bool Enabled,
    EmailProvider Provider,
    string? Host,
    int? Port,
    SmtpSecurity? Security,
    string? Username,
    bool HasPassword,
    string? FromAddress,
    string? FromName,
    string? SiteUrl,
    EmailStatusData Status,
    DateTimeOffset? UpdatedAt,
    PersonReference? UpdatedBy)
{
    public static EmailSettingsData From(EmailSettings settings, PeopleNames names) => new(
        settings.Enabled,
        settings.Provider,
        settings.Host,
        settings.Port,
        settings.Security,
        settings.Username,
        settings.HasPassword,
        settings.FromAddress,
        settings.FromName,
        settings.SiteUrl,
        new EmailStatusData(settings.Status.LastSentAt, settings.Status.LastFailedAt, settings.Status.LastError),
        settings.UpdatedAt,
        settings.UpdatedBy is { } person ? new PersonReference(person, names.NameOf(person)) : null);
}

internal sealed record EmailStatusData(DateTimeOffset? LastSentAt, DateTimeOffset? LastFailedAt, string? LastError);

/// <param name="SentTo">測試信寄到哪裡：登入的人自己的 Email。</param>
internal sealed record EmailTestResultData(string SentTo);

internal sealed record EmailPreferenceRequest(bool Enabled);

internal sealed record EmailPreferenceData(bool Enabled, bool OrganizationEnabled);

/// <summary>
/// 寄信設定（組織管理員）與自己的 Email 通知開關。
/// 設定一律只能在網頁上用 Cookie 登入操作：不宣告權杖範圍，個人存取權杖不能用。
/// 誰能看、能改由通知模組判斷（<see cref="Actor"/> 帶著是不是組織管理員），API 不重複一份規則。
/// </summary>
internal static class EmailSettingsEndpoints
{
    public static RouteGroupBuilder MapEmailEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/email-settings", GetSettingsAsync).WithName("GetEmailSettings");
        v1.MapPut("/email-settings", SaveSettingsAsync).WithName("SaveEmailSettings");

        // 每一次都會讓伺服器去連管理員填的主機，依人限流。
        v1.MapPost("/email-settings/test", SendTestAsync)
            .WithName("SendTestEmail")
            .RequireRateLimiting(ApiRateLimiting.EmailTestPolicy);

        v1.MapGet("/account/email-notifications", GetPreferenceAsync).WithName("GetEmailPreference");
        v1.MapPut("/account/email-notifications", SetPreferenceAsync).WithName("SetEmailPreference");
        return v1;
    }

    private static async Task<IResult> GetSettingsAsync(
        ClaimsPrincipal principal,
        [FromServices] IEmailSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return await RespondAsync(await settings.GetAsync(actor, cancellationToken), actor.OrganizationId, people, cancellationToken);
    }

    private static async Task<IResult> SaveSettingsAsync(
        JsonBody<EmailSettingsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IEmailSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return await RespondAsync(
            await settings.SaveAsync(actor, request.ToInput(), cancellationToken), actor.OrganizationId, people, cancellationToken);
    }

    private static async Task<IResult> SendTestAsync(
        JsonBody<EmailSettingsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IEmailSettingsService settings,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        // 收件地址一律是登入的人自己的 Email，不收本文裡的地址：不能拿它寄信給別人。
        var user = SessionPrincipal.Read(principal);
        var result = await settings.SendTestAsync(user.ToActor(), request.ToInput(), user.Email, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new EmailTestResultData(user.Email));
    }

    private static async Task<IResult> GetPreferenceAsync(
        ClaimsPrincipal principal, [FromServices] IEmailPreferenceService preferences, CancellationToken cancellationToken)
    {
        var preference = await preferences.GetAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return EndpointResults.Ok(new EmailPreferenceData(preference.Enabled, preference.OrganizationEnabled));
    }

    private static async Task<IResult> SetPreferenceAsync(
        JsonBody<EmailPreferenceRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IEmailPreferenceService preferences,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var preference = await preferences.SetAsync(SessionPrincipal.Read(principal).ToActor(), request.Enabled, cancellationToken);
        return EndpointResults.Ok(new EmailPreferenceData(preference.Enabled, preference.OrganizationEnabled));
    }

    private static async Task<IResult> RespondAsync(
        PmTool.Abstractions.Result<EmailSettings> result, Guid organizationId, IPeopleDirectory people, CancellationToken cancellationToken)
    {
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var settings = result.Value;
        var names = await PeopleNames.LookupAsync(people, organizationId, settings.UpdatedBy is { } id ? [id] : [], cancellationToken);
        return EndpointResults.Ok(EmailSettingsData.From(settings, names));
    }
}
