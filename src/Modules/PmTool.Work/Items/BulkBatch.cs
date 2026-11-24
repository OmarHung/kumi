using System.Globalization;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 一次批次的編號：專案裡的、去掉重複的，與不是這個專案的（直接算失敗，不去查資料庫）。
/// <see cref="Order"/> 是請求裡第一次出現的順序，結果照它排。
/// </summary>
/// <param name="Valid">正式寫法的編號（專案代號大寫），依第一次出現的順序。</param>
internal sealed record BulkBatch(
    Guid ProjectId, string ProjectKey, IReadOnlyList<string> Order, IReadOnlyList<string> Valid, IReadOnlySet<string> Invalid)
{
    /// <summary>呼叫者在這個專案看得到的項目：這一批裡看不到的，查詢時照不存在處理，不透露它存在。</summary>
    public required ItemScope Scope { get; init; }

    /// <summary>這一批裡專案的流水號，查資料表用。</summary>
    public long[] ValidNumbers => [.. Valid.Select(key => long.Parse(key[(key.LastIndexOf('-') + 1)..], CultureInfo.InvariantCulture))];

    public static BulkBatch Of(ProjectEntity project, IReadOnlyList<string> keys, ItemScope scope)
    {
        var projectKey = project.Key;
        var order = new List<string>();
        var valid = new List<string>();
        var invalid = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in keys)
        {
            var canonical = ItemKeys.TryParse(raw, out var itemProject, out var number) && itemProject == projectKey
                ? ItemKeys.Format(projectKey, number)
                : null;
            var key = canonical ?? raw ?? string.Empty;
            if (!seen.Add(key))
            {
                continue;
            }

            order.Add(key);
            (canonical is null ? invalid : (ICollection<string>)valid).Add(key);
        }

        return new BulkBatch(project.Id, projectKey, order, valid, invalid) { Scope = scope };
    }

    /// <summary>不是這個專案的編號，和單一項目一樣是找不到。</summary>
    public Dictionary<string, Result<T>> InitialResults<T>() =>
        Invalid.ToDictionary(key => key, key => (Result<T>)WorkFailures.ItemNotFound(key), StringComparer.Ordinal);

    /// <summary>
    /// 一筆一筆做。<paramref name="isWaiting"/> 認為一筆是在等這一批裡的另一筆（上層等下層刪除、下層等上層復原）時先放著，
    /// 做完其他的再回頭重試，直到一輪下來沒有進展；剩下的算失敗，原因是最後一次的結果。
    /// </summary>
    public async Task<Dictionary<string, Result<T>>> RunAsync<T>(
        Func<string, Task<Result<T>>> run, Func<string, Failure, IReadOnlySet<string>, bool> isWaiting)
    {
        var results = InitialResults<T>();
        var pending = Valid.ToList();
        var progressed = true;
        while (pending.Count > 0 && progressed)
        {
            progressed = false;
            var inBatch = pending.ToHashSet(StringComparer.Ordinal);
            foreach (var key in pending.ToArray())
            {
                var result = await run(key);
                results[key] = result;
                if (result.Failure is { } failure && isWaiting(key, failure, inBatch))
                {
                    continue;
                }

                pending.Remove(key);
                inBatch.Remove(key);
                progressed = true;
            }
        }

        return results;
    }

    /// <summary>照請求的順序分成成功與失敗兩份。</summary>
    public (List<T> Succeeded, List<BulkItemFailure> Failed) Split<T>(IReadOnlyDictionary<string, Result<T>> results)
    {
        var succeeded = new List<T>();
        var failed = new List<BulkItemFailure>();
        foreach (var key in Order)
        {
            var result = results[key];
            if (result.Failure is { } failure)
            {
                failed.Add(new BulkItemFailure(key, failure));
            }
            else
            {
                succeeded.Add(result.Value);
            }
        }

        return (succeeded, failed);
    }
}
