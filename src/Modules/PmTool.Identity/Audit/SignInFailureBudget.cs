using Microsoft.Extensions.Logging;

namespace PmTool.Identity.Audit;

/// <summary>
/// 登入失敗寫進稽核的額度：每個程序每分鐘有總量的上限，每個帳號每分鐘另有自己的上限。
/// 每個帳號每分鐘的第一筆失敗一定寫得進去，不受總量影響：灌大量失敗也蓋不掉對某個帳號的第一次失敗。
/// 用時間比對、不用計時器；記帳號的表有上限，換分鐘時整張清掉。
/// </summary>
internal sealed class SignInFailureBudget(
    TimeProvider time, ILogger logger, int perMinuteLimit, int perAccountLimit, int maxTrackedAccounts)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, int> _byAccount = [];
    private long _minute = long.MinValue;
    private int _failuresInWindow;
    private bool _overflowLogged;

    public SignInFailureBudget(TimeProvider time, ILogger logger)
        : this(time, logger, AuditRules.MaxSignInFailuresPerMinute, AuditRules.MaxSignInFailuresPerAccountPerMinute, MaxTrackedAccountsDefault)
    {
    }

    public const int MaxTrackedAccountsDefault = 10_000;

    /// <summary>這個帳號這一筆失敗能不能寫。成功的不用問，不限量。</summary>
    public bool TryTake(Guid accountId)
    {
        var minute = time.GetUtcNow().UtcTicks / TimeSpan.TicksPerMinute;
        lock (_gate)
        {
            if (minute != _minute)
            {
                _minute = minute;
                _failuresInWindow = 0;
                _overflowLogged = false;
                _byAccount.Clear();
            }

            if (_byAccount.TryGetValue(accountId, out var forAccount))
            {
                if (forAccount >= perAccountLimit || !TakeFromWindow())
                {
                    return false;
                }

                _byAccount[accountId] = forAccount + 1;
                return true;
            }

            // 這一分鐘的第一筆：一定寫得進去。表滿了記不下的帳號只好和別人一樣看總量，表才不會被撐大。
            if (_byAccount.Count >= maxTrackedAccounts)
            {
                return TakeFromWindow();
            }

            _failuresInWindow++;
            _byAccount[accountId] = 1;
            return true;
        }
    }

    private bool TakeFromWindow()
    {
        if (_failuresInWindow < perMinuteLimit)
        {
            _failuresInWindow++;
            return true;
        }

        if (!_overflowLogged)
        {
            _overflowLogged = true;
            logger.LogWarning("這一分鐘登入失敗超過 {Limit} 筆，其餘的不再寫進稽核紀錄。", perMinuteLimit);
        }

        return false;
    }
}
