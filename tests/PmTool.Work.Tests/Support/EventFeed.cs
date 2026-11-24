using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>透過公開介面讀事件流。同一個資料庫裡有別的測試的事件，所以只留這個情境的組織。</summary>
internal static class EventFeed
{
    private const int Everything = 10_000;

    public static async Task<IReadOnlyList<WorkEvent>> OfAsync(WorkTestHost host, Scenario scenario) =>
        [.. (await host.Events.ReadAfterAsync(WorkEventPosition.Start, Everything, CancellationToken.None))
            .Where(e => e.OrganizationId == scenario.OrganizationId)];

    public static async Task<WorkEvent> LastAsync(WorkTestHost host, Scenario scenario) => (await OfAsync(host, scenario))[^1];

    public static string Mention(Actor person, string name = "某人") => $"@[{name}](mention:{person.PersonId:D})";
}
