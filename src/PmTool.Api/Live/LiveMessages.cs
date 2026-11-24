namespace PmTool.Api.Live;

/// <summary>
/// 伺服器推給網頁的訊息名稱與形狀（網頁不會呼叫伺服器的方法）。只推「哪些東西變了」，不推內容：
/// 網頁收到之後讓對應的快取失效、用 API 重抓，看得到什麼仍然由 API 的權限決定。
/// </summary>
public static class LiveMessages
{
    public const string Items = "items";

    public const string Inbox = "inbox";
}

/// <summary>
/// 這個專案的這些項目有變動；<paramref name="ItemKeys"/> 是 null 表示變動太多，整個專案重抓。
/// <paramref name="ActorId"/> 是這一則裡所有事件的作者（都是同一個人時），否則 null：網頁用它略過「這個分頁自己剛改的」。
/// </summary>
public sealed record LiveItemsMessage(string ProjectKey, IReadOnlyList<string>? ItemKeys, Guid? ActorId);

/// <summary>自己的收件匣有變化（新的通知、別的分頁標成已讀）。</summary>
public sealed record LiveInboxMessage;

/// <summary>
/// 群組的名稱。「全部看得到」的專案放進 <c>project:</c>；「只看自己的」放進各自的 <c>own:</c>，
/// 只收到負責人、建立者或前一個負責人是自己的項目。<c>person:</c> 是這個人所有分頁，收件匣用。
/// </summary>
public static class LiveGroups
{
    public static string Project(Guid projectId) => $"project:{projectId:D}";

    public static string Own(Guid projectId, Guid personId) => $"own:{projectId:D}:{personId:D}";

    public static string Person(Guid personId) => $"person:{personId:D}";
}
