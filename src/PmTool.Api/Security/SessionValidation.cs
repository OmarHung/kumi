using Microsoft.AspNetCore.Authentication.Cookies;
using PmTool.Identity;

namespace PmTool.Api.Security;

/// <summary>
/// 每個帶 Cookie、需要知道是誰的請求都會經過這裡，每次都拿票證裡的權杖向身分模組確認伺服器端的工作階段：
/// 登出、改密碼、停用帳號作廢的工作階段，下一個請求就失效。查詢結果由身分模組快取，這裡不另外快取。
/// 閒置與絕對期限也由伺服器端的紀錄判斷，Cookie 本身不再滑動延長。
/// </summary>
internal static class SessionValidation
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        // 沒有權杖的是改成伺服器端工作階段之前發的票證，請他重新登入。
        if (SessionPrincipal.TryReadToken(context.Principal) is not { } token)
        {
            await RejectAsync(context);
            return;
        }

        var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionService>();
        var session = await sessions.FindAsync(token, context.HttpContext.RequestAborted);
        if (session is null)
        {
            await RejectAsync(context);
            return;
        }

        // 帳號的資料（名稱、角色）變了才換新的票證；沒變就不換，每個回應才不會都帶 Set-Cookie。
        if (SessionPrincipal.TryRead(context.Principal) != session.User)
        {
            context.ReplacePrincipal(SessionPrincipal.Create(session.User, token));

            // 換發時框架把期限算成「現在加上原本發出到到期的長度」。把發出的時刻改成現在，
            // 新的票證才停在伺服器端的絕對期限，不會每換發一次就多出幾天。
            context.Properties.IssuedUtc = (context.Options.TimeProvider ?? TimeProvider.System).GetUtcNow();
            context.Properties.ExpiresUtc = session.ExpiresAt;
            context.ShouldRenew = true;
        }
    }

    /// <summary>
    /// 只拒絕，不順便刪 Cookie：換發（登出其他裝置、改密碼）之後，瀏覽器可能還有請求帶著舊的 Cookie 在路上，
    /// 它的回應如果帶著刪 Cookie 的標頭，會把剛換上的新 Cookie 一起刪掉，所有分頁都被登出。
    /// 無效的 Cookie 留著沒有害處（每次都被拒絕，結果由身分模組快取）；明確的登出端點才刪。
    /// </summary>
    private static Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        return Task.CompletedTask;
    }
}
