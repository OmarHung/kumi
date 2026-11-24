namespace PmTool.Cli.Import.Monday;

// monday 的資料讀進來之後的樣子（internal/HANDOFF-monday-import.md 第 4 節）。
// fetch 把 GraphQL 的回應原樣存成檔案（dump），MondayDumpReader.Load 讀成這些 record；run 只看這些 record，不碰 monday 的 JSON。
// 編號一律是字串：monday 的 ID 是很大的整數，有的地方給字串、有的給數字。

/// <param name="Directory">dump 的資料夾（絕對路徑）。</param>
/// <param name="SkippedFiles">fetch 沒下載的檔案：asset 編號 → 原因（files/skipped.json 的 reason：too_large、download_failed、no_url、io_error）。</param>
/// <param name="FetchedAt">fetch 開始的時間。</param>
internal sealed record MondayDump(
    string Directory,
    DateTimeOffset FetchedAt,
    IReadOnlyList<MondayUser> Users,
    IReadOnlyList<MondayBoard> Boards)
{
    public IReadOnlyDictionary<string, string> SkippedFiles { get; init; } = new Dictionary<string, string>();
}

/// <param name="Email">monday 帳號的 Email；拿來和這裡的人員對上。</param>
internal sealed record MondayUser(string Id, string Name, string? Email);

/// <param name="Columns">看板的欄（含 status 的標籤，見 <see cref="MondayColumn.Labels"/>）。</param>
/// <param name="SubitemColumns">子項目所在的看板的欄（沒有子項目的看板是空的）。</param>
/// <param name="Items">看板的項目（只有 fetch 時指定的群組；含封存的，<see cref="MondayItem.State"/>）。</param>
internal sealed record MondayBoard(
    string Id,
    string Name,
    IReadOnlyList<MondayGroup> Groups,
    IReadOnlyList<MondayColumn> Columns,
    IReadOnlyList<MondayColumn> SubitemColumns,
    IReadOnlyList<MondayItem> Items);

internal sealed record MondayGroup(string Id, string Title);

/// <param name="Type">monday 的欄位種類：status、people、date、timeline、numbers、text、long_text、link、board_relation、file、checkbox、dropdown…</param>
/// <param name="Labels">只有 status 有：設定裡的標籤（照 index 排；空的標籤不列）。plan 用它列出要對應的狀態。</param>
internal sealed record MondayColumn(string Id, string Title, string Type, IReadOnlyList<string> Labels);

/// <param name="GroupId">子項目是空字串。</param>
/// <param name="State">monday 的 state：active、archived、deleted。</param>
/// <param name="CreatorId">建立的人；monday 的自動化建立的可能是 null。</param>
/// <param name="Url">monday 上的網址（每個項目都寫進「monday 原連結」欄位）。</param>
/// <param name="Assets">項目本身的檔案（檔案欄裡的）；留言與回覆裡的在各自的 Assets。</param>
internal sealed record MondayItem(
    string Id,
    string Name,
    string GroupId,
    string State,
    DateTimeOffset CreatedAt,
    string? CreatorId,
    string Url,
    IReadOnlyList<MondayColumnValue> Values,
    IReadOnlyList<MondayItem> Subitems,
    IReadOnlyList<MondayUpdate> Updates,
    IReadOnlyList<MondayAsset> Assets);

/// <param name="Text">monday 給的文字（數字、文字、連結、日期都看得到）；沒有值是 null 或空字串。</param>
/// <param name="ValueJson">monday 給的 value（JSON 字串，例如 date 是 {"date":"2026-11-04"}、timeline 是 {"from":…,"to":…}、link 是 {"url":…}）；沒有值是 null。</param>
/// <param name="Label">只有 status：標籤；沒有值是 null。</param>
/// <param name="PersonIds">只有 people：人（不含 team），照欄位裡的順序。</param>
/// <param name="LinkedItems">只有 board_relation：連到的項目（名稱用來對應「客戶」這類單選的選項）。</param>
internal sealed record MondayColumnValue(
    string ColumnId,
    string Type,
    string? Text,
    string? ValueJson,
    string? Label,
    IReadOnlyList<string> PersonIds,
    IReadOnlyList<MondayLinkedItem> LinkedItems);

internal sealed record MondayLinkedItem(string Id, string Name);

/// <param name="BodyHtml">monday 的 HTML（提及是 a.user_mention_editor[data-mention-id]，圖片是 img[data-asset_id]，到處夾著 U+FEFF）。</param>
/// <param name="Replies">回覆，照時間由舊到新。</param>
internal sealed record MondayUpdate(
    string Id,
    string? CreatorId,
    DateTimeOffset CreatedAt,
    string BodyHtml,
    IReadOnlyList<MondayAsset> Assets,
    IReadOnlyList<MondayReply> Replies);

internal sealed record MondayReply(
    string Id,
    string? CreatorId,
    DateTimeOffset CreatedAt,
    string BodyHtml,
    IReadOnlyList<MondayAsset> Assets);

/// <param name="SizeBytes">monday 報的大小；不知道是 null。</param>
/// <param name="UploadedById">上傳的人；不知道是 null。</param>
/// <param name="CreatedAt">上傳的時間；不知道是 null。</param>
/// <param name="LocalPath">fetch --files 下載下來的檔案（絕對路徑）；沒有下載是 null。</param>
internal sealed record MondayAsset(
    string Id,
    string Name,
    long? SizeBytes,
    string? UploadedById,
    DateTimeOffset? CreatedAt,
    string? LocalPath);
