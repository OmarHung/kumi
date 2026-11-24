using System.Globalization;

namespace PmTool.Cli.Import.Monday;

/// <summary>plan 的結果：對應檔的草稿與寫在它旁邊的說明。</summary>
internal sealed record MondayMappingDraft(MondayMapping Mapping, MappingAnnotations Annotations);

/// <summary>
/// 從 dump 猜一份對應檔的草稿（internal/HANDOFF-monday-import.md 第 5 節）：能確定的先填（人員依 Email、欄依種類、優先度依英文名稱、週期依群組標題），
/// 要人決定的留白並在註解寫出 monday 上的名稱與數量。猜錯的代價是匯錯東西，所以只猜規則明確的。
/// </summary>
internal static class MondayPlanner
{
    public static MondayMappingDraft Draft(MondayDump dump, IReadOnlyList<string> knownEmails, string dumpPathText)
    {
        var notes = new MappingAnnotations();
        notes.Header.AddRange(
        [
            "monday 匯入的對應（kumi import monday plan 產生的草稿）。改好之後：",
            "  kumi import monday fetch --board <編號> --out <dump 資料夾> --mapping <這個檔> --files   只抓要匯的群組並下載檔案",
            "  kumi import monday run --mapping <這個檔> --dry-run   檢查",
            "要填的地方是留白的（鍵後面沒有值）：群組的 project 與 type、狀態的對應、要匯的欄位。不匯的群組，把它整個改成「群組編號: skip」。",
            "這個檔裡有真實的人名、Email 與名稱，只放在自己的電腦，不要提交進版控。",
        ]);
        var boards = dump.Boards.ToDictionary(board => board.Id, board => MondayBoardPlanner.Draft(board, notes, dump.FetchedAt));
        var mapping = new MondayMapping
        {
            Dump = dumpPathText,
            People = DraftPeople(dump, knownEmails, notes),
            Boards = boards,
        };
        notes.Note("fallbackPerson", "對不上的作者、建立的人改用這個人（Email）；留白的話，對不上就停下來");
        notes.Note("assignee", "多人負責時不當負責人的人（monday 人員編號），例如 PM");
        notes.Note("followCommenters", "留過言的人自動關注那個項目");
        notes.Note("includeArchived", "monday 上封存的項目也匯");
        notes.Note("people", "monday 的人（編號）→ 這裡的人（Email）；null 是對不上");
        return new MondayMappingDraft(mapping, notes);
    }

    private static Dictionary<string, string?> DraftPeople(MondayDump dump, IReadOnlyList<string> knownEmails, MappingAnnotations notes)
    {
        var known = knownEmails.ToDictionary(email => email, email => email, StringComparer.OrdinalIgnoreCase);
        var users = dump.Users.GroupBy(user => user.Id).ToDictionary(group => group.Key, group => group.First());
        var comments = CommentCounts(dump);
        var people = new Dictionary<string, string?>();
        foreach (var id in ReferencedPeople(dump)
                     .OrderByDescending(id => comments.GetValueOrDefault(id))
                     .ThenBy(id => long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue)
                     .ThenBy(id => id, StringComparer.Ordinal))
        {
            users.TryGetValue(id, out var user);
            var match = user?.Email is { } email && known.TryGetValue(email, out var found) ? found : null;
            people[id] = match;
            notes.Note(MappingAnnotations.Key("people", id), DescribePerson(user, match, comments.GetValueOrDefault(id)));
        }

        return people;
    }

    private static string DescribePerson(MondayUser? user, string? match, int comments)
    {
        var count = comments > 0 ? $"{comments} 則留言" : "沒有留言";
        if (user is null)
        {
            return $"monday 的使用者清單裡沒有這個編號（{count}）";
        }

        var reason = match is not null ? string.Empty
            : user.Email is null ? "；monday 沒有給 Email" : "；這裡沒有同樣 Email 的人";
        return $"{user.Name}（{count}）{reason}";
    }

    /// <summary>每個人在整份 dump 裡寫的留言與回覆數（子項目的也算）。</summary>
    private static Dictionary<string, int> CommentCounts(MondayDump dump)
    {
        var counts = new Dictionary<string, int>();
        foreach (var update in AllItems(dump).SelectMany(item => item.Updates))
        {
            foreach (var author in new[] { update.CreatorId }.Concat(update.Replies.Select(reply => reply.CreatorId)).OfType<string>())
            {
                counts[author] = counts.GetValueOrDefault(author) + 1;
            }
        }

        return counts;
    }

    /// <summary>dump 裡出現過的人：建立者、留言與回覆的作者、檔案的上傳者、人員欄裡的人。沒出現過的人不列，免得草稿被整個公司的人填滿。</summary>
    private static HashSet<string> ReferencedPeople(MondayDump dump)
    {
        var ids = new HashSet<string>();
        foreach (var item in AllItems(dump))
        {
            ids.Add(item.CreatorId ?? string.Empty);
            ids.UnionWith(item.Values.SelectMany(value => value.PersonIds));
            ids.UnionWith(item.Assets.Select(asset => asset.UploadedById ?? string.Empty));
            foreach (var update in item.Updates)
            {
                ids.Add(update.CreatorId ?? string.Empty);
                ids.UnionWith(update.Assets.Select(asset => asset.UploadedById ?? string.Empty));
                ids.UnionWith(update.Replies.Select(reply => reply.CreatorId ?? string.Empty));
                ids.UnionWith(update.Replies.SelectMany(reply => reply.Assets).Select(asset => asset.UploadedById ?? string.Empty));
            }
        }

        ids.Remove(string.Empty);
        return ids;
    }

    internal static IEnumerable<MondayItem> AllItems(MondayDump dump) =>
        dump.Boards.SelectMany(board => board.Items).SelectMany(item => new[] { item }.Concat(item.Subitems));
}
