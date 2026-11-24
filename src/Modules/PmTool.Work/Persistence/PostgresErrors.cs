using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PmTool.Work.Persistence;

internal static class PostgresErrors
{
    /// <summary>違反了指定的唯一限制。只認限制名稱，其他唯一限制出錯仍然是例外，不會被誤當成使用者的錯。</summary>
    public static bool IsUniqueViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == constraintName;

    /// <summary>同上，但不限 SaveChanges：ExecuteUpdate 與 ExecuteSql 直接丟出 PostgresException，所以內層也要看。</summary>
    public static bool IsUniqueViolation(Exception exception, string constraintName) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres && postgres.ConstraintName == constraintName
        || (exception.InnerException is { } inner && IsUniqueViolation(inner, constraintName));

    /// <summary>
    /// 等鎖超過 lock_timeout。查詢與 ExecuteSql 直接丟出 PostgresException，SaveChanges 會包成 DbUpdateException，所以內層也要看。
    /// </summary>
    public static bool IsLockTimeout(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }
        || (exception.InnerException is { } inner && IsLockTimeout(inner));

    /// <summary>
    /// 被 PostgreSQL 選為互鎖的犧牲者（40P01）。EF 的執行策略會把它包成 InvalidOperationException（「可能是暫時的失敗」），
    /// SaveChanges 包成 DbUpdateException，所以一路往內層看。
    /// </summary>
    public static bool IsDeadlock(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected }
        || (exception.InnerException is { } inner && IsDeadlock(inner));
}
