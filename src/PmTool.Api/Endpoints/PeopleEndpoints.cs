using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Live;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <param name="Disabled">帳號被停用了：還在清單裡，工作項目與活動照樣顯示他，但不能登入。</param>
internal sealed record PersonData(Guid PersonId, string DisplayName, string? Email, OrganizationRole Role, bool Disabled)
{
    public static PersonData From(PersonSummary person) =>
        new(person.PersonId, person.DisplayName, person.Email, person.Role, person.IsDisabled);
}

internal sealed record CreatePersonRequest(string DisplayName, string Email, string Password, OrganizationRole Role);

internal sealed record ResetPasswordRequest(string NewPassword);

internal sealed record SetPersonRoleRequest(OrganizationRole Role);

internal static class PeopleEndpoints
{
    public static RouteGroupBuilder MapPeopleEndpoints(this RouteGroupBuilder v1)
    {
        // 人員目錄沒有權限檢查（它只看組織），所以由 API 擋掉訪客：訪客是客戶或外包，不該看到整個組織的人與 Email。
        v1.MapGet("/people", ListPeopleAsync)
            .WithName("ListPeople")
            .RequireAuthorization(SessionAuthentication.OrganizationMembersPolicy)
            .RequireTokenScope(AccessTokenScope.ItemsRead);

        // 誰可以新增、停用、重設密碼由身分模組判斷，API 不重複一份規則。
        v1.MapPost("/people", CreatePersonAsync).WithName("CreatePerson");

        // 路徑參數用字串收，自己解析：不是 GUID 時回 person.not_found，和「組織裡沒有這個人」一樣，
        // 而不是路由比對不到的 request.not_found（網頁就不必分辨兩種 404）。
        v1.MapPost("/people/{personId}/disable", DisableAsync).WithName("DisablePerson");
        v1.MapPost("/people/{personId}/enable", EnableAsync).WithName("EnablePerson");
        v1.MapPut("/people/{personId}/password", ResetPasswordAsync).WithName("ResetPersonPassword");
        v1.MapPut("/people/{personId}/role", SetRoleAsync).WithName("SetPersonRole");
        return v1;
    }

    private static async Task<IResult> ListPeopleAsync(
        ClaimsPrincipal principal, [FromServices] IPeopleDirectory people, CancellationToken cancellationToken)
    {
        var user = SessionPrincipal.Read(principal);
        var list = await people.ListAsync(user.OrganizationId, cancellationToken);
        return EndpointResults.Ok(list.Select(PersonData.From).ToArray());
    }

    private static async Task<IResult> CreatePersonAsync(
        JsonBody<CreatePersonRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var person = new NewPerson(request.DisplayName, request.Email, request.Password, request.Role);
        var result = await people.CreateAsync(actor, person, cancellationToken);

        // 沒有查單一個人的端點，所以不給 Location。
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(null, PersonData.From(result.Value));
    }

    private static Task<IResult> DisableAsync(
        string personId,
        ClaimsPrincipal principal,
        [FromServices] IPeopleDirectory people,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken) =>
        SetDisabledAsync(personId, disabled: true, principal, people, live, cancellationToken);

    private static Task<IResult> EnableAsync(
        string personId,
        ClaimsPrincipal principal,
        [FromServices] IPeopleDirectory people,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken) =>
        SetDisabledAsync(personId, disabled: false, principal, people, live, cancellationToken);

    private static async Task<IResult> SetDisabledAsync(
        string personId, bool disabled, ClaimsPrincipal principal, IPeopleDirectory people, ILiveNotifier live, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(personId, out var id))
        {
            return ApiFailure.PersonNotFound.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await people.SetDisabledAsync(actor, id, disabled, cancellationToken);
        if (result.Failure is null)
        {
            live.AccessChanged([id]);
        }

        return ToResult(result);
    }

    private static async Task<IResult> ResetPasswordAsync(
        string personId,
        JsonBody<ResetPasswordRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(personId, out var id))
        {
            return ApiFailure.PersonNotFound.ToResult();
        }

        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return ToResult(await people.ResetPasswordAsync(actor, id, request.NewPassword, cancellationToken));
    }

    private static async Task<IResult> SetRoleAsync(
        string personId,
        JsonBody<SetPersonRoleRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPeopleDirectory people,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(personId, out var id))
        {
            return ApiFailure.PersonNotFound.ToResult();
        }

        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await people.SetRoleAsync(actor, id, request.Role, cancellationToken);
        if (result.Failure is null)
        {
            live.AccessChanged([id]);
        }

        return ToResult(result);
    }

    private static IResult ToResult(Result<PersonSummary> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(PersonData.From(result.Value));
}
