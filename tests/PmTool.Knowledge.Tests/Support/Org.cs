using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Knowledge.Tests;

/// <summary>一個測試用的組織：自己的編號，所以同一個資料庫裡的測試互不影響。建立的人都登記在假的人員目錄裡。</summary>
internal sealed class Org
{
    private readonly KnowledgeTestHost _host;

    public Org(KnowledgeTestHost host)
    {
        _host = host;
        Id = Guid.NewGuid();
        Admin = Person(OrganizationRole.Admin);
    }

    public Guid Id { get; }

    public KnowledgeTestHost Host => _host;

    /// <summary>組織管理員（也是擁有者之外最常用的建立者）。</summary>
    public Actor Admin { get; }

    public Actor Owner() => Person(OrganizationRole.Owner);

    public Actor Member() => Person(OrganizationRole.Member);

    public Actor Guest() => Person(OrganizationRole.Guest);

    /// <summary>已經不在組織裡的人：身分模組找不到他。</summary>
    public Actor Stranger() => new(Id, Guid.NewGuid(), IsOrganizationAdmin: false);

    public Actor Person(OrganizationRole role)
    {
        var actor = new Actor(Id, Guid.NewGuid(), role is OrganizationRole.Owner or OrganizationRole.Admin);
        _host.People.Add(Id, actor.PersonId, role);
        return actor;
    }

    /// <summary>建一個組織空間（組織管理員建的）。</summary>
    public async Task<SpaceSummary> CreateSpaceAsync(string key = "HR")
    {
        var created = await _host.Spaces.CreateAsync(Admin, new NewSpace(key, $"空間 {key}", null, null), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        return created.Value;
    }

    /// <summary>建一個專案空間，並讓 <paramref name="manager"/> 在這個專案有管理權限。</summary>
    public async Task<(SpaceSummary Space, Guid ProjectId)> CreateProjectSpaceAsync(Actor manager, string key = "WEB")
    {
        var projectId = Guid.NewGuid();
        _host.Work.Grant(Id, manager.PersonId, projectId, key, FakeWorkAccessQueries.ManagerPermissions);
        var created = await _host.Spaces.CreateAsync(manager, new NewSpace(key, $"專案空間 {key}", null, key), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        return (created.Value, projectId);
    }

    /// <summary>新增一頁，失敗時讓測試直接停下來。</summary>
    public async Task<PageDetail> PageAsync(
        string spaceKey, string title, int? parent = null, int? before = null, string? body = null, Actor? actor = null)
    {
        var created = await _host.Pages.CreateAsync(
            actor ?? Admin, spaceKey, new NewPage(parent, title, body) { BeforeNumber = before }, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        return created.Value;
    }

    /// <summary>空間的樹（呼叫者看得到的），回傳「標題（上層編號）」的先序清單，測試用它一眼看出結構。</summary>
    public async Task<IReadOnlyList<string>> OutlineAsync(string spaceKey, Actor? actor = null)
    {
        var tree = await _host.Pages.GetTreeAsync(actor ?? Admin, spaceKey, CancellationToken.None);
        Assert.That(tree.Succeeded, Is.True, () => tree.Failure!.Message);
        var numbers = tree.Value.Pages.ToDictionary(p => p.Id, p => p.Number);
        return [.. tree.Value.Pages.Select(p => p.ParentId is { } parent ? $"{p.Title}<{numbers[parent]}" : p.Title)];
    }
}
