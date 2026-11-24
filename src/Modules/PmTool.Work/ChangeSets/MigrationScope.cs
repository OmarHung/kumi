using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Blueprint.Planning;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

/// <summary>
/// 一份變更集要對一個專案的資料做的事：目前的藍圖、新的藍圖，與計畫算出來的資料影響。
/// 預覽（算受影響的數量）與套用（真的搬移）看的是同一份，兩邊算出來的才會一致。
/// </summary>
internal sealed record MigrationScope(ProjectEntity Project, ProjectSchema Current, ProjectSchema Next, IReadOnlyList<DataImpact> Impacts)
{
    // 類型、狀態的 key 只能有小寫字母、數字與底線，類別是固定的文字，用冒號接起來不會撞在一起。
    private const string Separator = ":";

    public IEnumerable<StateMoveImpact> Moves => Impacts.OfType<StateMoveImpact>();

    public IEnumerable<FieldDetachImpact> Detaches => Impacts.OfType<FieldDetachImpact>();

    /// <summary>新藍圖裡用這個流程的類型。狀態搬移是對這些類型的項目做的。</summary>
    public string[] TypesUsing(string workflow) =>
        [.. Next.Blueprint.ItemTypes.Where(type => type.Workflow == workflow).Select(type => type.Key)];

    /// <summary>從欄位清單拿掉這個欄位的類型。這些類型的項目上的值直接封存，不做轉換。</summary>
    public string[] TypesDetaching(string field) => [.. Detaches.Where(d => d.Field == field).Select(d => d.ItemType)];

    public bool IsDetached(string itemType, string field) => Detaches.Any(d => d.ItemType == itemType && d.Field == field);

    /// <summary>
    /// 套用時會被改到的項目：狀態或類別和新藍圖對不上的（停在被拿掉的狀態、狀態換了類別、類型換了流程），
    /// 有被刪除或轉換的欄位的值的，以及類型不再有某個欄位、卻還有它的值的。只有這些項目會被鎖住與改寫。
    /// </summary>
    public IQueryable<ItemEntity> TouchedItems(WorkDbContext db)
    {
        var settled = SettledStates();
        var fields = Impacts.Select(FieldOf).OfType<string>().ToArray();
        var conditions = new List<Expression<Func<ItemEntity, bool>>>
        {
            i => !settled.Contains(i.Type + Separator + i.State + Separator + i.StateCategory),
        };
        if (fields.Length > 0)
        {
            conditions.Add(i => EF.Functions.JsonExistAny(i.Fields, fields));
        }

        // 拿掉的欄位只對那個類型算數：別的類型還有這個欄位，它們的值不能被當成要動的。
        foreach (var detached in Detaches.GroupBy(d => d.ItemType, d => d.Field))
        {
            var type = detached.Key;
            var keys = detached.ToArray();
            conditions.Add(i => i.Type == type && EF.Functions.JsonExistAny(i.Fields, keys));
        }

        return ItemsOf(db).Where(AnyOf(conditions));
    }

    /// <summary>
    /// 這個專案沒刪除的項目。影響的數量、擋住套用的原因、套用時要搬的項目都從這裡算：刪除的項目完全不算，
    /// 停在刪除當時的樣子，之後做復原時再照當時的藍圖對應。
    /// </summary>
    public IQueryable<ItemEntity> ItemsOf(WorkDbContext db) =>
        ItemStore.Live(db).Where(i => i.OrganizationId == Project.OrganizationId && i.ProjectId == Project.Id);

    private static string? FieldOf(DataImpact impact) => impact switch
    {
        FieldDropImpact drop => drop.Field,
        FieldConversionImpact conversion => conversion.Field,
        _ => null,
    };

    /// <summary>把幾個條件用 OR 接成一個，交給 EF 翻成一個 WHERE；條件的數量要看計畫，沒辦法直接寫成一個 lambda。</summary>
    private static Expression<Func<ItemEntity, bool>> AnyOf(IReadOnlyList<Expression<Func<ItemEntity, bool>>> conditions)
    {
        var item = conditions[0].Parameters[0];
        var body = conditions.Skip(1).Aggregate(
            conditions[0].Body, (combined, next) => Expression.OrElse(combined, new ParameterSwap(next.Parameters[0], item).Visit(next.Body)));
        return Expression.Lambda<Func<ItemEntity, bool>>(body, item);
    }

    /// <summary>新藍圖裡每個類型可以停的狀態與它的類別。項目的（類型、狀態、類別）在這裡面，就不必動。</summary>
    private string[] SettledStates() =>
        [.. Next.Blueprint.ItemTypes.SelectMany(type => Next.WorkflowOf(type).States.Select(state =>
            type.Key + Separator + state.Key + Separator + StateCategoryText.ToText(state.Category)))];

    private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
