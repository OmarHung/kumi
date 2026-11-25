using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 複合外鍵只保證上層在同一個空間，擋不住兩頁互為上層。資料萬一真的成環，每一條讀寫的路都要在幾秒內回應，
/// 不能在樹上無限地走；環上的頁面對一般人當作不存在。
/// </summary>
[TestFixture]
public sealed class CycleGuardTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task EveryReadAndWritePathByAMember_OnAPageInACycle_AnswersNotFoundQuickly()
    {
        // Arrange
        var scene = await CycleAsync();
        var member = scene.Org.Member();

        // Act
        var get = await Within(_host.Pages.GetAsync(member, "HR", scene.A.Number, CancellationToken.None));
        var update = await Within(_host.Pages.UpdateAsync(member, "HR", scene.A.Number, new PageChanges("新", null, 1), CancellationToken.None));
        var move = await Within(_host.Pages.MoveAsync(member, "HR", scene.A.Number, new PageMove(null, null), CancellationToken.None));
        var delete = await Within(_host.Pages.DeleteAsync(member, "HR", scene.A.Number, CancellationToken.None));
        var restriction = await Within(_host.Pages.GetRestrictionAsync(member, "HR", scene.B.Number, CancellationToken.None));
        var restore = await Within(_host.Pages.RestoreAsync(member, "HR", scene.B.Number, CancellationToken.None));
        var tree = await Within(_host.Pages.GetTreeAsync(member, "HR", CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(update.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(move.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(delete.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(restriction.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(restore.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(tree.Value.Pages.Select(p => p.Number), Is.EqualTo(new[] { scene.Plain.Number }));
        }
    }

    [Test]
    public async Task ReadingAndRestrictingAPageInACycle_ByAnAdmin_Finishes()
    {
        // Arrange
        var scene = await CycleAsync();

        // Act
        var get = await Within(_host.Pages.GetAsync(scene.Org.Admin, "HR", scene.A.Number, CancellationToken.None));
        var restricted = await Within(_host.Pages.SetRestrictionAsync(scene.Org.Admin, "HR", scene.B.Number, [scene.Org.Admin.PersonId], CancellationToken.None));
        var created = await Within(_host.Pages.CreateAsync(scene.Org.Admin, "HR", new NewPage(scene.A.Number, "子", null), CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(get.Succeeded, Is.True);
            Assert.That(restricted.Succeeded, Is.True);
            Assert.That(created.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task DeletingListingAndRestoringAPageInACycle_ByAnAdmin_Finishes()
    {
        // Arrange
        var scene = await CycleAsync();

        // Act
        var deleted = await Within(_host.Pages.DeleteAsync(scene.Org.Admin, "HR", scene.A.Number, CancellationToken.None));
        var trash = await Within(_host.Pages.ListTrashAsync(scene.Org.Admin, "HR", new TrashQuery(), CancellationToken.None));
        var restored = await Within(_host.Pages.RestoreAsync(scene.Org.Admin, "HR", scene.B.Number, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(trash.Succeeded, Is.True);
            Assert.That(restored.Succeeded, Is.True, () => restored.Failure?.Message ?? "");
        }
    }

    [Test]
    public async Task MovingAPageInACycleOutOfIt_ByAnAdmin_Finishes()
    {
        // Arrange
        var scene = await CycleAsync();

        // Act
        var moved = await Within(_host.Pages.MoveAsync(scene.Org.Admin, "HR", scene.A.Number, new PageMove(scene.Plain.Number, null), CancellationToken.None));

        // Assert
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure?.Message ?? "");
    }

    [Test]
    public async Task DeletingAPageBelowTheCycle_ByAnAdmin_FindsItsPlaceInTheTrashWithoutLooping()
    {
        // Arrange
        var scene = await CycleAsync();
        var below = await scene.Org.PageAsync("HR", "環下面的頁面", parent: scene.B.Number);
        var otherAdmin = scene.Org.Owner();

        // Act
        var deleted = await Within(_host.Pages.DeleteAsync(otherAdmin, "HR", below.Number, CancellationToken.None));
        var trash = await Within(_host.Pages.ListTrashAsync(otherAdmin, "HR", new TrashQuery(), CancellationToken.None));
        var restored = await Within(_host.Pages.RestoreAsync(otherAdmin, "HR", below.Number, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(trash.Value.Items.Select(e => e.Page.Number), Is.EqualTo(new[] { below.Number }));
            Assert.That(restored.Succeeded, Is.True);
        }
    }

    private static async Task<T> Within<T>(Task<T> operation) => await operation.WaitAsync(Deadline);

    /// <summary>A 與 B 互為上層，另外有一個沒有關係的頁面。</summary>
    private async Task<Scene> CycleAsync()
    {
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "甲");
        var b = await org.PageAsync("HR", "乙", parent: a.Number);
        var plain = await org.PageAsync("HR", "不相干");
        await using var db = await _host.CreateDbContextAsync();
        await db.Pages.Where(p => p.Id == a.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.ParentId, b.Id));
        return new Scene(org, a, b, plain);
    }

    private sealed record Scene(Org Org, PageDetail A, PageDetail B, PageDetail Plain);
}
