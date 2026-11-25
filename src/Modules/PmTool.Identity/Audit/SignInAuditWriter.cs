using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Audit;

/// <summary>
/// 把一筆登入結果寫進稽核：找得到帳號才寫一列（沒有帳號就沒有組織可以放）；找不到只記 Warning，而且不帶完整的 Email。
/// 在背景的消費者裡跑，什麼都不能丟出去。
/// </summary>
internal sealed class SignInAuditWriter(
    IDbContextFactory<IdentityDbContext> contexts, AuditTrail trail, SignInFailureBudget budget, ILogger<SignInAuditWriter> logger)
{
    public async Task WriteAsync(SignInAttempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            await WriteCoreAsync(attempt, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "登入的稽核紀錄寫不進去。");
        }
    }

    private async Task WriteCoreAsync(SignInAttempt attempt, CancellationToken cancellationToken)
    {
        if (!EmailAddress.TryNormalize(attempt.Email, out var email))
        {
            logger.LogWarning("登入失敗：Email 格式不對（長度 {Length}）。", attempt.Email.Length);
            return;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Email == email)
            .Select(a => new { a.OrganizationId, a.PersonId })
            .FirstOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            logger.LogWarning("登入失敗：找不到這個 Email 的帳號（雜湊 {EmailHash}）。", HashOf(email));
            return;
        }

        if (!attempt.Succeeded && !budget.TryTake(account.PersonId))
        {
            return;
        }

        var record = new AuditRecord(
            account.OrganizationId, AuditActions.SignIn, attempt.Succeeded ? AuditOutcomes.Succeeded : AuditOutcomes.Failed)
        {
            ActorPersonId = account.PersonId,
            Source = new RequestSource(attempt.SourceAddress, attempt.UserAgent),
        };
        await trail.AddAsync(db, record, cancellationToken);
    }

    private static string HashOf(string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)))[..12];
}
