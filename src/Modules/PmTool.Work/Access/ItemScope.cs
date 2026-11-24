using System.Linq.Expressions;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

/// <summary>一個人在一個專案能讀項目的範圍種類。預設值（0）是沒有，沒設定範圍的地方不會讀到任何東西。</summary>
internal enum ItemScopeKind
{
    None = 0,
    OwnOnly,
    All,
}

/// <summary>
/// 一個人在一個專案看得到的項目範圍：全部、只有自己的（負責或建立的，<see cref="WorkPermissions.ItemReadOwn"/>），或沒有（角色沒有任何讀取權限）。
/// 「自己的」的定義只在這個型別（<see cref="IsOwned"/> 與同一個規則的運算式 <see cref="Owned"/>，後者給資料庫查詢用）：
/// 清單、計數、搜尋用 <see cref="Apply"/> 放進查詢條件（分頁與計數才會對），單一項目用 <see cref="Contains"/>。
/// </summary>
internal readonly record struct ItemScope(ItemScopeKind Kind, Guid PersonId)
{
    public static ItemScope All(Guid personId) => new(ItemScopeKind.All, personId);

    public bool OwnOnly => Kind == ItemScopeKind.OwnOnly;

    /// <summary>項目是他負責或建立的。「自己的」的唯一定義；要放進資料庫查詢時用同一個規則的 <see cref="Owned"/>。</summary>
    public static bool IsOwned(Guid personId, Guid createdBy, Guid? assigneeId) => assigneeId == personId || createdBy == personId;

    /// <summary><see cref="IsOwned"/> 的運算式版本：EF 不能翻譯方法呼叫，所以寫成一對，兩者相鄰、改的時候一起改。</summary>
    public static Expression<Func<ItemEntity, bool>> Owned(Guid personId) => i => i.AssigneeId == personId || i.CreatedBy == personId;

    public IQueryable<ItemEntity> Apply(IQueryable<ItemEntity> items) => Kind switch
    {
        ItemScopeKind.All => items,
        ItemScopeKind.OwnOnly => items.Where(Owned(PersonId)),
        _ => items.Where(i => false),
    };

    public bool Contains(ItemEntity item) => Kind switch
    {
        ItemScopeKind.All => true,
        ItemScopeKind.OwnOnly => IsOwned(PersonId, item.CreatedBy, item.AssigneeId),
        _ => false,
    };

    /// <summary>
    /// 跨專案的範圍：<paramref name="allProjects"/> 的項目全部、<paramref name="ownOnlyProjects"/> 的只有自己的。
    /// 「自己的」照 <see cref="Owned"/>，不另寫一份。
    /// </summary>
    public static IQueryable<ItemEntity> Across(
        IQueryable<ItemEntity> items, IReadOnlyCollection<Guid> allProjects, IReadOnlyCollection<Guid> ownOnlyProjects, Guid personId)
    {
        var all = allProjects.ToArray();
        var own = ownOnlyProjects.ToArray();
        Expression<Func<ItemEntity, bool>> inAll = i => all.Contains(i.ProjectId);
        if (own.Length == 0)
        {
            return items.Where(inAll);
        }

        Expression<Func<ItemEntity, bool>> inOwn = i => own.Contains(i.ProjectId);
        var parameter = inAll.Parameters[0];
        var ownedExpression = Owned(personId);
        var owned = new Rebind(ownedExpression.Parameters[0], parameter).Visit(ownedExpression.Body);
        var ownInOwnProjects = Expression.AndAlso(new Rebind(inOwn.Parameters[0], parameter).Visit(inOwn.Body), owned);
        return items.Where(Expression.Lambda<Func<ItemEntity, bool>>(Expression.OrElse(inAll.Body, ownInOwnProjects), parameter));
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
