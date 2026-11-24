using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>回應裡的人要帶顯示名稱。一個回應用到的人一次查完，不要每個人查一次。</summary>
internal sealed class PeopleNames
{
    /// <summary>
    /// 查不到的人：已經離開組織，或被刪掉了。專案成員與負責人可能還記著他的編號，
    /// 給固定的替代文字，整個回應才不會因為一個人而失敗。
    /// </summary>
    public const string Unknown = "（已不在組織）";

    private static readonly PeopleNames None = new(new Dictionary<Guid, PersonSummary>());

    private readonly IReadOnlyDictionary<Guid, PersonSummary> _people;

    private PeopleNames(IReadOnlyDictionary<Guid, PersonSummary> people) => _people = people;

    public static async Task<PeopleNames> LookupAsync(
        IPeopleDirectory directory, Guid organizationId, IEnumerable<Guid> personIds, CancellationToken cancellationToken)
    {
        Guid[] distinct = [.. personIds.Distinct()];
        return distinct.Length == 0
            ? None
            : new PeopleNames(await directory.FindManyAsync(organizationId, distinct, cancellationToken));
    }

    public string NameOf(Guid personId) => _people.TryGetValue(personId, out var person) ? person.DisplayName : Unknown;

    public PersonReference Reference(Guid personId) => new(personId, NameOf(personId));

    public PersonSummary? Find(Guid personId) => _people.GetValueOrDefault(personId);
}
