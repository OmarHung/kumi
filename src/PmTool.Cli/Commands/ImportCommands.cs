namespace PmTool.Cli.Commands;

/// <summary>kumi import：把別的工具的資料搬進來。目前只有 monday（internal/HANDOFF-monday-import.md）。</summary>
internal static class ImportCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("import", "monday fetch", "從 monday 抓看板的資料，存成 dump 資料夾（不寫進這個伺服器）",
            "kumi import monday fetch --board 編號 [--board 編號…] --out 資料夾 [--mapping 對應檔] [--files] [--json]",
            [
                new("board", "編號", "monday 看板的編號（網址裡的數字）；可以重複給多個", null, Repeatable: true),
                new("out", "資料夾", "dump 的輸出資料夾；已經是 dump 的話整個換成新的"),
                new("mapping", "對應檔", "只抓對應檔裡沒有 skip 的群組"),
                new("files", "", "下載留言與項目裡的檔案（每個上限 25 MiB）到 dump 的 files 資料夾"),
            ],
            0, 0, MondayFetchCommand.RunAsync),
        new("import", "monday plan", "從 dump 產生對應檔的草稿（人員依 Email 對上；需要已登入）",
            "kumi import monday plan --dump 資料夾 --out 對應檔 [--force] [--json]",
            [
                new("dump", "資料夾", "kumi import monday fetch 的輸出"),
                new("out", "對應檔", "草稿寫到這個 YAML 檔"),
                new("force", "", "對應檔已經存在時覆蓋它"),
            ],
            0, 0, MondayPlanCommand.RunAsync),
        new("import", "monday run", "照對應檔把 dump 匯進這個伺服器（保留作者與時間、不發通知、可以重跑）",
            "kumi import monday run --mapping 對應檔 [--dry-run] [--add-members [--member-role 角色]] [--report 檔案] [--json]",
            [
                new("mapping", "對應檔", "kumi import monday plan 產生、你改好的對應檔"),
                new("dry-run", "", "只檢查（含請伺服器逐列檢查項目），不寫入任何東西"),
                new("add-members", "", "缺的成員以 member 加進專案（需要 blueprint:write 權杖範圍）；不加的話缺成員列成問題"),
                new("member-role", "角色", "--add-members 加進專案的角色（藍圖裡的角色 key，預設 editor）；每個目標專案的藍圖都要有這個角色"),
                new("report", "檔案", "另外把報告寫成 JSON 檔"),
            ],
            0, 0, MondayRunCommand.RunAsync),
    ];

    /// <summary>相對路徑以指令的「目前資料夾」為準（測試用假的資料夾隔開真正的目前資料夾）。</summary>
    internal static string Resolve(CommandCall call, string path) =>
        Path.GetFullPath(path, call.Context.WorkingDirectory);
}
