using System.Text.RegularExpressions;

namespace PmTool.Abstractions;

/// <summary>
/// 遷移完要授予執行角色（<see cref="DatabaseConnections.RuntimeRole"/>）的權限：自己 schema 的讀寫，
/// 加上之後新建的資料表與序列的預設權限。每個模組授權自己的 schema，SQL 放在這裡，各模組才不會各寫一份而漸漸不同。
/// </summary>
public static partial class RuntimeRoleGrants
{
    /// <summary>角色名稱的規則，和部署文件寫的一樣。</summary>
    public const string RoleNamePattern = "^[a-z_][a-z0-9_]{0,62}$";

    // 比對時用 \z 而不是 $：.NET 的 $ 也接受結尾多一個換行。
    private const string StrictRoleNamePattern = @"^[a-z_][a-z0-9_]{0,62}\z";

    /// <summary>
    /// 角色名稱合不合規定。名稱會放進 GRANT 的 SQL（識別字不能用參數），所以只收小寫英數字與底線，
    /// 不必處理引號跳脫，也不會因為大小寫被 PostgreSQL 當成另一個角色。
    /// </summary>
    public static bool IsValidRoleName(string? name) => name is not null && RoleName().IsMatch(name);

    /// <summary>
    /// 授權用的諮詢鎖的鍵（固定值，內容是 "PMTLGRNT" 的位元組）。多個容器同時啟動並各自授權時，
    /// 同時改同一個物件的權限會撞到「tuple concurrently updated」；用交易層級的鎖排成一個一個做，交易結束就自動放開。
    /// </summary>
    public const long GrantLockKey = 0x504D544C_47524E54;

    /// <summary>授權的 SQL，依序在同一個交易裡執行；每一句都可以重跑。</summary>
    /// <param name="schema">模組的 schema。</param>
    /// <param name="role">執行的角色。</param>
    /// <param name="migrationsHistoryTable">
    /// 遷移紀錄表（在 <paramref name="schema"/> 底下）。執行的角色只能讀它（就緒檢查要看還有沒有沒套用的遷移）：
    /// 能改的話，下一次部署的遷移會跳過或重跑某一版，等於間接改了結構。
    /// </param>
    /// <param name="appendOnlyTables">
    /// 只需要新增與讀取的資料表（在 <paramref name="schema"/> 底下），例如加密金鑰：
    /// 執行的角色被攻破時，不能把既有的資料改掉、刪掉或清空。
    /// </param>
    /// <exception cref="ArgumentException">名稱不合 <see cref="RoleNamePattern"/>。</exception>
    public static IReadOnlyList<string> StatementsFor(
        string schema, string role, string migrationsHistoryTable, IReadOnlyList<string>? appendOnlyTables = null)
    {
        // schema 與資料表是模組自己的常數，照樣檢查：這些值都會原樣進 SQL。
        Require(schema, nameof(schema));
        Require(role, nameof(role));
        Require(migrationsHistoryTable, nameof(migrationsHistoryTable));

        var s = Quote(schema);
        var r = Quote(role);
        var appendOnly = (appendOnlyTables ?? []).Select(table =>
        {
            Require(table, nameof(appendOnlyTables));
            return $"REVOKE UPDATE, DELETE, TRUNCATE ON {s}.{Quote(table)} FROM {r}";
        });
        return
        [
            $"SELECT pg_advisory_xact_lock({GrantLockKey})",

            // 遷移用的角色就是執行的角色時，底下的 REVOKE 會把擁有者自己對遷移紀錄表的寫入也收掉，下一次遷移就寫不進去；
            // 與其留下一個之後才壞的狀態，不如現在就失敗。名稱已經驗證過，放進字串常值是安全的。
            $"DO $$ BEGIN IF current_user = '{role}' THEN RAISE EXCEPTION '{SameRoleMessage}'; END IF; END $$",
            $"GRANT USAGE ON SCHEMA {s} TO {r}",
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {s} TO {r}",
            $"REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON {s}.{Quote(migrationsHistoryTable)} FROM {r}",
            .. appendOnly,
            $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {s} TO {r}",

            // 沒寫 FOR ROLE，所以是「遷移的角色之後在這個 schema 建的物件」：之後的遷移新增的資料表不必再授權一次。
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {s} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {r}",
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {s} GRANT USAGE, SELECT ON SEQUENCES TO {r}",
        ];
    }

    /// <summary>不含 % 與引號：這句話原樣放進 RAISE EXCEPTION 的字串常值。</summary>
    public const string SameRoleMessage =
        "Database:RuntimeRole 要和遷移用的角色不同；要分開角色就要另外設 ConnectionStrings:Migrations（遷移用的連線）。";

    private static void Require(string name, string parameter)
    {
        if (!IsValidRoleName(name))
        {
            throw new ArgumentException($"名稱不合規定（{RoleNamePattern}）。", parameter);
        }
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";

    [GeneratedRegex(StrictRoleNamePattern, RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();
}
