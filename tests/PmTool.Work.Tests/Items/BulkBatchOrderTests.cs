using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Items;

namespace PmTool.Work.Tests.Items;

/// <summary>批次的順序與嘗試次數：先依上下層排好，再做；總嘗試次數有上限，不會因為一條長鏈而跑出平方次。</summary>
[TestFixture]
public sealed class BulkBatchOrderTests
{
    private const int ChainLength = 200;

    [Test]
    public async Task RunAsync_ChainOfParentsListedFirst_DeletesAllWithinTwiceTheItemCount()
    {
        // Arrange：WEB-1 是最上層，WEB-n 掛在 WEB-(n-1) 底下；請求的順序是上層在前，刪除要下層在前。
        var keys = Enumerable.Range(1, ChainLength).Select(n => $"WEB-{n}").ToList();
        var parents = Enumerable.Range(2, ChainLength - 1).ToDictionary(n => $"WEB-{n}", n => $"WEB-{n - 1}");
        var batch = BatchOf(keys);
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        var attempts = 0;

        // Act
        var ordered = batch.Sorted(parents, parentsFirst: false);
        var results = await batch.RunAsync<string>(
            key =>
            {
                attempts++;
                var hasLiveChild = deleted.Count < ChainLength && parents.Any(p => p.Value == key && !deleted.Contains(p.Key));
                if (hasLiveChild)
                {
                    return Task.FromResult<Result<string>>(WaitFailure());
                }

                deleted.Add(key);
                return Task.FromResult<Result<string>>(key);
            },
            (_, failure, _) => failure.Code == "waiting",
            ordered);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Values.Count(r => r.Failure is null), Is.EqualTo(ChainLength));
            Assert.That(attempts, Is.LessThanOrEqualTo(ChainLength * 2));
        }
    }

    [Test]
    public async Task RunAsync_NeverMakesProgress_StopsAtTwiceTheItemCountAndKeepsTheLastFailure()
    {
        // Arrange
        var keys = Enumerable.Range(1, 50).Select(n => $"WEB-{n}").ToList();
        var batch = BatchOf(keys);
        var attempts = 0;

        // Act
        var results = await batch.RunAsync<string>(
            _ =>
            {
                attempts++;
                return Task.FromResult<Result<string>>(WaitFailure());
            },
            (_, _, _) => true,
            batch.Valid);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.LessThanOrEqualTo(keys.Count * 2));
            Assert.That(results.Values.Select(r => r.Failure?.Code), Is.All.EqualTo("waiting"));
        }
    }

    [Test]
    public void Sorted_ForRestore_PutsParentsBeforeTheirChildren()
    {
        // Arrange
        var batch = BatchOf(["WEB-3", "WEB-2", "WEB-1", "WEB-9"]);
        var parents = new Dictionary<string, string> { ["WEB-2"] = "WEB-1", ["WEB-3"] = "WEB-2", ["WEB-1"] = "WEB-77" };

        // Act
        var ordered = batch.Sorted(parents, parentsFirst: true);

        // Assert
        Assert.That(ordered.ToList().IndexOf("WEB-1"), Is.LessThan(ordered.ToList().IndexOf("WEB-2")));
        Assert.That(ordered.ToList().IndexOf("WEB-2"), Is.LessThan(ordered.ToList().IndexOf("WEB-3")));
        Assert.That(ordered, Is.EquivalentTo(batch.Valid));
    }

    [Test]
    public void Sorted_ParentCycle_DoesNotLoopForever()
    {
        // Arrange
        var batch = BatchOf(["WEB-1", "WEB-2"]);
        var parents = new Dictionary<string, string> { ["WEB-1"] = "WEB-2", ["WEB-2"] = "WEB-1" };

        // Act
        var ordered = batch.Sorted(parents, parentsFirst: false);

        // Assert
        Assert.That(ordered, Is.EquivalentTo(batch.Valid));
    }

    private static Failure WaitFailure() => new(FailureKind.Conflict, "waiting", "等下層");

    private static BulkBatch BatchOf(IReadOnlyList<string> keys) =>
        new(Guid.NewGuid(), "WEB", keys, keys, new HashSet<string>()) { Scope = ItemScope.All(Guid.NewGuid()) };
}
