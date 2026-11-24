using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>假的服務回傳的資料。內容只求形狀正確，規則由模組自己的測試負責。</summary>
internal static class TestData
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 11, 13, 1, 2, 3, TimeSpan.Zero);

    public static readonly DateTimeOffset UpdatedAt = new(2026, 11, 13, 4, 5, 6, TimeSpan.Zero);

    /// <summary>第一筆活動的時刻；第 n 筆是它加 n 分鐘。</summary>
    public static readonly DateTimeOffset FirstActivityAt = new(2026, 11, 14, 7, 0, 0, TimeSpan.Zero);

    public static ItemActivity Activity(ActivityKind kind, Guid actorId, int sequence = 1, string itemKey = "WEB-1") =>
        new(ActivityId(sequence), itemKey, kind, actorId, FirstActivityAt.AddMinutes(sequence));

    public static Guid ActivityId(int sequence) =>
        Guid.Parse($"0190a000-0000-7000-8000-{sequence:x12}");

    /// <summary>變更集送出的時刻；第 n 份是它加 n 小時。</summary>
    public static readonly DateTimeOffset FirstProposedAt = new(2026, 11, 15, 1, 0, 0, TimeSpan.Zero);

    /// <summary>一份等待核准的變更集，基準版本是 1。</summary>
    public static ChangeSetSummary ChangeSet(long number, Guid proposedBy) =>
        new(number, ChangeSetStatus.Pending, 1, $"第 {number} 份變更", proposedBy, FirstProposedAt.AddHours(number));

    /// <summary>同一份變更集被套用或拒絕之後的樣子，決定的時刻是送出後 30 分鐘。</summary>
    public static ChangeSetSummary Decided(ChangeSetSummary changeSet, ChangeSetStatus status, Guid decidedBy) =>
        changeSet with
        {
            Status = status,
            DecidedBy = decidedBy,
            DecidedAt = changeSet.ProposedAt.AddMinutes(30),
            AppliedVersion = status == ChangeSetStatus.Applied ? changeSet.BaseVersion + 1 : null,
        };

    /// <summary>沒有差異、影響與擋住原因的內容；測試要看哪一部分就自己換掉那一部分。</summary>
    public static ChangeSetDetail ChangeSetDetail(ChangeSetSummary changeSet) =>
        new(changeSet, Blueprint(), [], [], [], changeSet.Status == ChangeSetStatus.Pending) { BaseBlueprint = Blueprint() };

    public static ProjectSummary Project(string key = "WEB", string? myRole = "admin", DateTimeOffset? archivedAt = null) =>
        new(Guid.Parse("0190a000-0000-7000-8000-0000000000c1"), key, "Example 網站", 1, myRole, CreatedAt) { ArchivedAt = archivedAt };

    public static ProjectBlueprint Blueprint() => new()
    {
        ApiVersion = "blueprint/v0",
        Kind = "ProjectBlueprint",
        Metadata = new BlueprintMetadata { Key = "web", Name = "Example 網站" },
        Fields = [],
        Workflows =
        [
            new Workflow
            {
                Key = "main",
                Name = "主要流程",
                States =
                [
                    new WorkflowState { Key = "todo", Name = "待辦", Category = StateCategory.NotStarted },
                    new WorkflowState { Key = "doing", Name = "進行中", Category = StateCategory.InProgress },
                ],
            },
        ],
        ItemTypes = [new ItemType { Key = "task", Name = "任務", Workflow = "main" }],
    };

    public static WorkItem Item(string key = "WEB-1", Guid? assigneeId = null) => new(
        key,
        "WEB",
        long.Parse(key[(key.IndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture),
        "task",
        "寫首頁",
        "doing",
        StateCategory.InProgress,
        assigneeId,
        Priority.High,
        new DateOnly(2026, 11, 1),
        new DateOnly(2026, 11, 30),
        2.5m,
        null,
        new Dictionary<string, JsonElement> { ["impact"] = JsonSerializer.SerializeToElement("high") },
        CreatedAt,
        UpdatedAt);
}
