namespace PmTool.Cli.Import.Monday.Run;

/// <summary>
/// 封存的專案：搬家時做完的案子匯完就封存，切換期間再跑一次 run 時，它們不該擋住其他專案。
/// 沒有新的項目與留言就整個略過（週期、成員、項目、留言都不碰；封存的專案本來也寫不進去）；
/// 有新的東西時在寫入任何東西之前停下，請使用者先取消封存。項目本身的檔案不算：搬不了的檔案（太大、被拒絕）每次都還在，
/// 算進去的話封存的專案永遠過不了。
/// </summary>
internal static class ArchivedProjects
{
    /// <returns>這一次略過的專案代號。</returns>
    public static async Task<IReadOnlySet<string>> SettleAsync(HistoryApi api, ImportPlan plan, ImportState state, ImportReport report)
    {
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in plan.Projects.Values.Where(project => project.Info.IsArchived))
        {
            var key = project.Info.Key;
            var (items, comments) = await PendingAsync(api, project, state);
            if (items > 0 || comments > 0)
            {
                throw new CliException(
                    ExitCodes.Invalid,
                    $"專案 {key} 已經封存，但有 {Describe(items, comments)}要匯；先在網頁上取消封存再跑一次（匯完可以再封存）。");
            }

            skipped.Add(key);
            report.Reminders.Add(NoteKinds.Project, $"專案 {key} 已經封存，沒有新的東西，略過。");
        }

        return skipped;
    }

    private static async Task<(int Items, int Comments)> PendingAsync(HistoryApi api, ProjectPlan project, ImportState state)
    {
        var items = project.Items.Where(item => !state.Excluded.Contains(item.ExternalId)).ToArray();
        var newItems = items.Count(item => !state.PreExisting.Contains(item.ExternalId));
        var commentIds = items
            .Where(item => state.PreExisting.Contains(item.ExternalId))
            .SelectMany(item => CommentThreads.Flatten(item.Source).Select(comment => comment.Id))
            .ToArray();
        var known = await api.LookupAsync("comment", commentIds);
        return (newItems, commentIds.Count(id => !known.ContainsKey(id)));
    }

    private static string Describe(int items, int comments) =>
        string.Join("、", new[] { items > 0 ? $"{items} 個新的項目" : null, comments > 0 ? $"{comments} 則新的留言" : null }.OfType<string>());
}
