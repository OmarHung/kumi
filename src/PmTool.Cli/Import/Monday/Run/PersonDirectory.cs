using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>這個伺服器上的一個人。</summary>
internal sealed record ServerPerson(Guid Id, string DisplayName, string Email)
{
    public static ServerPerson? From(JsonElement person) =>
        Guid.TryParse(person.Text("personId"), out var id) && person.Text("email") is { Length: > 0 } email
            ? new ServerPerson(id, person.Text("displayName") ?? email, email)
            : null;
}

/// <summary>某個 monday 的人在這裡是誰：<see cref="UsedFallback"/> 是對不上、改用 fallbackPerson。</summary>
internal sealed record ResolvedPerson(ServerPerson Person, bool UsedFallback, string OriginalName);

/// <summary>monday 的人 → 這裡的人：對應檔的 people（Email）對伺服器的人員清單（不分大小寫）。</summary>
internal sealed class PersonDirectory
{
    private readonly Dictionary<string, ServerPerson> _byEmail;
    private readonly Dictionary<Guid, ServerPerson> _byId;
    private readonly IReadOnlyDictionary<string, string?> _mapping;
    private readonly Dictionary<string, string> _mondayNames;

    public PersonDirectory(MondayMapping mapping, MondayDump dump, IEnumerable<ServerPerson> people)
    {
        _byEmail = people.GroupBy(person => person.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _byId = _byEmail.Values.ToDictionary(person => person.Id);
        _mapping = mapping.People;
        _mondayNames = dump.Users.GroupBy(user => user.Id).ToDictionary(group => group.Key, group => group.First().Name);
        FallbackEmail = mapping.FallbackPerson;
        Fallback = FallbackEmail is null ? null : _byEmail.GetValueOrDefault(FallbackEmail);
    }

    public string? FallbackEmail { get; }

    /// <summary>fallbackPerson 在這個伺服器上的人；沒有設定或找不到是 null。</summary>
    public ServerPerson? Fallback { get; }

    /// <summary>對得上的人；對不上（沒寫、寫 null、Email 在這裡找不到）是 null。</summary>
    public ServerPerson? Match(string? mondayId) =>
        mondayId is not null && _mapping.TryGetValue(mondayId, out var email) && email is not null ? _byEmail.GetValueOrDefault(email) : null;

    public string NameOf(Guid personId) => _byId.TryGetValue(personId, out var person) ? person.DisplayName : personId.ToString();

    /// <summary>這個 monday 的人為什麼對不上，寫給使用者看的一句話（對應檔沒寫、寫 null，還是 Email 在這裡找不到）。</summary>
    public string WhyUnmatched(string? mondayId) =>
        mondayId is not null && _mapping.TryGetValue(mondayId, out var email) && email is not null
            ? $"對應檔寫的 Email {email} 在這個伺服器上找不到"
            : "對應檔裡沒有對應的 Email（people 是 null 或沒列）";

    public string MondayName(string? mondayId) =>
        mondayId is not null && _mondayNames.TryGetValue(mondayId, out var name) ? name : "不明的人";

    /// <summary>作者、建立的人、上傳的人：對得上用他，對不上改用 fallbackPerson；兩個都沒有是 null（呼叫端回報問題）。</summary>
    public ResolvedPerson? Author(string? mondayId)
    {
        if (Match(mondayId) is { } person)
        {
            return new ResolvedPerson(person, false, MondayName(mondayId));
        }

        return Fallback is null ? null : new ResolvedPerson(Fallback, true, MondayName(mondayId));
    }

    /// <summary><see cref="Author"/>，兩個都沒有時記成問題（一個 monday 的人一句話，數有幾次）並回 null。</summary>
    public ResolvedPerson? Require(string? mondayId, Notes issues)
    {
        var person = Author(mondayId);
        if (person is null)
        {
            var reason = FallbackEmail is null ? "也沒有設定 fallbackPerson" : $"fallbackPerson {FallbackEmail} 在這個伺服器上找不到";
            issues.Add(NoteKinds.Person, $"monday 的「{MondayName(mondayId)}」（編號 {mondayId ?? "無"}）對不上這裡的人（{WhyUnmatched(mondayId)}），{reason}；"
                + "他建立的項目、留言、上傳的檔案沒辦法署名。");
        }

        return person;
    }
}
