using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <param name="DisplayName">這個人已經不在組織裡時是 null。</param>
internal sealed record AuditPersonData(Guid PersonId, string? DisplayName);

internal sealed record AuditEntryData(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string Action,
    string Outcome,
    AuditPersonData? Actor,
    AuditPersonData? Target,
    string? SourceAddress,
    IReadOnlyDictionary<string, string> Details)
{
    public static AuditEntryData From(AuditEntry entry, PeopleNames names) => new(
        entry.EventId,
        entry.OccurredAt,
        entry.Action,
        entry.Outcome,
        Person(entry.ActorPersonId, names),
        Person(entry.TargetPersonId, names),
        entry.SourceAddress,
        entry.Details);

    private static AuditPersonData? Person(Guid? personId, PeopleNames names) =>
        personId is { } id ? new AuditPersonData(id, names.Find(id)?.DisplayName) : null;
}

internal sealed record AuditPageData(IReadOnlyList<AuditEntryData> Items, string? NextCursor);
